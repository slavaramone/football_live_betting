using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LiveTotalsHelper.Tools;

public sealed class AfterGoalEntryGateOptions
{
    public string EventsPath { get; set; } = string.Empty;
    public string AnglesDirectory { get; set; } = string.Empty;
    public string ProfilesDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public string TrainFromSeason { get; set; } = string.Empty;
    public string TrainToSeason { get; set; } = string.Empty;
    public string TestSeason { get; set; } = string.Empty;
    public string ProfileLeagueKey { get; set; } = string.Empty;
    public bool IncludeWatchlist { get; set; } = true;
    public int MinTrainStateSample { get; set; } = 15;
    public int MinTestStateSample { get; set; } = 5;
    public double MinStateResidual { get; set; } = 0.05;
    public double StrongStateResidual { get; set; } = 0.15;
    public bool RequireTestConfirmation { get; set; } = true;
    public string ConflictPolicy { get; set; } = "NoBet";
    public bool MarketGateRequired { get; set; } = true;
}

public sealed class AfterGoalEntryGateResult
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string TrainSeasons { get; set; } = string.Empty;
    public string TestSeason { get; set; } = string.Empty;
    public int StrictSignalsAnalyzed { get; set; }
    public int WatchlistSignalsAnalyzed { get; set; }
    public List<AfterGoalContextGateRow> ContextGates { get; } = [];
    public List<AfterGoalEntryRuleRow> EntryRules { get; } = [];
    public List<string> Warnings { get; } = [];
    public int ActiveEntryRules => EntryRules.Count(x => x.EntryRuleStatus == "Active");
    public int WatchlistEntryRules => EntryRules.Count(x => x.EntryRuleStatus == "WatchlistOnly");
    public int ConditionalWeakRules => EntryRules.Count(x => x.EntryRuleStatus == "ConditionalWeak");
    public int WatchlistWeakRules => EntryRules.Count(x => x.EntryRuleStatus == "WatchlistWeak");
    public int TooThinRules => EntryRules.Count(x => x.EntryRuleStatus == "TooThin");
    public int NoUsableGateRules => EntryRules.Count(x => x.EntryRuleStatus == "NoUsableGates");
}

