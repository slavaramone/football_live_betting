using System.Globalization;
using System.Text.Json;
using LiveTotalsHelper.Modeling;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalPriceOptions
{
    public string ModelPath { get; set; } = string.Empty;
    public string StateCorrectionPath { get; set; } = string.Empty;
    public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
    public double StartingLine { get; set; }
    public double StartingOverOdds { get; set; }
    public double StartingUnderOdds { get; set; }
    public int Minute { get; set; }
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    public double EmpiricalWeight { get; set; } = 0.80;
    public double EdgeThreshold { get; set; } = 0.10;
    public int HomeRedCards { get; set; }
    public int AwayRedCards { get; set; }
    public int LastGoalMinute { get; set; } = -1;
    public int RecentGoalMinutes { get; set; } = 2;
    public double VolumeFactor { get; set; } = 1.0;
    public string VolumeFactorSource { get; set; } = "manual/default";
    public List<double> TargetLines { get; } = [1.5, 2.0, 2.5, 3.0];
    public Dictionary<double, double> LiveOverOddsByLine { get; } = new();
    public Dictionary<double, double> LiveUnderOddsByLine { get; } = new();
}

public sealed class LiveTotalPriceResult
{
    public string ModelPath { get; set; } = string.Empty;
    public string StateCorrectionPath { get; set; } = string.Empty;
    public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
    public string League { get; set; } = string.Empty;
    public int Minute { get; set; }
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    public int CurrentGoals => HomeGoals + AwayGoals;
    public string ScoreState { get; set; } = string.Empty;
    public string DetailedScoreState { get; set; } = string.Empty;
    public string SelectedTimingGroup { get; set; } = string.Empty;
    public string TimingFallback { get; set; } = string.Empty;
    public double StartingLine { get; set; }
    public double StartingOverOdds { get; set; }
    public double StartingUnderOdds { get; set; }
    public double StartingFairOverProbability { get; set; }
    public double StartingTotalXg { get; set; }
    public double EmpiricalWeight { get; set; }
    public double WeibullWeight => 1.0 - EmpiricalWeight;
    public double WeibullRemainingShare { get; set; }
    public double EmpiricalRemainingShare { get; set; }
    public double TimingRemainingShare { get; set; }
    public double RemainingXgBeforeStateCorrection { get; set; }
    public double StateCorrectionFactor { get; set; } = 1.0;
    public bool StateCorrectionSupported { get; set; } = true;
    public string StateCorrectionSource { get; set; } = "none/default 1.0";
    public double RemainingXgBeforeVolume { get; set; }
    public double VolumeFactor { get; set; } = 1.0;
    public string VolumeFactorSource { get; set; } = string.Empty;
    public double RemainingXg { get; set; }
    public int HomeRedCards { get; set; }
    public int AwayRedCards { get; set; }
    public string RedCardWarning { get; set; } = string.Empty;
    public int LastGoalMinute { get; set; }
    public bool HasRecentGoal { get; set; }
    public string RecentGoalWarning { get; set; } = string.Empty;
    public List<LiveTotalLinePrice> Lines { get; } = [];
    public List<string> Warnings { get; } = [];
}

public sealed class LiveTotalLinePrice
{
    public double Line { get; set; }
    public double WinProbability { get; set; }
    public double PushProbability { get; set; }
    public double LossProbability { get; set; }
    public double FairOdds { get; set; }
    public double UnderWinProbability => LossProbability;
    public double UnderPushProbability => PushProbability;
    public double UnderLossProbability => WinProbability;
    public double FairUnderOdds { get; set; }
    public double? BookOverOdds { get; set; }
    public double? BookUnderOdds { get; set; }
    public double? OverEdge { get; set; }
    public double? OverExpectedValue { get; set; }
    public double? UnderEdge { get; set; }
    public double? UnderExpectedValue { get; set; }
    public string OverDecision { get; set; } = string.Empty;
    public string UnderDecision { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
}

public sealed class LiveTotalPricer
{
    private readonly LiveTotalPriceOptions _options;

    public LiveTotalPricer(LiveTotalPriceOptions options)
    {
        _options = options;
    }

