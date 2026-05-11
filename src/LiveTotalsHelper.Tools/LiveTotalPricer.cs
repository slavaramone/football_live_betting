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
    public double TeamVolumeFactor { get; set; } = 1.0;
    public string TeamVolumeFactorSource { get; set; } = "none/default 1.0";
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
    public double RemainingXgBeforeTeamVolume { get; set; }
    public double TeamVolumeFactor { get; set; } = 1.0;
    public string TeamVolumeFactorSource { get; set; } = string.Empty;
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

        LiveTotalTimingEvaluation timing = LiveTotalTimingEvaluator.Evaluate(
            model,
            _options.Minute,
            _options.HomeGoals,
            _options.AwayGoals,
            _options.EmpiricalWeight);

        double startingFairOverProbability = TotalGoalsPricingCalculator.RemoveTwoWayMargin(_options.StartingOverOdds, _options.StartingUnderOdds);
        double startingTotalXg = TotalGoalsPricingCalculator.SolveTotalXg(_options.StartingLine, startingFairOverProbability);
        double remainingXgBeforeStateCorrection = startingTotalXg * timing.TimingRemainingShare;

        LiveTotalStateCorrectionResolution stateCorrection = await ResolveStateCorrectionAsync(cancellationToken);
        double remainingXgBeforeVolume = remainingXgBeforeStateCorrection * stateCorrection.Factor;
        double volumeFactor = Math.Clamp(_options.VolumeFactor, 0.20, 2.50);
        double remainingXgBeforeTeamVolume = remainingXgBeforeVolume * volumeFactor;
        double teamVolumeFactor = Math.Clamp(_options.TeamVolumeFactor, 0.50, 1.50);
        double remainingXg = remainingXgBeforeTeamVolume * teamVolumeFactor;

        var result = new LiveTotalPriceResult
        {
            ModelPath = _options.ModelPath,
            StateCorrectionPath = _options.StateCorrectionPath,
            StateTrigger = LiveTotalStateTrigger.Normalize(_options.StateTrigger),
            League = model.League,
            Minute = _options.Minute,
            HomeGoals = _options.HomeGoals,
            AwayGoals = _options.AwayGoals,
            ScoreState = timing.ScoreState,
            DetailedScoreState = stateCorrection.DetailedScoreState,
            SelectedTimingGroup = timing.SelectedTimingGroup,
            TimingFallback = timing.TimingFallback,
            StartingLine = _options.StartingLine,
            StartingOverOdds = _options.StartingOverOdds,
            StartingUnderOdds = _options.StartingUnderOdds,
            StartingFairOverProbability = startingFairOverProbability,
            StartingTotalXg = startingTotalXg,
            EmpiricalWeight = timing.EmpiricalWeight,
            WeibullRemainingShare = timing.WeibullRemainingShare,
            EmpiricalRemainingShare = timing.EmpiricalRemainingShare,
            TimingRemainingShare = timing.TimingRemainingShare,
            RemainingXgBeforeStateCorrection = remainingXgBeforeStateCorrection,
            StateCorrectionFactor = stateCorrection.Factor,
            StateCorrectionSupported = stateCorrection.IsSupported,
            StateCorrectionSource = stateCorrection.Source,
            RemainingXgBeforeVolume = remainingXgBeforeVolume,
            VolumeFactor = volumeFactor,
            VolumeFactorSource = _options.VolumeFactorSource,
            RemainingXgBeforeTeamVolume = remainingXgBeforeTeamVolume,
            TeamVolumeFactor = teamVolumeFactor,
            TeamVolumeFactorSource = _options.TeamVolumeFactorSource,
            RemainingXg = remainingXg,
            HomeRedCards = _options.HomeRedCards,
            AwayRedCards = _options.AwayRedCards,
            LastGoalMinute = _options.LastGoalMinute
        };

        if (!string.IsNullOrWhiteSpace(timing.TimingFallback))
            result.Warnings.Add(timing.TimingFallback);

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
        if (_options.TeamVolumeFactor <= 0)
            throw new ArgumentException("--team-volume-factor must be greater than 0.");
    }

    public static double NormalizeLineKey(double line) => Math.Round(line, 2);
}