public sealed class AfterGoalContextGateRow
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public string SignalClass { get; set; } = string.Empty;
    public string SignalDirection { get; set; } = string.Empty;
    public string StateDimension { get; set; } = string.Empty;
    public string StateBucket { get; set; } = string.Empty;
    public string TrainSeasons { get; set; } = string.Empty;
    public string TestSeason { get; set; } = string.Empty;
    public int TrainSampleSize { get; set; }
    public int TestSampleSize { get; set; }
    public double? TrainAvgRemainingGoalsAfterGoal { get; set; }
    public double? TestAvgRemainingGoalsAfterGoal { get; set; }
    public double? TrainAvgBaselineExpectedRemainingGoals { get; set; }
    public double? TestAvgBaselineExpectedRemainingGoals { get; set; }
    public double? TrainResidualVsBaseline { get; set; }
    public double? TestResidualVsBaseline { get; set; }
    public string TrainBucketDirection { get; set; } = string.Empty;
    public string TestBucketDirection { get; set; } = string.Empty;
    public string GateStatus { get; set; } = string.Empty;
    public string GateStrength { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class AfterGoalEntryRuleRow
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public string SignalClass { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string AllowedMinuteBands { get; set; } = string.Empty;
    public string WeakAllowedMinuteBands { get; set; } = string.Empty;
    public string AvoidMinuteBands { get; set; } = string.Empty;
    public string AllowedScoreGapAfterBands { get; set; } = string.Empty;
    public string WeakAllowedScoreGapAfterBands { get; set; } = string.Empty;
    public string AvoidScoreGapAfterBands { get; set; } = string.Empty;
    public string AllowedTotalGoalsAfterBands { get; set; } = string.Empty;
    public string WeakAllowedTotalGoalsAfterBands { get; set; } = string.Empty;
    public string AvoidTotalGoalsAfterBands { get; set; } = string.Empty;
    public string AllowedGameStatesAfter { get; set; } = string.Empty;
    public string WeakAllowedGameStatesAfter { get; set; } = string.Empty;
    public string AvoidGameStatesAfter { get; set; } = string.Empty;
    public string EntryRuleStatus { get; set; } = string.Empty;
    public string EntryRuleConfidence { get; set; } = string.Empty;
    public bool MarketGateRequired { get; set; } = true;
    public string ConflictPolicy { get; set; } = "NoBet";
    public string CriticalDimensions { get; set; } = string.Empty;
    public string MissingUsableDimensions { get; set; } = string.Empty;
    public string WeakOnlyDimensions { get; set; } = string.Empty;
    public string ActiveAllowedDimensions { get; set; } = string.Empty;
    public string AvoidHeavyDimensions { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

internal sealed class AfterGoalEntrySignal
{
    public string LeagueKey { get; init; } = string.Empty;
    public string LeagueName { get; init; } = string.Empty;
    public string Team { get; init; } = string.Empty;
    public string TriggerType { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string SignalClass { get; init; } = string.Empty;

    public string Key => $"{LeagueKey}|{Team}|{TriggerType}|{SignalClass}|{Direction}";
}

public sealed class AfterGoalEntryGateBuilder
{
    private static readonly string[] EventRequiredColumns =
    [
        "LeagueKey",
        "LeagueName",
        "Season",
        "MatchId",
        "HomeTeam",
        "AwayTeam",
        "GoalIndex",
        "GoalMinuteBase",
        "GoalStoppageMinutes",
        "GoalMinuteElapsed",
        "Period",
        "ScoringTeam",
        "ConcedingTeam",
        "TotalGoalsAfter",
        "ScoreGapAfter",
        "HomeLeadAfter",
        "AwayLeadAfter",
        "IsEqualAfter",
        "RemainingGoalsAfterGoal",
        "MinutesToNextGoal"
    ];

    private static readonly IReadOnlyList<(string Dimension, string[] Buckets)> Dimensions =
    [
        ("MinuteBand", ["00-15", "16-30", "31-45+", "46-60", "61-75", "76-90+"]),
        ("ScoreGapAfterBand", ["Draw", "Lead1", "Lead2", "Lead3Plus"]),
        ("TotalGoalsAfterBand", ["1", "2", "3", "4", "5+"]),
        ("GameStateAfter", ["EqualAfter", "HomeLeadAfter", "AwayLeadAfter"]),
        ("Half", ["1H", "2H"])
    ];

    private static readonly string[] CriticalDimensions =
    [
        "MinuteBand",
        "ScoreGapAfterBand",
        "TotalGoalsAfterBand",
        "GameStateAfter"
    ];

    public async Task<AfterGoalEntryGateResult> BuildAsync(AfterGoalEntryGateOptions options, CancellationToken cancellationToken)
    {
        ValidateOptions(options);

        List<AfterGoalEventCsvRow> events = await ReadEventsAsync(options.EventsPath, cancellationToken);
        ValidateEvents(events, options);

        AfterGoalEntrySplit split = ResolveSplit(events, options);
        List<AfterGoalEventCsvRow> trainRows = events.Where(x => split.TrainSeasons.Contains(x.Season, StringComparer.OrdinalIgnoreCase)).ToList();
        List<AfterGoalEventCsvRow> testRows = events.Where(x => x.Season.Equals(split.TestSeason, StringComparison.OrdinalIgnoreCase)).ToList();
        if (trainRows.Count == 0)
            throw new ArgumentException("Train split has no after-goal event rows.");
        if (testRows.Count == 0)
            throw new ArgumentException($"Test season {split.TestSeason} has no after-goal event rows.");

        List<string> eventLeagueKeys = events.Select(x => x.LeagueKey).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        if (eventLeagueKeys.Count != 1)
            throw new ArgumentException($"Entry gate input must contain one LeagueKey. Found: {string.Join(", ", eventLeagueKeys)}.");

        string leagueKey = eventLeagueKeys[0];
        if (!string.IsNullOrWhiteSpace(options.ProfileLeagueKey) &&
            !leagueKey.Equals(options.ProfileLeagueKey, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Profile leagueKey {options.ProfileLeagueKey} does not match input LeagueKey {leagueKey}.");

        List<AfterGoalEntrySignal> strictSignals = await ReadStrictSignalsAsync(Path.Combine(options.ProfilesDirectory, "after-goal-usable-signals.csv"), cancellationToken);
        if (strictSignals.Count == 0)
            strictSignals = ReadStrictSignalsFromTeamProfiles(Path.Combine(options.ProfilesDirectory, "after-goal-team-profiles.csv"));

        var warnings = new List<string>();
        if (strictSignals.Count == 0)
            warnings.Add("No strict usable signals found.");

        List<AfterGoalEntrySignal> watchlistSignals = [];
        string watchlistPath = Path.Combine(options.ProfilesDirectory, "after-goal-watchlist-signals.csv");
        if (options.IncludeWatchlist)
        {
            if (File.Exists(watchlistPath))
                watchlistSignals = await ReadWatchlistSignalsAsync(watchlistPath, cancellationToken);
            else
                warnings.Add($"Watchlist requested but file is absent: {watchlistPath}");
        }

        List<AfterGoalEntrySignal> signals = strictSignals
            .Concat(watchlistSignals)
            .Where(x => x.LeagueKey.Equals(leagueKey, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.SignalClass == "Strict" ? 0 : 1)
            .ThenBy(x => x.Team)
            .ThenBy(x => x.TriggerType)
            .ToList();

        if (signals.Count == 0 && strictSignals.Concat(watchlistSignals).Any())
            throw new ArgumentException($"Events LeagueKey {leagueKey} does not match profile signal LeagueKey(s): {string.Join(", ", strictSignals.Concat(watchlistSignals).Select(x => x.LeagueKey).Distinct(StringComparer.OrdinalIgnoreCase))}.");

        var result = new AfterGoalEntryGateResult
        {
            LeagueKey = leagueKey,
            LeagueName = events.Select(x => x.LeagueName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
            TrainSeasons = string.Join(";", split.TrainSeasons),
            TestSeason = split.TestSeason,
            StrictSignalsAnalyzed = signals.Count(x => x.SignalClass == "Strict"),
            WatchlistSignalsAnalyzed = signals.Count(x => x.SignalClass == "Watchlist")
        };
        result.Warnings.AddRange(warnings);

        var baseline = new AfterGoalBaselineModel(trainRows, hasMultipleLeagues: false, minSample: 1);
        foreach (AfterGoalEntrySignal signal in signals)
        {
            foreach ((string dimension, string[] buckets) in Dimensions)
            {
                foreach (string bucket in buckets)
                {
                    AfterGoalContextGateRow row = BuildGateRow(signal, dimension, bucket, trainRows, testRows, baseline, split, options);
                    result.ContextGates.Add(row);
                }
            }

            result.EntryRules.Add(BuildEntryRule(signal, result.ContextGates.Where(x => IsSignalGate(x, signal)).ToList(), options));
        }

        ValidateEntryRules(result.EntryRules);
        SortResult(result);
        return result;
    }

    private static AfterGoalContextGateRow BuildGateRow(
        AfterGoalEntrySignal signal,
        string dimension,
        string bucket,
        IReadOnlyList<AfterGoalEventCsvRow> trainRows,
        IReadOnlyList<AfterGoalEventCsvRow> testRows,
        AfterGoalBaselineModel baseline,
        AfterGoalEntrySplit split,
        AfterGoalEntryGateOptions options)
    {
        List<ScoredEntryEvent> train = MatchingEvents(trainRows, signal)
            .Where(x => StateBucket(x, dimension) == bucket)
            .Select(x => new ScoredEntryEvent(x, baseline.Expect(x)))
            .ToList();
        List<ScoredEntryEvent> test = MatchingEvents(testRows, signal)
            .Where(x => StateBucket(x, dimension) == bucket)
            .Select(x => new ScoredEntryEvent(x, baseline.Expect(x)))
            .ToList();

        double? trainAvg = train.Count == 0 ? null : train.Average(x => x.Row.RemainingGoalsAfterGoal);
        double? testAvg = test.Count == 0 ? null : test.Average(x => x.Row.RemainingGoalsAfterGoal);
        double? trainBase = train.Count == 0 ? null : train.Average(x => x.Baseline.ExpectedRemainingGoals);
        double? testBase = test.Count == 0 ? null : test.Average(x => x.Baseline.ExpectedRemainingGoals);
        double? trainResidual = train.Count == 0 ? null : train.Average(x => x.Row.RemainingGoalsAfterGoal - x.Baseline.ExpectedRemainingGoals);
        double? testResidual = test.Count == 0 ? null : test.Average(x => x.Row.RemainingGoalsAfterGoal - x.Baseline.ExpectedRemainingGoals);

        string trainDirection = Direction(trainResidual);
        string testDirection = Direction(testResidual);
        (string status, string strength, string reason) = ClassifyGate(signal.Direction, dimension, bucket, train.Count, test.Count, trainDirection, testDirection, trainResidual, testResidual, options);

        return new AfterGoalContextGateRow
        {
            LeagueKey = signal.LeagueKey,
            LeagueName = signal.LeagueName,
            Team = signal.Team,
            TriggerType = signal.TriggerType,
            SignalClass = signal.SignalClass,
            SignalDirection = signal.Direction,
            StateDimension = dimension,
            StateBucket = bucket,
            TrainSeasons = string.Join(";", split.TrainSeasons),
            TestSeason = split.TestSeason,
            TrainSampleSize = train.Count,
            TestSampleSize = test.Count,
            TrainAvgRemainingGoalsAfterGoal = trainAvg,
            TestAvgRemainingGoalsAfterGoal = testAvg,
            TrainAvgBaselineExpectedRemainingGoals = trainBase,
            TestAvgBaselineExpectedRemainingGoals = testBase,
            TrainResidualVsBaseline = trainResidual,
            TestResidualVsBaseline = testResidual,
            TrainBucketDirection = trainDirection,
            TestBucketDirection = testDirection,
            GateStatus = status,
            GateStrength = strength,
            Reason = reason
        };
    }

    private static (string Status, string Strength, string Reason) ClassifyGate(
        string signalDirection,
        string dimension,
        string bucket,
        int trainSample,
        int testSample,
        string trainDirection,
        string testDirection,
        double? trainResidual,
        double? testResidual,
        AfterGoalEntryGateOptions options)
    {
        if (trainSample == 0 && testSample == 0)
            return ("NoData", "None", "No data for this signal/state bucket.");

        bool enoughTrain = trainSample >= options.MinTrainStateSample;
        bool enoughTest = testSample >= options.MinTestStateSample;
        bool halfTrain = trainSample >= Math.Max(1, (int)Math.Ceiling(options.MinTrainStateSample / 2.0));
        bool halfTest = testSample >= Math.Max(1, (int)Math.Ceiling(options.MinTestStateSample / 2.0));
        bool trainConfirms = trainDirection == signalDirection;
        bool testConfirms = testDirection == signalDirection;
        bool trainOpposes = IsOpposite(trainDirection, signalDirection);
        bool testOpposes = IsOpposite(testDirection, signalDirection);
        double absTest = Math.Abs(testResidual.GetValueOrDefault());
        bool conservativeBucket = IsConservativeDefaultAvoid(dimension, bucket);

        if ((enoughTrain && trainOpposes) || (enoughTest && testOpposes && absTest >= options.MinStateResidual))
            return ("Avoid", "None", $"Avoid: signal is {signalDirection} but this bucket tested {Opposite(signalDirection)} with residual {Signed(testResidual ?? trainResidual ?? 0)}.");

        if (conservativeBucket && !(enoughTrain && enoughTest && trainConfirms && testConfirms && absTest >= options.StrongStateResidual))
            return ("Avoid", "None", $"Avoid: {bucket} is conservative-default bucket and signal was not strongly confirmed there.");

        if (!enoughTrain || !enoughTest)
        {
            if (trainConfirms && (!options.RequireTestConfirmation || testConfirms) && halfTrain && halfTest && absTest > 0)
                return ("WeakAllowed", "Weak", $"Weak {signalDirection} gate: direction confirmed but sample {trainSample}/{testSample} is below threshold {options.MinTrainStateSample}/{options.MinTestStateSample}.");

            return ("LowSample", "None", $"Low sample: train {trainSample}, test {testSample}; required {options.MinTrainStateSample}/{options.MinTestStateSample}.");
        }

        if (trainConfirms && (!options.RequireTestConfirmation || testConfirms) && absTest >= options.MinStateResidual)
        {
            string strength = absTest >= options.StrongStateResidual ? "Strong" : "Medium";
            return ("Allowed", strength, $"Allowed {signalDirection} gate: test residual {Signed(testResidual.GetValueOrDefault())} over {testSample} events; train also {trainDirection.ToLowerInvariant()}.");
        }

        if (trainConfirms && (!options.RequireTestConfirmation || testConfirms) && absTest > 0)
            return ("WeakAllowed", "Weak", $"Weak {signalDirection} gate: direction confirmed but test residual {Signed(testResidual.GetValueOrDefault())} is below {options.MinStateResidual.ToString("0.####", CultureInfo.InvariantCulture)}.");

        return ("Inconclusive", "None", $"Inconclusive: train direction {trainDirection}, test direction {testDirection}.");
    }

    private static AfterGoalEntryRuleRow BuildEntryRule(AfterGoalEntrySignal signal, IReadOnlyList<AfterGoalContextGateRow> gates, AfterGoalEntryGateOptions options)
    {
        List<EntryDimensionDiagnostic> diagnostics = CriticalDimensions
            .Select(dimension => EntryDimensionDiagnostic.From(dimension, gates.Where(x => x.StateDimension == dimension)))
            .ToList();

        var row = new AfterGoalEntryRuleRow
        {
            LeagueKey = signal.LeagueKey,
            LeagueName = signal.LeagueName,
            Team = signal.Team,
            TriggerType = signal.TriggerType,
            SignalClass = signal.SignalClass,
            Direction = signal.Direction,
            AllowedMinuteBands = Buckets(gates, "MinuteBand", "Allowed"),
            WeakAllowedMinuteBands = Buckets(gates, "MinuteBand", "WeakAllowed"),
            AvoidMinuteBands = Buckets(gates, "MinuteBand", "Avoid"),
            AllowedScoreGapAfterBands = Buckets(gates, "ScoreGapAfterBand", "Allowed"),
            WeakAllowedScoreGapAfterBands = Buckets(gates, "ScoreGapAfterBand", "WeakAllowed"),
            AvoidScoreGapAfterBands = Buckets(gates, "ScoreGapAfterBand", "Avoid"),
            AllowedTotalGoalsAfterBands = Buckets(gates, "TotalGoalsAfterBand", "Allowed"),
            WeakAllowedTotalGoalsAfterBands = Buckets(gates, "TotalGoalsAfterBand", "WeakAllowed"),
            AvoidTotalGoalsAfterBands = Buckets(gates, "TotalGoalsAfterBand", "Avoid"),
            AllowedGameStatesAfter = Buckets(gates, "GameStateAfter", "Allowed"),
            WeakAllowedGameStatesAfter = Buckets(gates, "GameStateAfter", "WeakAllowed"),
            AvoidGameStatesAfter = Buckets(gates, "GameStateAfter", "Avoid"),
            MarketGateRequired = options.MarketGateRequired,
            ConflictPolicy = NormalizeConflictPolicy(options.ConflictPolicy),
            CriticalDimensions = string.Join(";", CriticalDimensions),
            MissingUsableDimensions = string.Join(";", diagnostics.Where(x => x.HasNoUsable).Select(x => x.Dimension)),
            WeakOnlyDimensions = string.Join(";", diagnostics.Where(x => x.HasOnlyWeak).Select(x => x.Dimension)),
            ActiveAllowedDimensions = string.Join(";", diagnostics.Where(x => x.HasAllowed).Select(x => x.Dimension)),
            AvoidHeavyDimensions = string.Join(";", diagnostics.Where(x => x.IsAvoidHeavy).Select(x => x.Dimension))
        };

        bool allCriticalAllowed = diagnostics.All(x => x.HasAllowed);
        bool allCriticalUsable = diagnostics.All(x => x.HasUsable);
        bool anyWeakOnly = diagnostics.Any(x => x.HasOnlyWeak);
        bool mostlyThin = diagnostics.Count(x => x.IsMostlyThin) >= 3;
        bool anyAvoidEvidence = diagnostics.Any(x => x.AvoidCount > 0 && !x.IsMostlyThin);

        row.EntryRuleStatus = signal.SignalClass == "Strict"
            ? allCriticalAllowed
                ? "Active"
                : allCriticalUsable
                    ? "ConditionalWeak"
                    : mostlyThin && !anyAvoidEvidence
                        ? "TooThin"
                        : "NoUsableGates"
            : allCriticalAllowed
                ? "WatchlistOnly"
                : allCriticalUsable
                    ? "WatchlistWeak"
                    : mostlyThin && !anyAvoidEvidence
                        ? "TooThin"
                        : "NoUsableGates";

        int avoidHeavyCount = diagnostics.Count(x => x.IsAvoidHeavy);
        row.EntryRuleConfidence = row.EntryRuleStatus switch
        {
            "Active" when avoidHeavyCount == 0 && signal.SignalClass == "Strict" => "HIGH",
            "Active" => "MEDIUM",
            "ConditionalWeak" when signal.SignalClass == "Strict" && avoidHeavyCount == 0 => "MEDIUM",
            "ConditionalWeak" => "LOW",
            "WatchlistOnly" => "LOW",
            "WatchlistWeak" => "LOW",
            _ => "NONE"
        };

        string missing = row.MissingUsableDimensions;
        string weakOnly = row.WeakOnlyDimensions;
        row.Reason = row.EntryRuleStatus switch
        {
            "Active" => "Active: all critical dimensions have allowed gates; market gate still required.",
            "ConditionalWeak" => $"ConditionalWeak: all critical dimensions have usable gates, but {weakOnly} {(weakOnly.Contains(';') ? "are" : "is")} weak-only.",
            "WatchlistOnly" => "WatchlistOnly: all critical dimensions have allowed gates; market gate still required.",
            "WatchlistWeak" => $"WatchlistWeak: all critical dimensions have usable gates, but {weakOnly} {(weakOnly.Contains(';') ? "are" : "is")} weak-only.",
            "TooThin" => "TooThin: missing usable gates because critical dimensions are mostly low-sample/no-data.",
            _ when !string.IsNullOrWhiteSpace(missing) => $"NoUsableGates: missing usable gates for {missing}.",
            _ => "NoUsableGates: signal exists but critical state coverage is unusable."
        };

        return row;
    }

    private static void ValidateEntryRules(IReadOnlyList<AfterGoalEntryRuleRow> rows)
    {
        foreach (AfterGoalEntryRuleRow row in rows)
        {
            if (row.EntryRuleStatus == "Active" && CriticalDimensions.Any(dimension => !HasAllowedForDimension(row, dimension)))
                throw new InvalidOperationException($"Invalid Active entry rule for {row.Team} {row.TriggerType}: at least one critical dimension has no allowed bucket.");
            if (row.EntryRuleStatus == "WatchlistOnly" && CriticalDimensions.Any(dimension => !HasAllowedForDimension(row, dimension)))
                throw new InvalidOperationException($"Invalid WatchlistOnly entry rule for {row.Team} {row.TriggerType}: at least one critical dimension has no allowed bucket.");
            if (row.EntryRuleStatus == "ConditionalWeak" && !string.IsNullOrWhiteSpace(row.MissingUsableDimensions))
                throw new InvalidOperationException($"Invalid ConditionalWeak entry rule for {row.Team} {row.TriggerType}: missing usable dimensions {row.MissingUsableDimensions}.");
            if (row.EntryRuleStatus == "NoUsableGates" && string.IsNullOrWhiteSpace(row.MissingUsableDimensions))
                throw new InvalidOperationException($"Invalid NoUsableGates entry rule for {row.Team} {row.TriggerType}: all critical dimensions are usable.");
        }
    }

    private static bool HasAllowedForDimension(AfterGoalEntryRuleRow row, string dimension)
        => dimension switch
        {
            "MinuteBand" => !string.IsNullOrWhiteSpace(row.AllowedMinuteBands),
            "ScoreGapAfterBand" => !string.IsNullOrWhiteSpace(row.AllowedScoreGapAfterBands),
            "TotalGoalsAfterBand" => !string.IsNullOrWhiteSpace(row.AllowedTotalGoalsAfterBands),
            "GameStateAfter" => !string.IsNullOrWhiteSpace(row.AllowedGameStatesAfter),
            _ => false
        };

    private static IEnumerable<AfterGoalEventCsvRow> MatchingEvents(IEnumerable<AfterGoalEventCsvRow> rows, AfterGoalEntrySignal signal)
    {
        return signal.TriggerType switch
        {
            "AfterScoring" => rows.Where(x => x.ScoringTeam.Equals(signal.Team, StringComparison.OrdinalIgnoreCase)),
            "AfterConceding" => rows.Where(x => x.ConcedingTeam.Equals(signal.Team, StringComparison.OrdinalIgnoreCase)),
            _ => []
        };
    }

    private static string StateBucket(AfterGoalEventCsvRow row, string dimension)
        => dimension switch
        {
            "MinuteBand" => row.MinuteBand,
            "ScoreGapAfterBand" => row.ScoreGapAfterBand,
            "TotalGoalsAfterBand" => row.TotalGoalsAfterBand,
            "GameStateAfter" => row.GameStateAfter,
            "Half" => row.Half,
            _ => string.Empty
        };

    private static async Task<List<AfterGoalEventCsvRow>> ReadEventsAsync(string path, CancellationToken cancellationToken)
    {
        RequireFile(path);
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new ArgumentException($"Input file is empty: {path}");

        List<string> headers = CsvUtility.ParseLine(headerLine);
        var index = headers.Select((name, i) => new { name, i }).ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);
        List<string> missing = EventRequiredColumns.Where(x => !index.ContainsKey(x)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"After-goal events file is missing required columns: {string.Join(", ", missing)}");

        var rows = new List<AfterGoalEventCsvRow>();
        while (!reader.EndOfStream)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;
            List<string> values = CsvUtility.ParseLine(line);
            rows.Add(new AfterGoalEventCsvRow
            {
                LeagueKey = Get(values, index, "LeagueKey"),
                LeagueName = Get(values, index, "LeagueName"),
                Season = Get(values, index, "Season"),
                MatchId = Get(values, index, "MatchId"),
                HomeTeam = Get(values, index, "HomeTeam"),
                AwayTeam = Get(values, index, "AwayTeam"),
                GoalIndex = GetInt(values, index, "GoalIndex"),
                GoalMinuteBase = GetInt(values, index, "GoalMinuteBase"),
                GoalStoppageMinutes = GetInt(values, index, "GoalStoppageMinutes"),
                GoalMinuteElapsed = GetInt(values, index, "GoalMinuteElapsed"),
                Period = Get(values, index, "Period"),
                ScoringTeam = Get(values, index, "ScoringTeam"),
                ConcedingTeam = Get(values, index, "ConcedingTeam"),
                TotalGoalsAfter = GetInt(values, index, "TotalGoalsAfter"),
                ScoreGapAfter = GetInt(values, index, "ScoreGapAfter"),
                HomeLeadAfter = GetInt(values, index, "HomeLeadAfter"),
                AwayLeadAfter = GetInt(values, index, "AwayLeadAfter"),
                IsEqualAfter = GetBool(values, index, "IsEqualAfter"),
                RemainingGoalsAfterGoal = GetDouble(values, index, "RemainingGoalsAfterGoal"),
                MinutesToNextGoal = Get(values, index, "MinutesToNextGoal")
            });
        }

        return rows;
    }

    private static async Task<List<AfterGoalEntrySignal>> ReadStrictSignalsAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required strict signal file was not found: {Path.GetFullPath(path)}", path);

        return await ReadSignalsAsync(path, "Strict", cancellationToken);
    }

    private static async Task<List<AfterGoalEntrySignal>> ReadWatchlistSignalsAsync(string path, CancellationToken cancellationToken)
        => await ReadSignalsAsync(path, "Watchlist", cancellationToken);

    private static async Task<List<AfterGoalEntrySignal>> ReadSignalsAsync(string path, string signalClass, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            return [];

        List<string> headers = CsvUtility.ParseLine(headerLine);
        var index = headers.Select((name, i) => new { name, i }).ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);
        foreach (string required in new[] { "LeagueKey", "LeagueName", "Team", "TriggerType", "Direction" })
        {
            if (!index.ContainsKey(required))
                throw new ArgumentException($"Signal file {path} is missing required column {required}.");
        }

        var rows = new List<AfterGoalEntrySignal>();
        while (!reader.EndOfStream)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            List<string> values = CsvUtility.ParseLine(line);
            string direction = NormalizeDirection(Get(values, index, "Direction"));
            string trigger = NormalizeTriggerType(Get(values, index, "TriggerType"));
            if (direction is not ("OVER" or "UNDER") || string.IsNullOrWhiteSpace(trigger))
                continue;

            rows.Add(new AfterGoalEntrySignal
            {
                LeagueKey = Get(values, index, "LeagueKey"),
                LeagueName = Get(values, index, "LeagueName"),
                Team = Get(values, index, "Team"),
                TriggerType = trigger,
                Direction = direction,
                SignalClass = signalClass
            });
        }

        return rows;
    }

    private static List<AfterGoalEntrySignal> ReadStrictSignalsFromTeamProfiles(string path)
    {
        if (!File.Exists(path))
            return [];

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length <= 1)
            return [];
        List<string> headers = CsvUtility.ParseLine(lines[0]);
        var index = headers.Select((name, i) => new { name, i }).ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);
        foreach (string required in new[] { "LeagueKey", "LeagueName", "Team" })
        {
            if (!index.ContainsKey(required))
                return [];
        }

        var rows = new List<AfterGoalEntrySignal>();
        foreach (string line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            List<string> values = CsvUtility.ParseLine(line);
            AddProfileSignal(rows, values, index, "AfterScoring", "AfterScoringUsable", "AfterScoringProfile");
            AddProfileSignal(rows, values, index, "AfterConceding", "AfterConcedingUsable", "AfterConcedingProfile");
        }

        return rows;
    }

    private static void AddProfileSignal(List<AfterGoalEntrySignal> rows, List<string> values, IReadOnlyDictionary<string, int> index, string triggerType, string usableColumn, string profileColumn)
    {
        if (!index.ContainsKey(usableColumn) || !index.ContainsKey(profileColumn) || !GetBool(values, index, usableColumn))
            return;

        string profile = Get(values, index, profileColumn);
        string direction = profile.Contains("Over", StringComparison.OrdinalIgnoreCase)
            ? "OVER"
            : profile.Contains("Under", StringComparison.OrdinalIgnoreCase)
                ? "UNDER"
                : string.Empty;
        if (string.IsNullOrWhiteSpace(direction))
            return;

        rows.Add(new AfterGoalEntrySignal
        {
            LeagueKey = Get(values, index, "LeagueKey"),
            LeagueName = Get(values, index, "LeagueName"),
            Team = Get(values, index, "Team"),
            TriggerType = triggerType,
            Direction = direction,
            SignalClass = "Strict"
        });
    }

    private static void ValidateEvents(IReadOnlyList<AfterGoalEventCsvRow> events, AfterGoalEntryGateOptions options)
    {
        if (events.Count == 0)
            throw new ArgumentException("after-goal-events.csv has no data rows.");

        int negativeMinutes = events.Count(x => !string.IsNullOrWhiteSpace(x.MinutesToNextGoal)
            && int.TryParse(x.MinutesToNextGoal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            && value < 0);
        if (negativeMinutes > 0)
            throw new ArgumentException($"after-goal-events.csv has {negativeMinutes} negative MinutesToNextGoal rows.");

        int duplicateKeys = events.GroupBy(x => $"{x.MatchId}|{x.GoalIndex}", StringComparer.OrdinalIgnoreCase).Count(x => x.Count() > 1);
        if (duplicateKeys > 0)
            throw new ArgumentException($"after-goal-events.csv has {duplicateKeys} duplicate MatchId + GoalIndex keys.");

        RequireFile(Path.Combine(options.AnglesDirectory, "after-goal-angle-analysis-summary.json"));
        RequireFile(Path.Combine(options.ProfilesDirectory, "after-goal-team-profiles-summary.json"));
    }

    private static AfterGoalEntrySplit ResolveSplit(IReadOnlyList<AfterGoalEventCsvRow> rows, AfterGoalEntryGateOptions options)
    {
        List<string> seasons = rows.Select(x => x.Season)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(SeasonSortKey)
            .ThenBy(x => x)
            .ToList();

        bool explicitSplit = !string.IsNullOrWhiteSpace(options.TrainFromSeason) ||
                             !string.IsNullOrWhiteSpace(options.TrainToSeason) ||
                             !string.IsNullOrWhiteSpace(options.TestSeason);
        if (explicitSplit && (string.IsNullOrWhiteSpace(options.TrainFromSeason) ||
                              string.IsNullOrWhiteSpace(options.TrainToSeason) ||
                              string.IsNullOrWhiteSpace(options.TestSeason)))
            throw new ArgumentException("Provide all split options together: --train-from-season, --train-to-season, and --test-season.");

        if (explicitSplit)
        {
            if (!seasons.Contains(options.TestSeason, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Explicit test season {options.TestSeason} has zero rows. Available seasons: {string.Join(", ", seasons)}.");
            List<string> train = seasons
                .Where(x => CompareSeason(x, options.TrainFromSeason) >= 0)
                .Where(x => CompareSeason(x, options.TrainToSeason) <= 0)
                .ToList();
            if (train.Count == 0)
                throw new ArgumentException($"Train range {options.TrainFromSeason}-{options.TrainToSeason} selected zero rows.");
            if (train.Contains(options.TestSeason, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Train/test overlap: {options.TestSeason}.");
            return new AfterGoalEntrySplit(train, options.TestSeason);
        }

        if (seasons.Count < 2)
            throw new ArgumentException("Could not infer train/test split because fewer than two seasons are present.");

        string test = seasons[^1];
        return new AfterGoalEntrySplit(seasons.Where(x => !x.Equals(test, StringComparison.OrdinalIgnoreCase)).ToList(), test);
    }

    private static void SortResult(AfterGoalEntryGateResult result)
    {
        result.ContextGates.Sort((left, right) =>
        {
            int signalClass = SignalClassRank(left.SignalClass).CompareTo(SignalClassRank(right.SignalClass));
            if (signalClass != 0) return signalClass;
            int team = string.Compare(left.Team, right.Team, StringComparison.OrdinalIgnoreCase);
            if (team != 0) return team;
            int trigger = string.Compare(left.TriggerType, right.TriggerType, StringComparison.OrdinalIgnoreCase);
            if (trigger != 0) return trigger;
            int dim = DimensionRank(left.StateDimension).CompareTo(DimensionRank(right.StateDimension));
            if (dim != 0) return dim;
            return BucketRank(left.StateDimension, left.StateBucket).CompareTo(BucketRank(right.StateDimension, right.StateBucket));
        });

        result.EntryRules.Sort((left, right) =>
        {
            int status = EntryStatusRank(left.EntryRuleStatus).CompareTo(EntryStatusRank(right.EntryRuleStatus));
            if (status != 0) return status;
            int confidence = ConfidenceRank(left.EntryRuleConfidence).CompareTo(ConfidenceRank(right.EntryRuleConfidence));
            if (confidence != 0) return confidence;
            int signalClass = SignalClassRank(left.SignalClass).CompareTo(SignalClassRank(right.SignalClass));
            if (signalClass != 0) return signalClass;
            int team = string.Compare(left.Team, right.Team, StringComparison.OrdinalIgnoreCase);
            if (team != 0) return team;
            return string.Compare(left.TriggerType, right.TriggerType, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void ValidateOptions(AfterGoalEntryGateOptions options)
    {
        RequireFile(options.EventsPath);
        if (string.IsNullOrWhiteSpace(options.AnglesDirectory))
            throw new ArgumentException("Provide --angles-dir or --profile.");
        if (string.IsNullOrWhiteSpace(options.ProfilesDirectory))
            throw new ArgumentException("Provide --profiles-dir or --profile.");
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("Provide --output-dir or --profile.");
        if (options.MinTrainStateSample <= 0)
            throw new ArgumentException("--min-train-state-sample must be positive.");
        if (options.MinTestStateSample <= 0)
            throw new ArgumentException("--min-test-state-sample must be positive.");
        if (options.MinStateResidual < 0 || options.StrongStateResidual < 0)
            throw new ArgumentException("State residual thresholds must be non-negative.");
        _ = NormalizeConflictPolicy(options.ConflictPolicy);
    }

    private static void RequireFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Required input file was not found: {fullPath}", fullPath);
    }

    private static bool IsSignalGate(AfterGoalContextGateRow row, AfterGoalEntrySignal signal)
        => row.Team.Equals(signal.Team, StringComparison.OrdinalIgnoreCase) &&
           row.TriggerType.Equals(signal.TriggerType, StringComparison.OrdinalIgnoreCase) &&
           row.SignalClass.Equals(signal.SignalClass, StringComparison.OrdinalIgnoreCase) &&
           row.SignalDirection.Equals(signal.Direction, StringComparison.OrdinalIgnoreCase);

    private static string Buckets(IReadOnlyList<AfterGoalContextGateRow> gates, string dimension, string status)
        => string.Join(";", gates.Where(x => x.StateDimension == dimension && x.GateStatus == status).OrderBy(x => BucketRank(dimension, x.StateBucket)).Select(x => x.StateBucket));

    private static string Direction(double? value)
        => !value.HasValue || Math.Abs(value.Value) < 0.0000001 ? "NEUTRAL" : value.Value > 0 ? "OVER" : "UNDER";

    private static string Opposite(string direction)
        => direction == "OVER" ? "UNDER" : direction == "UNDER" ? "OVER" : "NEUTRAL";

    private static bool IsOpposite(string direction, string expected)
        => direction is "OVER" or "UNDER" && expected is "OVER" or "UNDER" && direction != expected;

    private static bool IsConservativeDefaultAvoid(string dimension, string bucket)
        => (dimension == "MinuteBand" && bucket == "76-90+") ||
           (dimension == "ScoreGapAfterBand" && bucket == "Lead3Plus") ||
           (dimension == "TotalGoalsAfterBand" && bucket == "5+");

    private static string NormalizeDirection(string value)
        => value.Equals("OVER", StringComparison.OrdinalIgnoreCase) ? "OVER" :
            value.Equals("UNDER", StringComparison.OrdinalIgnoreCase) ? "UNDER" : string.Empty;

    private static string NormalizeTriggerType(string value)
        => value.Equals("AfterScoring", StringComparison.OrdinalIgnoreCase) ? "AfterScoring" :
            value.Equals("AfterConceding", StringComparison.OrdinalIgnoreCase) ? "AfterConceding" : string.Empty;

    public static string NormalizeConflictPolicy(string value)
        => value.Equals("NoBet", StringComparison.OrdinalIgnoreCase) ? "NoBet" :
            value.Equals("PreferStrict", StringComparison.OrdinalIgnoreCase) ? "PreferStrict" :
            value.Equals("PreferScoring", StringComparison.OrdinalIgnoreCase) ? "PreferScoring" :
            value.Equals("PreferConceding", StringComparison.OrdinalIgnoreCase) ? "PreferConceding" :
            throw new ArgumentException("--conflict-policy must be NoBet, PreferStrict, PreferScoring, or PreferConceding.");

    private static int SignalClassRank(string value) => value == "Strict" ? 0 : 1;

    private static int DimensionRank(string value)
        => value switch
        {
            "MinuteBand" => 0,
            "ScoreGapAfterBand" => 1,
            "TotalGoalsAfterBand" => 2,
            "GameStateAfter" => 3,
            "Half" => 4,
            _ => 99
        };

    private static int BucketRank(string dimension, string bucket)
    {
        string[] buckets = Dimensions.FirstOrDefault(x => x.Dimension == dimension).Buckets ?? [];
        int index = Array.IndexOf(buckets, bucket);
        return index < 0 ? 99 : index;
    }

    private static int EntryStatusRank(string value)
        => value switch
        {
            "Active" => 0,
            "ConditionalWeak" => 1,
            "WatchlistOnly" => 2,
            "WatchlistWeak" => 3,
            "TooThin" => 4,
            "NoUsableGates" => 5,
            _ => 9
        };

    private static int ConfidenceRank(string value)
        => value switch
        {
            "HIGH" => 0,
            "MEDIUM" => 1,
            "LOW" => 2,
            _ => 3
        };

    private static int SeasonSortKey(string season)
        => int.TryParse(season, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : int.MaxValue;

    private static int CompareSeason(string left, string right)
    {
        bool leftNumeric = int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out int leftInt);
        bool rightNumeric = int.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rightInt);
        if (leftNumeric && rightNumeric)
            return leftInt.CompareTo(rightInt);

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string Signed(double value)
        => value.ToString("+0.0000;-0.0000;0.0000", CultureInfo.InvariantCulture);

    private static string Get(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> index, string name)
        => index.TryGetValue(name, out int i) && i < values.Count ? values[i] : string.Empty;

    private static int GetInt(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> index, string name)
        => int.TryParse(Get(values, index, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new ArgumentException($"Column {name} contains a non-integer value.");

    private static double GetDouble(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> index, string name)
        => double.TryParse(Get(values, index, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : throw new ArgumentException($"Column {name} contains a non-numeric value.");

    private static bool GetBool(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> index, string name)
        => bool.TryParse(Get(values, index, name), out bool parsed) && parsed;

    private sealed record AfterGoalEntrySplit(List<string> TrainSeasons, string TestSeason);
    private sealed record ScoredEntryEvent(AfterGoalEventCsvRow Row, BaselineExpectation Baseline);

    private sealed class EntryDimensionDiagnostic
    {
        public string Dimension { get; init; } = string.Empty;
        public int AllowedCount { get; init; }
        public int WeakAllowedCount { get; init; }
        public int AvoidCount { get; init; }
        public int LowSampleCount { get; init; }
        public int NoDataCount { get; init; }
        public int InconclusiveCount { get; init; }
        public bool HasAllowed => AllowedCount > 0;
        public bool HasWeakAllowed => WeakAllowedCount > 0;
        public bool HasUsable => HasAllowed || HasWeakAllowed;
        public bool HasOnlyWeak => !HasAllowed && HasWeakAllowed;
        public bool HasNoUsable => !HasUsable;
        public bool IsAvoidHeavy => AvoidCount > AllowedCount + WeakAllowedCount;
        public bool IsMostlyThin => LowSampleCount + NoDataCount > AvoidCount + AllowedCount + WeakAllowedCount + InconclusiveCount;

        public static EntryDimensionDiagnostic From(string dimension, IEnumerable<AfterGoalContextGateRow> gates)
        {
            List<AfterGoalContextGateRow> rows = gates.ToList();
            return new EntryDimensionDiagnostic
            {
                Dimension = dimension,
                AllowedCount = rows.Count(x => x.GateStatus == "Allowed"),
                WeakAllowedCount = rows.Count(x => x.GateStatus == "WeakAllowed"),
                AvoidCount = rows.Count(x => x.GateStatus == "Avoid"),
                LowSampleCount = rows.Count(x => x.GateStatus == "LowSample"),
                NoDataCount = rows.Count(x => x.GateStatus == "NoData"),
                InconclusiveCount = rows.Count(x => x.GateStatus == "Inconclusive")
            };
        }
    }
}

public static class AfterGoalEntryGateReportWriter
{
    public static async Task WriteAsync(string outputDirectory, AfterGoalEntryGateOptions options, AfterGoalEntryGateResult result, CancellationToken cancellationToken)
    {
        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);

        await WriteContextGatesAsync(Path.Combine(fullDirectory, "after-goal-profile-context-gates.csv"), result.ContextGates, cancellationToken);
        await WriteEntryRulesAsync(Path.Combine(fullDirectory, "after-goal-entry-rules.csv"), result.EntryRules, cancellationToken);

        var summary = new
        {
            InputEventsPath = Path.GetFullPath(options.EventsPath),
            AnglesDir = Path.GetFullPath(options.AnglesDirectory),
            ProfilesDir = Path.GetFullPath(options.ProfilesDirectory),
            OutputDir = fullDirectory,
            result.LeagueKey,
            result.LeagueName,
            TrainSeasons = result.TrainSeasons,
            result.TestSeason,
            options.IncludeWatchlist,
            options.MinTrainStateSample,
            options.MinTestStateSample,
            options.MinStateResidual,
            options.StrongStateResidual,
            options.RequireTestConfirmation,
            ConflictPolicy = AfterGoalEntryGateBuilder.NormalizeConflictPolicy(options.ConflictPolicy),
            result.StrictSignalsAnalyzed,
            result.WatchlistSignalsAnalyzed,
            TotalContextGateRows = result.ContextGates.Count,
            result.ActiveEntryRules,
            result.WatchlistEntryRules,
            result.ConditionalWeakRules,
            result.WatchlistWeakRules,
            result.TooThinRules,
            result.NoUsableGateRules,
            Warnings = result.Warnings,
            Timestamp = DateTimeOffset.UtcNow
        };

        string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(fullDirectory, "after-goal-entry-gates-summary.json"), json, Encoding.UTF8, cancellationToken);
    }

    private static async Task WriteContextGatesAsync(string path, IReadOnlyList<AfterGoalContextGateRow> rows, CancellationToken cancellationToken)
    {
        string[] headers =
        [
            "LeagueKey",
            "LeagueName",
            "Team",
            "TriggerType",
            "SignalClass",
            "SignalDirection",
            "StateDimension",
            "StateBucket",
            "TrainSeasons",
            "TestSeason",
            "TrainSampleSize",
            "TestSampleSize",
            "TrainAvgRemainingGoalsAfterGoal",
            "TestAvgRemainingGoalsAfterGoal",
            "TrainAvgBaselineExpectedRemainingGoals",
            "TestAvgBaselineExpectedRemainingGoals",
            "TrainResidualVsBaseline",
            "TestResidualVsBaseline",
            "TrainBucketDirection",
            "TestBucketDirection",
            "GateStatus",
            "GateStrength",
            "Reason"
        ];

        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync(string.Join(",", headers));
        foreach (AfterGoalContextGateRow row in rows)
            await writer.WriteLineAsync(CsvUtility.ToLine(ContextValues(row)));
    }

    private static async Task WriteEntryRulesAsync(string path, IReadOnlyList<AfterGoalEntryRuleRow> rows, CancellationToken cancellationToken)
    {
        string[] headers =
        [
            "LeagueKey",
            "LeagueName",
            "Team",
            "TriggerType",
            "SignalClass",
            "Direction",
            "AllowedMinuteBands",
            "WeakAllowedMinuteBands",
            "AvoidMinuteBands",
            "AllowedScoreGapAfterBands",
            "WeakAllowedScoreGapAfterBands",
            "AvoidScoreGapAfterBands",
            "AllowedTotalGoalsAfterBands",
            "WeakAllowedTotalGoalsAfterBands",
            "AvoidTotalGoalsAfterBands",
            "AllowedGameStatesAfter",
            "WeakAllowedGameStatesAfter",
            "AvoidGameStatesAfter",
            "EntryRuleStatus",
            "EntryRuleConfidence",
            "MarketGateRequired",
            "ConflictPolicy",
            "CriticalDimensions",
            "MissingUsableDimensions",
            "WeakOnlyDimensions",
            "ActiveAllowedDimensions",
            "AvoidHeavyDimensions",
            "Reason"
        ];

        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync(string.Join(",", headers));
        foreach (AfterGoalEntryRuleRow row in rows)
            await writer.WriteLineAsync(CsvUtility.ToLine(RuleValues(row)));
    }

    private static IEnumerable<string> ContextValues(AfterGoalContextGateRow row)
    {
        yield return row.LeagueKey;
        yield return row.LeagueName;
        yield return row.Team;
        yield return row.TriggerType;
        yield return row.SignalClass;
        yield return row.SignalDirection;
        yield return row.StateDimension;
        yield return row.StateBucket;
        yield return row.TrainSeasons;
        yield return row.TestSeason;
        yield return row.TrainSampleSize.ToString(CultureInfo.InvariantCulture);
        yield return row.TestSampleSize.ToString(CultureInfo.InvariantCulture);
        yield return Format(row.TrainAvgRemainingGoalsAfterGoal);
        yield return Format(row.TestAvgRemainingGoalsAfterGoal);
        yield return Format(row.TrainAvgBaselineExpectedRemainingGoals);
        yield return Format(row.TestAvgBaselineExpectedRemainingGoals);
        yield return Format(row.TrainResidualVsBaseline);
        yield return Format(row.TestResidualVsBaseline);
        yield return row.TrainBucketDirection;
        yield return row.TestBucketDirection;
        yield return row.GateStatus;
        yield return row.GateStrength;
        yield return row.Reason;
    }

    private static IEnumerable<string> RuleValues(AfterGoalEntryRuleRow row)
    {
        yield return row.LeagueKey;
        yield return row.LeagueName;
        yield return row.Team;
        yield return row.TriggerType;
        yield return row.SignalClass;
        yield return row.Direction;
        yield return row.AllowedMinuteBands;
        yield return row.WeakAllowedMinuteBands;
        yield return row.AvoidMinuteBands;
        yield return row.AllowedScoreGapAfterBands;
        yield return row.WeakAllowedScoreGapAfterBands;
        yield return row.AvoidScoreGapAfterBands;
        yield return row.AllowedTotalGoalsAfterBands;
        yield return row.WeakAllowedTotalGoalsAfterBands;
        yield return row.AvoidTotalGoalsAfterBands;
        yield return row.AllowedGameStatesAfter;
        yield return row.WeakAllowedGameStatesAfter;
        yield return row.AvoidGameStatesAfter;
        yield return row.EntryRuleStatus;
        yield return row.EntryRuleConfidence;
        yield return row.MarketGateRequired.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        yield return row.ConflictPolicy;
        yield return row.CriticalDimensions;
        yield return row.MissingUsableDimensions;
        yield return row.WeakOnlyDimensions;
        yield return row.ActiveAllowedDimensions;
        yield return row.AvoidHeavyDimensions;
        yield return row.Reason;
    }

    private static string Format(double? value)
        => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;
}