    public async Task<LiveTotalPriceResult> PriceAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        await using FileStream stream = File.OpenRead(_options.ModelPath);
        WeibullModelFile model = await JsonSerializer.DeserializeAsync<WeibullModelFile>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, cancellationToken) ?? throw new InvalidOperationException("Could not read timing model JSON.");

        string scoreState = ScoreStateResolver.FromScore(_options.HomeGoals, _options.AwayGoals);
        TimingModelSource source = ResolveTimingModel(model, scoreState);

        double startingFairOverProbability = TotalGoalsPricingCalculator.RemoveTwoWayMargin(_options.StartingOverOdds, _options.StartingUnderOdds);
        double startingTotalXg = TotalGoalsPricingCalculator.SolveTotalXg(_options.StartingLine, startingFairOverProbability);

        double minute = Math.Clamp(_options.Minute, 0, model.MaxMinute > 0 ? model.MaxMinute : 90);
        TimingBlendResult timing = TimingShareCalculator.Calculate(new TimingBlendInput
        {
            Minute = minute,
            ShapeK = source.ShapeK,
            ScaleLambda = source.ScaleLambda,
            CdfAtMaxMinute = source.CdfAtMaxMinute,
            EmpiricalBuckets = MapBuckets(source.EmpiricalBuckets),
            EmpiricalWeight = _options.EmpiricalWeight
        });
        double empiricalWeight = timing.EmpiricalWeight;
        double remainingShare = timing.BlendedRemainingShare;
        double remainingXgBeforeStateCorrection = startingTotalXg * remainingShare;

        LiveTotalStateCorrectionResolution stateCorrection = await ResolveStateCorrectionAsync(cancellationToken);
        double remainingXgBeforeVolume = remainingXgBeforeStateCorrection * stateCorrection.Factor;
        double volumeFactor = Math.Clamp(_options.VolumeFactor, 0.20, 2.50);
        double remainingXg = remainingXgBeforeVolume * volumeFactor;

        var result = new LiveTotalPriceResult
        {
            ModelPath = _options.ModelPath,
            StateCorrectionPath = _options.StateCorrectionPath,
            StateTrigger = LiveTotalStateTrigger.Normalize(_options.StateTrigger),
            League = model.League,
            Minute = _options.Minute,
            HomeGoals = _options.HomeGoals,
            AwayGoals = _options.AwayGoals,
            ScoreState = scoreState,
            DetailedScoreState = stateCorrection.DetailedScoreState,
            SelectedTimingGroup = source.GroupName,
            TimingFallback = source.FallbackReason,
            StartingLine = _options.StartingLine,
            StartingOverOdds = _options.StartingOverOdds,
            StartingUnderOdds = _options.StartingUnderOdds,
            StartingFairOverProbability = startingFairOverProbability,
            StartingTotalXg = startingTotalXg,
            EmpiricalWeight = empiricalWeight,
            WeibullRemainingShare = timing.WeibullRemainingShare,
            EmpiricalRemainingShare = timing.EmpiricalRemainingShare,
            TimingRemainingShare = remainingShare,
            RemainingXgBeforeStateCorrection = remainingXgBeforeStateCorrection,
            StateCorrectionFactor = stateCorrection.Factor,
            StateCorrectionSupported = stateCorrection.IsSupported,
            StateCorrectionSource = stateCorrection.Source,
            RemainingXgBeforeVolume = remainingXgBeforeVolume,
            VolumeFactor = volumeFactor,
            VolumeFactorSource = _options.VolumeFactorSource,
            RemainingXg = remainingXg,
            HomeRedCards = _options.HomeRedCards,
            AwayRedCards = _options.AwayRedCards,
            LastGoalMinute = _options.LastGoalMinute
        };

        if (!string.IsNullOrWhiteSpace(source.FallbackReason))
            result.Warnings.Add(source.FallbackReason);

        if (_options.HomeRedCards + _options.AwayRedCards > 0)
        {
            result.RedCardWarning = "RED CARD WARNING - pricing not adjusted automatically; manual review recommended.";
            result.Warnings.Add(result.RedCardWarning);
        }

