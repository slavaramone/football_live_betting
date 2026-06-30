using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LiveTotalsHelper.Tools;

public sealed class AfterGoalEntryEvaluationOptions
{
    public string EntryRulesPath { get; set; } = string.Empty;
    public string ContextGatesPath { get; set; } = string.Empty;
    public string SummaryPath { get; set; } = string.Empty;
    public string LeagueKey { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string ScoringTeam { get; set; } = string.Empty;
    public string ConcedingTeam { get; set; } = string.Empty;
    public string Minute { get; set; } = string.Empty;
    public int ScoreAfterHome { get; set; }
    public int ScoreAfterAway { get; set; }
    public string ConflictPolicy { get; set; } = string.Empty;
}

public sealed class AfterGoalEntryEvaluationResult
{
    public string LeagueKey { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string ScoringTeam { get; set; } = string.Empty;
    public string ConcedingTeam { get; set; } = string.Empty;
    public string MinuteDisplay { get; set; } = string.Empty;
    public int ScoreAfterHome { get; set; }
    public int ScoreAfterAway { get; set; }
    public AfterGoalLiveState State { get; set; } = new();
    public AfterGoalTriggerEvaluation? ScoringTrigger { get; set; }
    public AfterGoalTriggerEvaluation? ConcedingTrigger { get; set; }
    public string FinalDecision { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public bool MarketGateRequired { get; set; } = true;
    public string ConflictPolicy { get; set; } = "NoBet";
    public string Reason { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
}

public sealed class AfterGoalLiveState
{
    public string MinuteBand { get; set; } = string.Empty;
    public string ScoreGapAfterBand { get; set; } = string.Empty;
    public string TotalGoalsAfterBand { get; set; } = string.Empty;
    public string GameStateAfter { get; set; } = string.Empty;
}

public sealed class AfterGoalTriggerEvaluation
{
    public string Team { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public string SignalClass { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string EntryRuleStatus { get; set; } = string.Empty;
    public string TriggerDecision { get; set; } = string.Empty;
    public string Strength { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool MarketGateRequired { get; set; } = true;
    public List<AfterGoalGateCheck> GateChecks { get; set; } = [];
}

public sealed class AfterGoalGateCheck
{
    public string StateDimension { get; set; } = string.Empty;
    public string StateBucket { get; set; } = string.Empty;
    public string GateStatus { get; set; } = "NoData";
    public string GateStrength { get; set; } = "None";
    public int TrainSampleSize { get; set; }
    public int TestSampleSize { get; set; }
    public double? TrainResidualVsBaseline { get; set; }
    public double? TestResidualVsBaseline { get; set; }
    public string Reason { get; set; } = string.Empty;
}

internal sealed class EntryRuleRecord
{
    public string LeagueKey { get; init; } = string.Empty;
    public string LeagueName { get; init; } = string.Empty;
    public string Team { get; init; } = string.Empty;
    public string TriggerType { get; init; } = string.Empty;
    public string SignalClass { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string EntryRuleStatus { get; init; } = string.Empty;
    public bool MarketGateRequired { get; init; } = true;
    public string ConflictPolicy { get; init; } = "NoBet";
    public string Reason { get; init; } = string.Empty;
}

internal sealed class ContextGateRecord
{
    public string LeagueKey { get; init; } = string.Empty;
    public string Team { get; init; } = string.Empty;
    public string TriggerType { get; init; } = string.Empty;
    public string SignalClass { get; init; } = string.Empty;
    public string SignalDirection { get; init; } = string.Empty;
    public string StateDimension { get; init; } = string.Empty;
    public string StateBucket { get; init; } = string.Empty;
    public string GateStatus { get; init; } = string.Empty;
    public string GateStrength { get; init; } = string.Empty;
    public int TrainSampleSize { get; init; }
    public int TestSampleSize { get; init; }
    public double? TrainResidualVsBaseline { get; init; }
    public double? TestResidualVsBaseline { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class AfterGoalEntryEvaluator
{
    private static readonly string[] CriticalDimensions =
    [
        "MinuteBand",
        "ScoreGapAfterBand",
        "TotalGoalsAfterBand",
        "GameStateAfter"
    ];

    public async Task<AfterGoalEntryEvaluationResult> EvaluateAsync(AfterGoalEntryEvaluationOptions options, CancellationToken cancellationToken)
    {
        ValidateInput(options);

        List<EntryRuleRecord> rules = await ReadEntryRulesAsync(options.EntryRulesPath, cancellationToken);
        List<ContextGateRecord> gates = await ReadContextGatesAsync(options.ContextGatesPath, cancellationToken);
        (string summaryLeagueKey, string summaryConflictPolicy) = ReadSummaryMetadata(options.SummaryPath);
        options.ConflictPolicy = FirstNonEmpty(options.ConflictPolicy, summaryConflictPolicy, "NoBet");

        string leagueKey = FirstNonEmpty(options.LeagueKey, summaryLeagueKey, rules.Select(x => x.LeagueKey).FirstOrDefault() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(leagueKey))
            throw new ArgumentException("League key could not be resolved. Provide --league-key, --profile, or --summary.");

        ValidateLeagueKey("entry rules", leagueKey, rules.Select(x => x.LeagueKey));
        ValidateLeagueKey("context gates", leagueKey, gates.Select(x => x.LeagueKey));
        if (!string.IsNullOrWhiteSpace(summaryLeagueKey) && !summaryLeagueKey.Equals(leagueKey, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Summary LeagueKey {summaryLeagueKey} does not match requested LeagueKey {leagueKey}.");

        AfterGoalLiveState state = BuildState(options);
        EntryRuleRecord? scoringRule = FindRule(rules, leagueKey, options.ScoringTeam, "AfterScoring");
        EntryRuleRecord? concedingRule = FindRule(rules, leagueKey, options.ConcedingTeam, "AfterConceding");

        var warnings = new List<string>();
        if (scoringRule is null && concedingRule is null)
            warnings.Add("No matching scoring or conceding trigger signal exists.");
        else if (scoringRule is null)
            warnings.Add($"Only conceding trigger found; no signal for {options.ScoringTeam} AfterScoring.");
        else if (concedingRule is null)
            warnings.Add($"Only scoring trigger found; no signal for {options.ConcedingTeam} AfterConceding.");

        AfterGoalTriggerEvaluation? scoring = scoringRule is null ? null : EvaluateTrigger(scoringRule, gates, state, warnings);
        AfterGoalTriggerEvaluation? conceding = concedingRule is null ? null : EvaluateTrigger(concedingRule, gates, state, warnings);

        AfterGoalEntryEvaluationResult result = BuildFinalResult(options, leagueKey, state, scoring, conceding, warnings);
        return result;
    }

    public static string ToText(AfterGoalEntryEvaluationResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("After-goal entry evaluation");
        builder.AppendLine($"League: {result.LeagueKey}");
        builder.AppendLine($"Match: {result.HomeTeam} vs {result.AwayTeam}");
        builder.AppendLine($"Goal: {result.ScoringTeam} scored at {result.MinuteDisplay}'");
        builder.AppendLine($"Score after goal: {result.ScoreAfterHome}-{result.ScoreAfterAway}");
        builder.AppendLine($"State: MinuteBand={result.State.MinuteBand}, ScoreGap={result.State.ScoreGapAfterBand}, TotalGoalsAfter={result.State.TotalGoalsAfterBand}, GameState={result.State.GameStateAfter}");
        builder.AppendLine();
        AppendTrigger(builder, "Scoring trigger", result.ScoringTrigger);
        builder.AppendLine();
        AppendTrigger(builder, "Conceding trigger", result.ConcedingTrigger);
        builder.AppendLine();
        builder.AppendLine($"Final decision: {result.FinalDecision}");
        builder.AppendLine($"Direction: {Empty(result.Direction)}");
        builder.AppendLine($"Confidence: {Empty(result.Confidence)}");
        builder.AppendLine($"Reason: {result.Reason}");
        builder.AppendLine($"Market gate required: {result.MarketGateRequired.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}");
        if (result.MarketGateRequired && result.FinalDecision is "Candidate" or "WeakCandidate" or "Watchlist")
            builder.AppendLine("Important: this is not a bet recommendation. Check live total/odds before entry.");
        if (result.Warnings.Count > 0)
        {
            builder.AppendLine("Warnings:");
            foreach (string warning in result.Warnings)
                builder.AppendLine($"- {warning}");
        }

        return builder.ToString();
    }

    private static void AppendTrigger(StringBuilder builder, string title, AfterGoalTriggerEvaluation? trigger)
    {
        builder.AppendLine($"{title}:");
        if (trigger is null)
        {
            builder.AppendLine("- No signal");
            return;
        }

        builder.AppendLine($"- Team: {trigger.Team}");
        builder.AppendLine($"- Trigger: {trigger.TriggerType}");
        builder.AppendLine($"- Signal: {trigger.Direction} / {trigger.SignalClass}");
        builder.AppendLine($"- Rule status: {trigger.EntryRuleStatus}");
        builder.AppendLine($"- Trigger decision: {trigger.TriggerDecision}");
        builder.AppendLine($"- Gate checks:");
        foreach (AfterGoalGateCheck gate in trigger.GateChecks)
            builder.AppendLine($"  - {gate.StateDimension} {gate.StateBucket}: {gate.GateStatus} — {gate.Reason}");
    }

    private static AfterGoalTriggerEvaluation EvaluateTrigger(EntryRuleRecord rule, IReadOnlyList<ContextGateRecord> gates, AfterGoalLiveState state, List<string> warnings)
    {
        List<AfterGoalGateCheck> checks = [];
        foreach ((string dimension, string bucket) in CurrentBuckets(state))
        {
            ContextGateRecord? gate = gates.FirstOrDefault(x =>
                x.LeagueKey.Equals(rule.LeagueKey, StringComparison.OrdinalIgnoreCase) &&
                TeamEquals(x.Team, rule.Team) &&
                x.TriggerType.Equals(rule.TriggerType, StringComparison.OrdinalIgnoreCase) &&
                x.SignalClass.Equals(rule.SignalClass, StringComparison.OrdinalIgnoreCase) &&
                x.SignalDirection.Equals(rule.Direction, StringComparison.OrdinalIgnoreCase) &&
                x.StateDimension.Equals(dimension, StringComparison.OrdinalIgnoreCase) &&
                x.StateBucket.Equals(bucket, StringComparison.OrdinalIgnoreCase));

            if (gate is null)
            {
                warnings.Add($"Context gate row missing for {rule.Team} {rule.TriggerType} {dimension}={bucket}.");
                checks.Add(new AfterGoalGateCheck
                {
                    StateDimension = dimension,
                    StateBucket = bucket,
                    GateStatus = "NoData",
                    GateStrength = "None",
                    Reason = "Context gate row missing for this current bucket."
                });
            }
            else
            {
                checks.Add(new AfterGoalGateCheck
                {
                    StateDimension = dimension,
                    StateBucket = bucket,
                    GateStatus = gate.GateStatus,
                    GateStrength = gate.GateStrength,
                    TrainSampleSize = gate.TrainSampleSize,
                    TestSampleSize = gate.TestSampleSize,
                    TrainResidualVsBaseline = gate.TrainResidualVsBaseline,
                    TestResidualVsBaseline = gate.TestResidualVsBaseline,
                    Reason = gate.Reason
                });
            }
        }

        string decision = TriggerDecision(rule, checks);
        string strength = TriggerStrength(rule, checks, decision);
        if (rule.EntryRuleStatus is "NoUsableGates" or "TooThin" or "Disabled")
            warnings.Add($"{rule.Team} {rule.TriggerType} signal exists but entry status is {rule.EntryRuleStatus}.");

        return new AfterGoalTriggerEvaluation
        {
            Team = rule.Team,
            TriggerType = rule.TriggerType,
            SignalClass = rule.SignalClass,
            Direction = rule.Direction,
            EntryRuleStatus = rule.EntryRuleStatus,
            TriggerDecision = decision,
            Strength = strength,
            Reason = rule.Reason,
            MarketGateRequired = rule.MarketGateRequired,
            GateChecks = checks
        };
    }

    private static string TriggerDecision(EntryRuleRecord rule, IReadOnlyList<AfterGoalGateCheck> checks)
    {
        if (rule.EntryRuleStatus is "NoUsableGates" or "TooThin" or "Disabled")
            return "Avoid";
        if (checks.Any(x => x.GateStatus == "Avoid"))
            return "Avoid";

        bool allUsable = checks.All(x => x.GateStatus is "Allowed" or "WeakAllowed");
        if (allUsable)
            return checks.Any(x => x.GateStatus == "WeakAllowed") ? "WeakAllowed" : "Allowed";

        return "Inconclusive";
    }

    private static string TriggerStrength(EntryRuleRecord rule, IReadOnlyList<AfterGoalGateCheck> checks, string decision)
    {
        if (decision is "Avoid" or "Inconclusive")
            return "None";
        if (checks.All(x => x.GateStatus == "Allowed") && rule.EntryRuleStatus == "Active")
            return "Strong";
        if (rule.SignalClass == "Strict" && rule.EntryRuleStatus != "ConditionalWeak")
            return "Medium";
        return "Low";
    }

    private static AfterGoalEntryEvaluationResult BuildFinalResult(
        AfterGoalEntryEvaluationOptions options,
        string leagueKey,
        AfterGoalLiveState state,
        AfterGoalTriggerEvaluation? scoring,
        AfterGoalTriggerEvaluation? conceding,
        List<string> warnings)
    {
        string policy = AfterGoalEntryGateBuilder.NormalizeConflictPolicy(options.ConflictPolicy);
        AfterGoalTriggerEvaluation? chosen = null;
        string final;
        string reason;

        if (scoring is null && conceding is null)
        {
            final = "NoSignal";
            reason = "No scoring or conceding team trigger signal exists.";
        }
        else if (scoring is not null && conceding is not null)
        {
            if (!scoring.Direction.Equals(conceding.Direction, StringComparison.OrdinalIgnoreCase))
                return BuildConflictResult(options, leagueKey, state, scoring, conceding, warnings, policy);

            if (scoring.TriggerDecision == "Avoid" || conceding.TriggerDecision == "Avoid")
            {
                final = "Avoid";
                chosen = scoring.TriggerDecision == "Avoid" ? scoring : conceding;
                reason = "At least one same-direction trigger has an avoid gate; conservative avoid.";
            }
            else if (scoring.TriggerDecision == "Allowed" && conceding.TriggerDecision == "Allowed")
            {
                chosen = scoring.SignalClass == "Strict" ? scoring : conceding;
                final = FinalFromTrigger(chosen);
                reason = "Scoring and conceding triggers agree and pass state gates.";
            }
            else if (scoring.TriggerDecision is "Allowed" or "WeakAllowed" || conceding.TriggerDecision is "Allowed" or "WeakAllowed")
            {
                chosen = PickBest(scoring, conceding);
                final = "WeakCandidate";
                reason = "Same-direction triggers partially support this state.";
            }
            else
            {
                final = "Avoid";
                reason = "Signals exist but do not pass current state gates.";
            }
        }
        else
        {
            chosen = scoring ?? conceding;
            final = FinalFromTrigger(chosen!);
            reason = ReasonFromSingle(chosen!);
        }

        return BaseResult(options, leagueKey, state, scoring, conceding, chosen, final, reason, warnings, policy);
    }

    private static AfterGoalEntryEvaluationResult BuildConflictResult(
        AfterGoalEntryEvaluationOptions options,
        string leagueKey,
        AfterGoalLiveState state,
        AfterGoalTriggerEvaluation scoring,
        AfterGoalTriggerEvaluation conceding,
        List<string> warnings,
        string policy)
    {
        AfterGoalTriggerEvaluation? chosen = policy switch
        {
            "PreferStrict" when scoring.SignalClass == "Strict" && conceding.SignalClass == "Watchlist" => scoring,
            "PreferStrict" when conceding.SignalClass == "Strict" && scoring.SignalClass == "Watchlist" => conceding,
            "PreferScoring" when scoring.TriggerDecision != "Avoid" => scoring,
            "PreferConceding" when conceding.TriggerDecision != "Avoid" => conceding,
            _ => null
        };

        if (chosen is null)
        {
            return BaseResult(options, leagueKey, state, scoring, conceding, null, "ConflictNoBet",
                $"Scoring trigger is {scoring.Direction}, conceding trigger is {conceding.Direction}; conflict policy {policy} blocks entry.",
                warnings, policy);
        }

        return BaseResult(options, leagueKey, state, scoring, conceding, chosen, FinalFromTrigger(chosen),
            $"Opposite-direction triggers resolved by conflict policy {policy}; selected {chosen.Team} {chosen.TriggerType}.",
            warnings, policy);
    }

    private static AfterGoalEntryEvaluationResult BaseResult(
        AfterGoalEntryEvaluationOptions options,
        string leagueKey,
        AfterGoalLiveState state,
        AfterGoalTriggerEvaluation? scoring,
        AfterGoalTriggerEvaluation? conceding,
        AfterGoalTriggerEvaluation? chosen,
        string final,
        string reason,
        List<string> warnings,
        string policy)
    {
        bool marketGateRequired = final is "Candidate" or "WeakCandidate" or "Watchlist" || chosen?.MarketGateRequired == true;
        if (marketGateRequired)
            warnings.Add("Market gate required: check live total/odds before any entry.");

        return new AfterGoalEntryEvaluationResult
        {
            LeagueKey = leagueKey,
            HomeTeam = options.HomeTeam,
            AwayTeam = options.AwayTeam,
            ScoringTeam = options.ScoringTeam,
            ConcedingTeam = options.ConcedingTeam,
            MinuteDisplay = options.Minute,
            ScoreAfterHome = options.ScoreAfterHome,
            ScoreAfterAway = options.ScoreAfterAway,
            State = state,
            ScoringTrigger = scoring,
            ConcedingTrigger = conceding,
            FinalDecision = final,
            Direction = chosen?.Direction ?? string.Empty,
            Confidence = chosen?.Strength ?? "None",
            MarketGateRequired = marketGateRequired,
            ConflictPolicy = policy,
            Reason = reason,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static string FinalFromTrigger(AfterGoalTriggerEvaluation trigger)
    {
        if (trigger.TriggerDecision == "Avoid")
            return "Avoid";
        if (trigger.TriggerDecision == "Inconclusive")
            return "Avoid";
        if (trigger.SignalClass == "Watchlist")
            return "Watchlist";
        return trigger.TriggerDecision == "Allowed" && trigger.EntryRuleStatus == "Active" ? "Candidate" : "WeakCandidate";
    }

    private static string ReasonFromSingle(AfterGoalTriggerEvaluation trigger)
        => trigger.TriggerDecision switch
        {
            "Allowed" => "Trigger exists and all critical state gates allow this state.",
            "WeakAllowed" => "Trigger exists and critical state gates are usable, with at least one weak gate.",
            "Avoid" => "Trigger exists but current state gate or entry status says avoid.",
            _ => "Trigger exists but current state gates are inconclusive."
        };

    private static AfterGoalTriggerEvaluation PickBest(AfterGoalTriggerEvaluation left, AfterGoalTriggerEvaluation right)
    {
        int Score(AfterGoalTriggerEvaluation trigger)
        {
            int value = trigger.SignalClass == "Strict" ? 10 : 0;
            value += trigger.TriggerDecision == "Allowed" ? 5 : trigger.TriggerDecision == "WeakAllowed" ? 2 : 0;
            return value;
        }

        return Score(left) >= Score(right) ? left : right;
    }

    private static AfterGoalLiveState BuildState(AfterGoalEntryEvaluationOptions options)
    {
        int baseMinute = ParseBaseMinute(options.Minute);
        int total = options.ScoreAfterHome + options.ScoreAfterAway;
        int gap = Math.Abs(options.ScoreAfterHome - options.ScoreAfterAway);
        return new AfterGoalLiveState
        {
            MinuteBand = MinuteBand(baseMinute),
            TotalGoalsAfterBand = total >= 5 ? "5+" : Math.Max(1, total).ToString(CultureInfo.InvariantCulture),
            ScoreGapAfterBand = gap == 0 ? "Draw" : gap == 1 ? "Lead1" : gap == 2 ? "Lead2" : "Lead3Plus",
            GameStateAfter = options.ScoreAfterHome == options.ScoreAfterAway ? "EqualAfter" : options.ScoreAfterHome > options.ScoreAfterAway ? "HomeLeadAfter" : "AwayLeadAfter"
        };
    }

    private static int ParseBaseMinute(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Provide --minute.");

        string basePart = value.Split('+', 2, StringSplitOptions.TrimEntries)[0];
        if (!int.TryParse(basePart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minute) || minute < 0)
            throw new ArgumentException($"Could not parse minute '{value}'. Use 38, 45+2, or 90+4.");

        return minute;
    }

    private static string MinuteBand(int baseMinute)
    {
        if (baseMinute <= 15) return "00-15";
        if (baseMinute <= 30) return "16-30";
        if (baseMinute <= 45) return "31-45+";
        if (baseMinute <= 60) return "46-60";
        if (baseMinute <= 75) return "61-75";
        return "76-90+";
    }

    private static void ValidateInput(AfterGoalEntryEvaluationOptions options)
    {
        RequireFile(options.EntryRulesPath, "entry rules");
        RequireFile(options.ContextGatesPath, "context gates");
        if (!string.IsNullOrWhiteSpace(options.SummaryPath))
            RequireFile(options.SummaryPath, "summary");
        if (string.IsNullOrWhiteSpace(options.HomeTeam) || string.IsNullOrWhiteSpace(options.AwayTeam))
            throw new ArgumentException("Provide --home-team and --away-team.");
        if (string.IsNullOrWhiteSpace(options.ScoringTeam) || string.IsNullOrWhiteSpace(options.ConcedingTeam))
            throw new ArgumentException("Provide --scoring-team and --conceding-team.");
        if (!TeamEquals(options.ScoringTeam, options.HomeTeam) && !TeamEquals(options.ScoringTeam, options.AwayTeam))
            throw new ArgumentException("--scoring-team must be either home team or away team.");
        if (!TeamEquals(options.ConcedingTeam, options.HomeTeam) && !TeamEquals(options.ConcedingTeam, options.AwayTeam))
            throw new ArgumentException("--conceding-team must be either home team or away team.");
        if (TeamEquals(options.ScoringTeam, options.ConcedingTeam))
            throw new ArgumentException("--scoring-team and --conceding-team must differ.");
        if (options.ScoreAfterHome + options.ScoreAfterAway < 1)
            throw new ArgumentException("Score after goal must contain at least one goal.");
        if (TeamEquals(options.ScoringTeam, options.HomeTeam) && options.ScoreAfterHome <= 0)
            throw new ArgumentException("Home scoring team is impossible because score-after-home is zero.");
        if (TeamEquals(options.ScoringTeam, options.AwayTeam) && options.ScoreAfterAway <= 0)
            throw new ArgumentException("Away scoring team is impossible because score-after-away is zero.");
        _ = ParseBaseMinute(options.Minute);
        if (!string.IsNullOrWhiteSpace(options.ConflictPolicy))
            _ = AfterGoalEntryGateBuilder.NormalizeConflictPolicy(options.ConflictPolicy);
    }

    private static async Task<List<EntryRuleRecord>> ReadEntryRulesAsync(string path, CancellationToken cancellationToken)
    {
        List<Dictionary<string, string>> rows = await ReadCsvAsync(path, RequiredEntryColumns, cancellationToken);
        return rows.Select(row => new EntryRuleRecord
        {
            LeagueKey = row["LeagueKey"],
            LeagueName = row["LeagueName"],
            Team = row["Team"],
            TriggerType = row["TriggerType"],
            SignalClass = row["SignalClass"],
            Direction = row["Direction"],
            EntryRuleStatus = row["EntryRuleStatus"],
            MarketGateRequired = !row.TryGetValue("MarketGateRequired", out string? marketGate) || !marketGate.Equals("false", StringComparison.OrdinalIgnoreCase),
            ConflictPolicy = row.TryGetValue("ConflictPolicy", out string? conflict) ? conflict : "NoBet",
            Reason = row.TryGetValue("Reason", out string? reason) ? reason : string.Empty
        }).ToList();
    }

    private static async Task<List<ContextGateRecord>> ReadContextGatesAsync(string path, CancellationToken cancellationToken)
    {
        List<Dictionary<string, string>> rows = await ReadCsvAsync(path, RequiredContextColumns, cancellationToken);
        return rows.Select(row => new ContextGateRecord
        {
            LeagueKey = row["LeagueKey"],
            Team = row["Team"],
            TriggerType = row["TriggerType"],
            SignalClass = row["SignalClass"],
            SignalDirection = row["SignalDirection"],
            StateDimension = row["StateDimension"],
            StateBucket = row["StateBucket"],
            GateStatus = row["GateStatus"],
            GateStrength = row["GateStrength"],
            TrainSampleSize = ParseInt(row, "TrainSampleSize"),
            TestSampleSize = ParseInt(row, "TestSampleSize"),
            TrainResidualVsBaseline = ParseNullableDouble(row, "TrainResidualVsBaseline"),
            TestResidualVsBaseline = ParseNullableDouble(row, "TestResidualVsBaseline"),
            Reason = row.TryGetValue("Reason", out string? reason) ? reason : string.Empty
        }).ToList();
    }

    private static async Task<List<Dictionary<string, string>>> ReadCsvAsync(string path, IReadOnlyList<string> requiredColumns, CancellationToken cancellationToken)
    {
        RequireFile(path, "CSV");
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new ArgumentException($"CSV file is empty: {path}");

        List<string> headers = CsvUtility.ParseLine(headerLine);
        foreach (string required in requiredColumns)
        {
            if (!headers.Contains(required, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"CSV file {path} is missing required column {required}.");
        }

        var result = new List<Dictionary<string, string>>();
        while (!reader.EndOfStream)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;
            List<string> values = CsvUtility.ParseLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
                row[headers[i]] = i < values.Count ? values[i] : string.Empty;
            result.Add(row);
        }

        return result;
    }

    private static readonly string[] RequiredEntryColumns =
    [
        "LeagueKey",
        "LeagueName",
        "Team",
        "TriggerType",
        "SignalClass",
        "Direction",
        "EntryRuleStatus"
    ];

    private static readonly string[] RequiredContextColumns =
    [
        "LeagueKey",
        "Team",
        "TriggerType",
        "SignalClass",
        "SignalDirection",
        "StateDimension",
        "StateBucket",
        "GateStatus",
        "GateStrength"
    ];

    private static (string LeagueKey, string ConflictPolicy) ReadSummaryMetadata(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return (string.Empty, string.Empty);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        string leagueKey = document.RootElement.TryGetProperty("LeagueKey", out JsonElement leagueValue) && leagueValue.ValueKind == JsonValueKind.String
            ? leagueValue.GetString() ?? string.Empty
            : string.Empty;
        string conflictPolicy = document.RootElement.TryGetProperty("ConflictPolicy", out JsonElement conflictValue) && conflictValue.ValueKind == JsonValueKind.String
            ? conflictValue.GetString() ?? string.Empty
            : string.Empty;
        return (leagueKey, conflictPolicy);
    }

    private static EntryRuleRecord? FindRule(IReadOnlyList<EntryRuleRecord> rules, string leagueKey, string team, string triggerType)
        => rules
            .Where(x => x.LeagueKey.Equals(leagueKey, StringComparison.OrdinalIgnoreCase))
            .Where(x => TeamEquals(x.Team, team))
            .Where(x => x.TriggerType.Equals(triggerType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.SignalClass == "Strict" ? 0 : 1)
            .FirstOrDefault();

    private static IEnumerable<(string Dimension, string Bucket)> CurrentBuckets(AfterGoalLiveState state)
    {
        yield return ("MinuteBand", state.MinuteBand);
        yield return ("ScoreGapAfterBand", state.ScoreGapAfterBand);
        yield return ("TotalGoalsAfterBand", state.TotalGoalsAfterBand);
        yield return ("GameStateAfter", state.GameStateAfter);
    }

    private static void ValidateLeagueKey(string source, string expected, IEnumerable<string> keys)
    {
        List<string> distinct = keys.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        List<string> mismatches = distinct.Where(x => !x.Equals(expected, StringComparison.OrdinalIgnoreCase)).ToList();
        if (mismatches.Count > 0)
            throw new ArgumentException($"{source} LeagueKey {string.Join(", ", mismatches)} does not match requested LeagueKey {expected}.");
    }

    private static void RequireFile(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"Missing {label} path.");
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Required {label} file was not found: {fullPath}", fullPath);
    }

    private static bool TeamEquals(string left, string right)
        => NormalizeTeam(left).Equals(NormalizeTeam(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeTeam(string value)
        => string.Join(" ", (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

    private static string Empty(string value) => string.IsNullOrWhiteSpace(value) ? "<none>" : value;

    private static int ParseInt(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out string? raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

    private static double? ParseNullableDouble(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out string? raw) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
}
