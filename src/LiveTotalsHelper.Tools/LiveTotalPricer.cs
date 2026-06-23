using System.Globalization;
using System.Text.Json;
using LiveTotalsHelper.Modeling;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalPriceOptions
{
    public string ModelPath { get; set; } = string.Empty;
    public string StateCorrectionPath { get; set; } = string.Empty;
    public string StateCorrectionScope { get; set; } = LiveTotalStateCorrectionScope.FixedMinute;
    public string EmpiricalSettlementPath { get; set; } = string.Empty;
    public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
    public double StartingLine { get; set; }
    public double StartingOverOdds { get; set; }
    public double StartingUnderOdds { get; set; }
    public int Minute { get; set; }
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    public double EmpiricalWeight { get; set; } = 0.80;
    public double EdgeThreshold { get; set; } = 0.10;
    public bool UseProbabilityMoveFilter { get; set; }
    public double MinOverProbabilityMove { get; set; } = 0.10;
    public double MinUnderProbabilityMove { get; set; } = -0.12;
    public bool UnderSignalsBettingAllowed { get; set; }
    public LiveTotalDecisionRuleOptions DecisionRules { get; } = new();
    public List<LiveTotalProfileBettingRule> LiveBettingRules { get; } = [];
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
    public string StateCorrectionScope { get; set; } = LiveTotalStateCorrectionScope.FixedMinute;
    public string EmpiricalSettlementPath { get; set; } = string.Empty;
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
    public string DecisionRulesSummary { get; set; } = string.Empty;
    public double RemainingXg { get; set; }
    public bool EmpiricalSettlementSupported { get; set; } = true;
    public string EmpiricalSettlementSource { get; set; } = "empirical settlement not configured";
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
    public double BaselineOverNoPushProbability { get; set; }
    public double CorrectedOverNoPushProbability { get; set; }
    public double OverProbabilityMove { get; set; }
    public double UnderProbabilityMove => -OverProbabilityMove;
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
    public string OverDecisionExplanation { get; set; } = string.Empty;
    public string UnderDecision { get; set; } = string.Empty;
    public string UnderDecisionExplanation { get; set; } = string.Empty;
    public string Decision { get; set; } = string.Empty;
    public string DecisionExplanation { get; set; } = string.Empty;
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
        double startingTotalXg = TotalGoalsPricingCalculator.EstimateTotalGoalsFromLine(_options.StartingLine, startingFairOverProbability);
        double remainingXgBeforeStateCorrection = startingTotalXg * timing.TimingRemainingShare;

        LiveTotalStateCorrectionResolution stateCorrection = await ResolveStateCorrectionAsync(cancellationToken);
        double remainingXgBeforeVolume = remainingXgBeforeStateCorrection * stateCorrection.Factor;
        double volumeFactor = Math.Clamp(_options.VolumeFactor, 0.20, 2.50);
        double remainingXg = remainingXgBeforeVolume * volumeFactor;
        LiveTotalEmpiricalSettlementResolution empiricalSettlement = await ResolveEmpiricalSettlementAsync(cancellationToken);

        var result = new LiveTotalPriceResult
        {
            ModelPath = _options.ModelPath,
            StateCorrectionPath = _options.StateCorrectionPath,
            StateCorrectionScope = LiveTotalStateCorrectionScope.Normalize(_options.StateCorrectionScope),
            EmpiricalSettlementPath = _options.EmpiricalSettlementPath,
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
            DecisionRulesSummary = _options.DecisionRules.Summary(),
            RemainingXg = remainingXg,
            EmpiricalSettlementSupported = empiricalSettlement.IsSupported,
            EmpiricalSettlementSource = empiricalSettlement.Source,
            HomeRedCards = _options.HomeRedCards,
            AwayRedCards = _options.AwayRedCards,
            LastGoalMinute = _options.LastGoalMinute
        };

        if (!string.IsNullOrWhiteSpace(timing.TimingFallback))
            result.Warnings.Add(timing.TimingFallback);

        if (!empiricalSettlement.IsSupported)
            throw new InvalidOperationException($"Empirical settlement is required; {empiricalSettlement.Source}.");

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

        double baselineRemainingXgForMove = remainingXgBeforeStateCorrection * volumeFactor;

        foreach (double line in _options.TargetLines.Distinct().OrderBy(x => x))
        {
            OverSettlementProbabilities baselineProbabilities = CalculateSettlementProbabilities(line, result.CurrentGoals, baselineRemainingXgForMove, empiricalSettlement);
            OverSettlementProbabilities probabilities = CalculateSettlementProbabilities(line, result.CurrentGoals, remainingXg, empiricalSettlement);
            double baselineOverNoPushProbability = NoPushOverProbability(baselineProbabilities);
            double correctedOverNoPushProbability = NoPushOverProbability(probabilities);
            double overProbabilityMove = correctedOverNoPushProbability - baselineOverNoPushProbability;

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
            LiveTotalSideDecision overSideDecision;
            if (!hasBookOverOdds)
            {
                overSideDecision = new LiveTotalSideDecision { Decision = "NO ODDS", Explanation = "No over odds were entered for this line." };
            }
            else
            {
                overEdge = fairOverOdds > 0 && !double.IsInfinity(fairOverOdds) ? bookOverOdds / fairOverOdds - 1.0 : null;
                overEv = probabilities.WinProbability * (bookOverOdds - 1.0) - probabilities.LossProbability;
                overSideDecision = BuildSideDecision(line, overEdge, overProbabilityMove, result.StateCorrectionSupported, result.StateTrigger, result.HasRecentGoal, _options.HomeRedCards + _options.AwayRedCards > 0, "OVER");
            }

            double? underEdge = null;
            double? underEv = null;
            LiveTotalSideDecision underSideDecision;
            if (!hasBookUnderOdds)
            {
                underSideDecision = new LiveTotalSideDecision { Decision = "NO ODDS", Explanation = "No under odds were entered for this line." };
            }
            else
            {
                underEdge = fairUnderOdds > 0 && !double.IsInfinity(fairUnderOdds) ? bookUnderOdds / fairUnderOdds - 1.0 : null;
                underEv = probabilities.LossProbability * (bookUnderOdds - 1.0) - probabilities.WinProbability;
                underSideDecision = BuildSideDecision(line, underEdge, overProbabilityMove, result.StateCorrectionSupported, result.StateTrigger, result.HasRecentGoal, _options.HomeRedCards + _options.AwayRedCards > 0, "UNDER");
            }

            LiveTotalSideDecision selectedDecision = SelectBestDecision(overEdge, overSideDecision, underEdge, underSideDecision);

            result.Lines.Add(new LiveTotalLinePrice
            {
                Line = line,
                WinProbability = probabilities.WinProbability,
                PushProbability = probabilities.PushProbability,
                LossProbability = probabilities.LossProbability,
                BaselineOverNoPushProbability = baselineOverNoPushProbability,
                CorrectedOverNoPushProbability = correctedOverNoPushProbability,
                OverProbabilityMove = overProbabilityMove,
                FairOdds = fairOverOdds,
                FairUnderOdds = fairUnderOdds,
                BookOverOdds = hasBookOverOdds ? bookOverOdds : null,
                BookUnderOdds = hasBookUnderOdds ? bookUnderOdds : null,
                OverEdge = overEdge,
                OverExpectedValue = overEv,
                UnderEdge = underEdge,
                UnderExpectedValue = underEv,
                OverDecision = overSideDecision.Decision,
                OverDecisionExplanation = overSideDecision.Explanation,
                UnderDecision = underSideDecision.Decision,
                UnderDecisionExplanation = underSideDecision.Explanation,
                Decision = selectedDecision.Decision,
                DecisionExplanation = selectedDecision.Explanation
            });
        }

        return result;
    }

    private async Task<LiveTotalEmpiricalSettlementResolution> ResolveEmpiricalSettlementAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.EmpiricalSettlementPath))
            return new LiveTotalEmpiricalSettlementResolution { IsSupported = false, Source = "no empirical settlement table configured" };

        if (!File.Exists(_options.EmpiricalSettlementPath))
            return new LiveTotalEmpiricalSettlementResolution { IsSupported = false, Source = $"empirical settlement table not found: {_options.EmpiricalSettlementPath}" };

        await using FileStream stream = File.OpenRead(_options.EmpiricalSettlementPath);
        LiveTotalEmpiricalSettlementFile settlement = await JsonSerializer.DeserializeAsync<LiveTotalEmpiricalSettlementFile>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, cancellationToken) ?? throw new InvalidOperationException("Could not read empirical settlement JSON.");

        return LiveTotalEmpiricalSettlementResolver.Resolve(settlement, _options.StateTrigger, _options.Minute, _options.HomeGoals, _options.AwayGoals);
    }

    private static OverSettlementProbabilities CalculateSettlementProbabilities(
        double line,
        int currentGoals,
        double remainingGoalsMean,
        LiveTotalEmpiricalSettlementResolution empiricalSettlement)
    {
        if (!empiricalSettlement.IsSupported)
            throw new InvalidOperationException($"Empirical settlement is required; {empiricalSettlement.Source}.");

        return TotalGoalsPricingCalculator.CalculateOverSettlementProbabilities(
            line,
            currentGoals,
            empiricalSettlement.Probabilities,
            remainingGoalsMean);
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

        return LiveTotalStateCorrectionGate.Resolve(correction, _options.StateCorrectionScope, _options.StateTrigger, _options.Minute, _options.HomeGoals, _options.AwayGoals);
    }

    private LiveTotalSideDecision BuildSideDecision(double line, double? edge, double probabilityMove, bool stateCorrectionSupported, string stateTrigger, bool hasRecentGoal, bool hasRedCard, string side)
    {
        return LiveTotalDecisionRulesHandler.BuildSideDecision(
            _options.DecisionRules,
            _options.LiveBettingRules,
            FindBettingRule,
            _options.UseProbabilityMoveFilter,
            _options.UnderSignalsBettingAllowed,
            _options.EdgeThreshold,
            _options.MinOverProbabilityMove,
            _options.MinUnderProbabilityMove,
            line,
            edge,
            probabilityMove,
            stateCorrectionSupported,
            stateTrigger,
            _options.Minute,
            hasRecentGoal,
            hasRedCard,
            side);
    }

    private LiveTotalProfileBettingRule? FindBettingRule(double line, string stateTrigger, string side)
    {
        double normalizedLine = NormalizeLineKey(line);
        string normalizedTrigger = LiveTotalStateTrigger.Normalize(stateTrigger);

        return _options.LiveBettingRules.FirstOrDefault(rule =>
            NormalizeLineKey(rule.Line).Equals(normalizedLine) &&
            SideMatches(rule.Side, side) &&
            TriggerMatches(rule.StateTrigger, normalizedTrigger));
    }

    private static bool SideMatches(string ruleSide, string side)
    {
        return ruleSide.Equals(side, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TriggerMatches(string ruleTrigger, string stateTrigger)
    {
        if (string.IsNullOrWhiteSpace(ruleTrigger) ||
            ruleTrigger.Equals("All", StringComparison.OrdinalIgnoreCase) ||
            ruleTrigger.Equals("Any", StringComparison.OrdinalIgnoreCase))
            return true;

        return LiveTotalStateTrigger.Normalize(ruleTrigger).Equals(stateTrigger, StringComparison.OrdinalIgnoreCase);
    }

    private static double NoPushOverProbability(OverSettlementProbabilities probabilities)
    {
        double decisive = probabilities.WinProbability + probabilities.LossProbability;
        if (decisive <= 1e-12)
            return 0.5;

        return Math.Clamp(probabilities.WinProbability / decisive, 0.0, 1.0);
    }

    private static LiveTotalSideDecision SelectBestDecision(double? overEdge, LiveTotalSideDecision overDecision, double? underEdge, LiveTotalSideDecision underDecision)
    {
        bool overAction = overDecision.IsAction;
        bool underAction = underDecision.IsAction;

        if (overAction && underAction)
        {
            double o = overEdge ?? double.NegativeInfinity;
            double u = underEdge ?? double.NegativeInfinity;
            return o >= u ? overDecision : underDecision;
        }

        if (overAction) return overDecision;
        if (underAction) return underDecision;
        if (overDecision.Decision == "WAIT" || underDecision.Decision == "WAIT") return overDecision.Decision == "WAIT" ? overDecision : underDecision;
        if (overDecision.Decision == "NO BET - unsupported sparse state bucket" || underDecision.Decision == "NO BET - unsupported sparse state bucket")
            return overDecision.Decision == "NO BET - unsupported sparse state bucket" ? overDecision : underDecision;
        if (overDecision.Decision == "NO ODDS" && underDecision.Decision == "NO ODDS") return overDecision;
        if (overDecision.Decision.StartsWith("NO BET - rules", StringComparison.OrdinalIgnoreCase)) return overDecision;
        if (underDecision.Decision.StartsWith("NO BET - rules", StringComparison.OrdinalIgnoreCase)) return underDecision;
        return new LiveTotalSideDecision { Decision = "NO BET", Explanation = "Neither side passed edge/rule thresholds." };
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ModelPath))
            throw new ArgumentException("Missing required argument --model.");
        if (!File.Exists(_options.ModelPath))
            throw new FileNotFoundException("Timing model JSON was not found.", _options.ModelPath);
        _ = LiveTotalStateCorrectionScope.Normalize(_options.StateCorrectionScope);

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
        if (_options.MinOverProbabilityMove < -1 || _options.MinOverProbabilityMove > 1)
            throw new ArgumentException("--min-over-probability-move must be between -1 and 1.");
        if (_options.MinUnderProbabilityMove < -1 || _options.MinUnderProbabilityMove > 1)
            throw new ArgumentException("--min-under-probability-move must be between -1 and 1.");
        if (_options.VolumeFactor <= 0)
            throw new ArgumentException("--volume-factor must be greater than 0.");
    }

    public static double NormalizeLineKey(double line) => Math.Round(line, 2);
}