        if (_options.LastGoalMinute >= 0 && _options.Minute >= _options.LastGoalMinute && _options.Minute - _options.LastGoalMinute <= _options.RecentGoalMinutes)
        {
            result.HasRecentGoal = true;
            result.RecentGoalWarning = $"WAIT - goal occurred {_options.Minute - _options.LastGoalMinute} minute(s) ago.";
            result.Warnings.Add(result.RecentGoalWarning);
        }

        foreach (double line in _options.TargetLines.Distinct().OrderBy(x => x))
        {
            OverSettlementProbabilities probabilities = TotalGoalsPricingCalculator.CalculateOverSettlementProbabilities(line, result.CurrentGoals, remainingXg);
            double fairOverOdds = TotalGoalsPricingCalculator.CalculateFairOdds(probabilities);
            double fairUnderOdds = TotalGoalsPricingCalculator.CalculateFairOdds(new OverSettlementProbabilities(
                probabilities.LossProbability,
                probabilities.PushProbability,
                probabilities.WinProbability));

            double normalizedLine = NormalizeLineKey(line);
            _options.LiveOverOddsByLine.TryGetValue(normalizedLine, out double bookOverOdds);
            _options.LiveUnderOddsByLine.TryGetValue(normalizedLine, out double bookUnderOdds);
            bool hasBookOverOdds = bookOverOdds > 1.0;
            bool hasBookUnderOdds = bookUnderOdds > 1.0;

            double? overEdge = null;
            double? overEv = null;
            string overDecision;
            if (!hasBookOverOdds)
            {
                overDecision = "NO ODDS";
            }
            else
            {
                overEdge = fairOverOdds > 0 && !double.IsInfinity(fairOverOdds) ? bookOverOdds / fairOverOdds - 1.0 : null;
                overEv = probabilities.WinProbability * (bookOverOdds - 1.0) - probabilities.LossProbability;
                overDecision = BuildSideDecision(overEdge, result.StateCorrectionSupported, result.StateTrigger, result.HasRecentGoal, _options.HomeRedCards + _options.AwayRedCards > 0, "OVER");
            }

            double? underEdge = null;
            double? underEv = null;
            string underDecision;
            if (!hasBookUnderOdds)
            {
                underDecision = "NO ODDS";
            }
            else
            {
                underEdge = fairUnderOdds > 0 && !double.IsInfinity(fairUnderOdds) ? bookUnderOdds / fairUnderOdds - 1.0 : null;
                underEv = probabilities.LossProbability * (bookUnderOdds - 1.0) - probabilities.WinProbability;
                underDecision = BuildSideDecision(underEdge, result.StateCorrectionSupported, result.StateTrigger, result.HasRecentGoal, _options.HomeRedCards + _options.AwayRedCards > 0, "UNDER");
            }

            string decision = SelectBestDecision(overEdge, overDecision, underEdge, underDecision);

            result.Lines.Add(new LiveTotalLinePrice
            {
                Line = line,
                WinProbability = probabilities.WinProbability,
                PushProbability = probabilities.PushProbability,
                LossProbability = probabilities.LossProbability,
                FairOdds = fairOverOdds,
                FairUnderOdds = fairUnderOdds,
                BookOverOdds = hasBookOverOdds ? bookOverOdds : null,
                BookUnderOdds = hasBookUnderOdds ? bookUnderOdds : null,
                OverEdge = overEdge,
                OverExpectedValue = overEv,
                UnderEdge = underEdge,
                UnderExpectedValue = underEv,
                OverDecision = overDecision,
                UnderDecision = underDecision,
                Decision = decision
            });
        }

        return result;
    }

    private async Task<LiveTotalStateCorrectionResolution> ResolveStateCorrectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.StateCorrectionPath))
        {
            return new LiveTotalStateCorrectionResolution
            {
                StateTrigger = LiveTotalStateTrigger.Normalize(_options.StateTrigger),
                DetailedScoreState = LiveTotalStateCorrectionResolver.DetailedScoreState(_options.HomeGoals, _options.AwayGoals),
                MinuteBand = LiveTotalStateCorrectionResolver.MinuteBand(_options.StateTrigger, _options.Minute),
                Factor = 1.0,
                IsSupported = true,
                Source = "none/default 1.0"
            };
        }

        if (!File.Exists(_options.StateCorrectionPath))
            throw new FileNotFoundException("State correction JSON was not found.", _options.StateCorrectionPath);

        await using FileStream stream = File.OpenRead(_options.StateCorrectionPath);
        LiveTotalStateCorrectionFile correction = await JsonSerializer.DeserializeAsync<LiveTotalStateCorrectionFile>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, cancellationToken) ?? throw new InvalidOperationException("Could not read state correction JSON.");

        return LiveTotalStateCorrectionResolver.Resolve(correction, _options.StateTrigger, _options.Minute, _options.HomeGoals, _options.AwayGoals);
    }

    private string BuildSideDecision(double? edge, bool stateCorrectionSupported, string stateTrigger, bool hasRecentGoal, bool hasRedCard, string side)
    {
        if (!edge.HasValue)
            return "NO ODDS";
        if (!stateCorrectionSupported)
            return "NO BET - unsupported sparse state bucket";
        if (hasRecentGoal && !LiveTotalStateTrigger.Normalize(stateTrigger).Equals(LiveTotalStateTrigger.AfterGoal, StringComparison.OrdinalIgnoreCase))
            return "WAIT";
        if (hasRedCard)
            return edge >= _options.EdgeThreshold ? "MANUAL REVIEW" : "NO BET";
        if (edge >= _options.EdgeThreshold)
            return $"BET {side}";
        if (edge >= _options.EdgeThreshold / 2.0)
            return $"LEAN {side}";
        return "NO BET";
    }

    private static string SelectBestDecision(double? overEdge, string overDecision, double? underEdge, string underDecision)
    {
        bool overAction = overDecision.StartsWith("BET ", StringComparison.OrdinalIgnoreCase) || overDecision.StartsWith("LEAN ", StringComparison.OrdinalIgnoreCase) || overDecision.Equals("MANUAL REVIEW", StringComparison.OrdinalIgnoreCase);
        bool underAction = underDecision.StartsWith("BET ", StringComparison.OrdinalIgnoreCase) || underDecision.StartsWith("LEAN ", StringComparison.OrdinalIgnoreCase) || underDecision.Equals("MANUAL REVIEW", StringComparison.OrdinalIgnoreCase);

        if (overAction && underAction)
        {
            double o = overEdge ?? double.NegativeInfinity;
            double u = underEdge ?? double.NegativeInfinity;
            return o >= u ? overDecision : underDecision;
        }

        if (overAction) return overDecision;
        if (underAction) return underDecision;
        if (overDecision == "WAIT" || underDecision == "WAIT") return "WAIT";
        if (overDecision == "NO BET - unsupported sparse state bucket" || underDecision == "NO BET - unsupported sparse state bucket")
            return "NO BET - unsupported sparse state bucket";
        if (overDecision == "NO ODDS" && underDecision == "NO ODDS") return "NO ODDS";
        return "NO BET";
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ModelPath))
            throw new ArgumentException("Missing required argument --model.");
        if (!File.Exists(_options.ModelPath))
            throw new FileNotFoundException("Timing model JSON was not found.", _options.ModelPath);
        if (_options.StartingLine <= 0)
            throw new ArgumentException("--starting-line must be greater than 0.");
        if (_options.StartingOverOdds <= 1.0)
            throw new ArgumentException("--starting-over must be greater than 1.0.");
        if (_options.StartingUnderOdds <= 1.0)
            throw new ArgumentException("--starting-under must be greater than 1.0.");
        if (_options.Minute < 0)
            throw new ArgumentException("--minute must be >= 0.");
        if (_options.HomeGoals < 0 || _options.AwayGoals < 0)
            throw new ArgumentException("--home-goals and --away-goals must be >= 0.");
        if (_options.EmpiricalWeight < 0 || _options.EmpiricalWeight > 1)
            throw new ArgumentException("--empirical-weight must be between 0 and 1.");
        if (_options.EdgeThreshold < 0)
            throw new ArgumentException("--edge-threshold must be >= 0.");
        if (_options.VolumeFactor <= 0)
            throw new ArgumentException("--volume-factor must be greater than 0.");
    }

    private static double RemoveTwoWayMargin(double overOdds, double underOdds)
    {
        double overRaw = 1.0 / overOdds;
        double underRaw = 1.0 / underOdds;
        return overRaw / (overRaw + underRaw);
    }

    private static double SolveTotalXg(double line, double fairOverProbability)
    {
        double low = 0.01;
        double high = 8.0;

        while (OverNoPushProbability(high, line) < fairOverProbability && high < 20.0)
            high *= 1.5;

        for (int i = 0; i < 100; i++)
        {
            double mid = (low + high) / 2.0;
            double p = OverNoPushProbability(mid, line);
            if (p < fairOverProbability)
                low = mid;
            else
                high = mid;
        }

        return (low + high) / 2.0;
    }

    private static double OverNoPushProbability(double lambda, double line)
    {
        SettlementProbabilities p = CalculateOverSettlementProbabilities(line, 0, lambda);
        double decisive = p.WinProbability + p.LossProbability;
        if (decisive <= 0)
            return 0.5;
        return p.WinProbability / decisive;
    }

    private static SettlementProbabilities CalculateOverSettlementProbabilities(double line, int currentGoals, double remainingLambda)
    {
        double frac = Math.Round(line - Math.Floor(line), 6);
        int floor = (int)Math.Floor(line);

        if (Math.Abs(frac - 0.5) < 1e-6)
        {
            int winFromTotal = floor + 1;
            int needed = winFromTotal - currentGoals;
            double win = ProbabilityAtLeast(needed, remainingLambda);
            return new SettlementProbabilities(win, 0.0, 1.0 - win);
        }

        if (Math.Abs(frac) < 1e-6)
        {
            int neededWin = floor + 1 - currentGoals;
            int neededPush = floor - currentGoals;
            double win = ProbabilityAtLeast(neededWin, remainingLambda);
            double push = ProbabilityExactly(neededPush, remainingLambda);
            double loss = Math.Max(0.0, 1.0 - win - push);
            return new SettlementProbabilities(win, push, loss);
        }

        // Quarter lines are not primary targets, but this approximation returns full-win/push/loss style values
        // for display by averaging the adjacent Asian half-lines.
        if (Math.Abs(frac - 0.25) < 1e-6)
        {
            SettlementProbabilities lower = CalculateOverSettlementProbabilities(floor, currentGoals, remainingLambda);
            SettlementProbabilities upper = CalculateOverSettlementProbabilities(floor + 0.5, currentGoals, remainingLambda);
            return Average(lower, upper);
        }

        if (Math.Abs(frac - 0.75) < 1e-6)
        {
            SettlementProbabilities lower = CalculateOverSettlementProbabilities(floor + 0.5, currentGoals, remainingLambda);
            SettlementProbabilities upper = CalculateOverSettlementProbabilities(floor + 1.0, currentGoals, remainingLambda);
            return Average(lower, upper);
        }

        throw new ArgumentException($"Unsupported total line {line.ToString(CultureInfo.InvariantCulture)}. Supported: .0, .25, .5, .75 lines.");
    }

    private static SettlementProbabilities Average(SettlementProbabilities a, SettlementProbabilities b)
    {
        return new SettlementProbabilities(
            (a.WinProbability + b.WinProbability) / 2.0,
            (a.PushProbability + b.PushProbability) / 2.0,
            (a.LossProbability + b.LossProbability) / 2.0);
    }

    private static double CalculateFairOdds(SettlementProbabilities p)
    {
        if (p.WinProbability <= 0)
            return double.PositiveInfinity;
        return 1.0 + p.LossProbability / p.WinProbability;
    }

    private static double ProbabilityAtLeast(int needed, double lambda)
    {
        if (needed <= 0)
            return 1.0;
        return Math.Clamp(1.0 - PoissonCdf(needed - 1, lambda), 0.0, 1.0);
    }

    private static double ProbabilityExactly(int needed, double lambda)
    {
        if (needed < 0)
            return 0.0;
        return PoissonPmf(needed, lambda);
    }

    private static double PoissonPmf(int k, double lambda)
    {
        if (k < 0)
            return 0.0;
        if (lambda < 0)
            throw new ArgumentOutOfRangeException(nameof(lambda));

        double result = Math.Exp(-lambda);
        for (int i = 1; i <= k; i++)
            result *= lambda / i;
        return result;
    }

    private static double PoissonCdf(int k, double lambda)
    {
        if (k < 0)
            return 0.0;
        double sum = 0.0;
        for (int i = 0; i <= k; i++)
            sum += PoissonPmf(i, lambda);
        return Math.Clamp(sum, 0.0, 1.0);
    }

    private static string ResolveScoreState(int homeGoals, int awayGoals)
    {
        int margin = Math.Abs(homeGoals - awayGoals);
        return margin switch
        {
            0 => "Level",
            1 => "OneGoalMargin",
            2 => "TwoGoalMargin",
            _ => "ThreePlusGoalMargin"
        };
    }

    private static TimingModelSource ResolveTimingModel(WeibullModelFile model, string scoreState)
    {
        TimingModelGroupResult? group = model.Groups.FirstOrDefault(g => g.GroupName.Equals(scoreState, StringComparison.OrdinalIgnoreCase));
        if (group is not null)
        {
            return new TimingModelSource
            {
                GroupName = group.GroupName,
                ShapeK = group.ShapeK,
                ScaleLambda = group.ScaleLambda,
                CdfAtMaxMinute = group.CdfAtMaxMinute,
                EmpiricalBuckets = group.EmpiricalBuckets
            };
        }

        string fallback = model.Groups.Count > 0
            ? $"Timing group '{scoreState}' was not found; falling back to All/root model."
            : string.Empty;

        return new TimingModelSource
        {
            GroupName = "All",
            FallbackReason = fallback,
            ShapeK = model.Weibull.ShapeK,
            ScaleLambda = model.Weibull.ScaleLambda,
            CdfAtMaxMinute = model.Weibull.CdfAtMaxMinute,
            EmpiricalBuckets = model.Empirical.Buckets
        };
    }

    private static double NormalizedWeibullCdf(double minute, double shapeK, double scaleLambda, double cdfAtMaxMinute)
    {
        if (minute <= 0)
            return 0.0;
        if (shapeK <= 0 || scaleLambda <= 0 || cdfAtMaxMinute <= 0)
            return 0.0;
        double raw = 1.0 - Math.Exp(-Math.Pow(minute / scaleLambda, shapeK));
        return Math.Clamp(raw / cdfAtMaxMinute, 0.0, 1.0);
    }

    private static double EmpiricalCdf(double minute, IReadOnlyList<EmpiricalTimingBucket> buckets)
    {
        if (minute <= 0 || buckets.Count == 0)
            return 0.0;

        foreach (EmpiricalTimingBucket bucket in buckets)
        {
            if (minute <= bucket.FromMinuteExclusive)
                return bucket.CumulativeShareBefore;

            if (minute <= bucket.ToMinuteInclusive)
            {
                double width = bucket.ToMinuteInclusive - bucket.FromMinuteExclusive;
                if (width <= 0)
                    return bucket.CumulativeShareAfter;
                double progress = (minute - bucket.FromMinuteExclusive) / width;
                return Math.Clamp(bucket.CumulativeShareBefore + bucket.GoalShare * progress, 0.0, 1.0);
            }
        }

        return 1.0;
    }

    private static List<EmpiricalTimingBucketModel> MapBuckets(IEnumerable<EmpiricalTimingBucket> buckets)
    {
        return buckets.Select(x => new EmpiricalTimingBucketModel
        {
            FromMinuteExclusive = x.FromMinuteExclusive,
            ToMinuteInclusive = x.ToMinuteInclusive,
            Label = x.Label,
            GoalCount = x.GoalCount,
            GoalShare = x.GoalShare,
            CumulativeShareBefore = x.CumulativeShareBefore,
            CumulativeShareAfter = x.CumulativeShareAfter
        }).ToList();
    }

    public static double NormalizeLineKey(double line) => Math.Round(line, 2);
}

internal readonly record struct SettlementProbabilities(double WinProbability, double PushProbability, double LossProbability);

internal sealed class TimingModelSource
{
    public string GroupName { get; set; } = string.Empty;
    public string FallbackReason { get; set; } = string.Empty;
    public double ShapeK { get; set; }
    public double ScaleLambda { get; set; }
    public double CdfAtMaxMinute { get; set; }
    public List<EmpiricalTimingBucket> EmpiricalBuckets { get; set; } = [];
}
