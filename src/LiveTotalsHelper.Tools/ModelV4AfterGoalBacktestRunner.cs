using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LiveTotalsHelper.Tools;

public sealed class ModelV4AfterGoalBacktestOptions
{
    public string EventsPath { get; set; } = string.Empty;
    public string WorkDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public int TrainFromSeason { get; set; }
    public int TrainToSeason { get; set; }
    public int ValidationSeason { get; set; }
    public int TestSeason { get; set; }
    public string ProfileLeagueKey { get; set; } = string.Empty;
    public bool IncludeWatchlist { get; set; } = true;
    public string CandidateClasses { get; set; } = "Candidate;WeakCandidate;Watchlist";
    public int MinSample { get; set; } = 30;
    public int StrongSample { get; set; } = 80;
    public double ShrinkK { get; set; } = 50;
    public int MinTrainSample { get; set; } = 50;
    public int MinTestSample { get; set; } = 15;
    public double MinTrainAbsResidual { get; set; } = 0.10;
    public double MinTestAbsResidual { get; set; } = 0.05;
    public double StrongTestAbsResidual { get; set; } = 0.15;
    public bool RequireProfileTestConfirmation { get; set; } = true;
    public bool WatchlistEnabled { get; set; } = true;
    public int WatchlistTrainSampleTolerance { get; set; } = 10;
    public int WatchlistTestSampleTolerance { get; set; } = 5;
    public double WatchlistResidualTolerance { get; set; } = 0.03;
    public int MinTrainStateSample { get; set; } = 15;
    public int MinTestStateSample { get; set; } = 5;
    public double MinStateResidual { get; set; } = 0.05;
    public double StrongStateResidual { get; set; } = 0.15;
    public bool RequireGateTestConfirmation { get; set; } = true;
    public string ConflictPolicy { get; set; } = "NoBet";
    public bool MarketGateRequired { get; set; } = true;
    public string Format { get; set; } = "csv";
}

public sealed class ModelV4AfterGoalBacktestResult
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string InputEventsPath { get; set; } = string.Empty;
    public string WorkDir { get; set; } = string.Empty;
    public string OutputDir { get; set; } = string.Empty;
    public List<int> TrainingSeasons { get; } = [];
    public int ValidationSeason { get; set; }
    public int TestSeason { get; set; }
    public bool IncludeWatchlist { get; set; }
    public string CandidateClasses { get; set; } = string.Empty;
    public int TotalEventsRead { get; set; }
    public int TrainingRows { get; set; }
    public int ValidationRows { get; set; }
    public int TestRows { get; set; }
    public string FrozenAnglesDir { get; set; } = string.Empty;
    public string FrozenProfilesDir { get; set; } = string.Empty;
    public string FrozenEntryGatesDir { get; set; } = string.Empty;
    public string TestEventDecisionsFile { get; set; } = string.Empty;
    public string PerformanceSummaryFile { get; set; } = string.Empty;
    public string RulePerformanceFile { get; set; } = string.Empty;
    public int CandidateCount { get; set; }
    public int StrictCandidateCount { get; set; }
    public int WeakCandidateCount { get; set; }
    public int WatchlistCount { get; set; }
    public int AvoidCount { get; set; }
    public int NoSignalCount { get; set; }
    public double CandidateCoveragePct { get; set; }
    public double? CandidateDirectionHitRate { get; set; }
    public double? CandidateAvgDirectionalResidual { get; set; }
    public double? CandidateMedianDirectionalResidual { get; set; }
    public bool LeakageCheckPassed { get; set; }
    public List<int> RuleGenerationSeasons { get; } = [];
    public List<int> BaselineFitSeasons { get; } = [];
    public List<int> FinalTestOnlySeasons { get; } = [];
    public List<string> LeakageWarnings { get; } = [];
    public List<string> Warnings { get; } = [];
    public List<ModelV4BacktestEventDecision> EventDecisions { get; } = [];
    public List<ModelV4PerformanceSummaryRow> PerformanceRows { get; } = [];
    public List<ModelV4RulePerformanceRow> RulePerformanceRows { get; } = [];
}

public sealed class ModelV4AfterGoalBacktestRunner
{
    public async Task<ModelV4AfterGoalBacktestResult> RunAsync(ModelV4AfterGoalBacktestOptions options, CancellationToken cancellationToken)
    {
        ValidateOptions(options);

        List<ModelV4BacktestEventRow> rows = await ModelV4EventCsv.ReadAsync(options.EventsPath, cancellationToken);
        ValidateSplit(rows, options);

        var trainingSeasons = Enumerable.Range(options.TrainFromSeason, options.TrainToSeason - options.TrainFromSeason + 1).ToList();
        var ruleGenerationSeasons = trainingSeasons.Concat([options.ValidationSeason]).ToList();
        string workRoot = Path.Combine(Path.GetFullPath(options.WorkDirectory), "frozen-v4-rules");
        string anglesDir = Path.Combine(workRoot, "after-goal-angles");
        string profilesDir = Path.Combine(workRoot, "after-goal-profiles");
        string gatesDir = Path.Combine(workRoot, "after-goal-entry-gates");
        string outputRoot = Path.Combine(Path.GetFullPath(options.OutputDirectory), "model-v4-backtest");
        Directory.CreateDirectory(workRoot);
        Directory.CreateDirectory(outputRoot);

        string leagueKey = rows.Select(x => x.LeagueKey).Distinct(StringComparer.OrdinalIgnoreCase).Single();
        if (!string.IsNullOrWhiteSpace(options.ProfileLeagueKey) && !leagueKey.Equals(options.ProfileLeagueKey, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Profile leagueKey {options.ProfileLeagueKey} does not match events LeagueKey {leagueKey}.");

        string leagueName = rows.Select(x => x.LeagueName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        string frozenEventsPath = Path.Combine(workRoot, "frozen-after-goal-events.csv");
        await ModelV4EventCsv.WriteFilteredAsync(options.EventsPath, frozenEventsPath, ruleGenerationSeasons.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.OrdinalIgnoreCase), cancellationToken);

        var angleOptions = new AfterGoalAngleAnalysisOptions
        {
            InputPath = frozenEventsPath,
            OutputDirectory = anglesDir,
            TrainFromSeason = options.TrainFromSeason.ToString(CultureInfo.InvariantCulture),
            TrainToSeason = options.TrainToSeason.ToString(CultureInfo.InvariantCulture),
            TestSeason = options.ValidationSeason.ToString(CultureInfo.InvariantCulture),
            MinSample = options.MinSample,
            StrongSample = options.StrongSample,
            ShrinkK = options.ShrinkK
        };
        var angleResult = await new AfterGoalAngleAnalyzer().AnalyzeAsync(angleOptions, cancellationToken);
        await AfterGoalAngleReportWriter.WriteAsync(anglesDir, angleOptions, angleResult, cancellationToken);

        var profileOptions = new AfterGoalTeamProfileOptions
        {
            AnglesDirectory = anglesDir,
            OutputDirectory = profilesDir,
            MinTrainSample = options.MinTrainSample,
            MinTestSample = options.MinTestSample,
            MinTrainAbsResidual = options.MinTrainAbsResidual,
            MinTestAbsResidual = options.MinTestAbsResidual,
            StrongTestAbsResidual = options.StrongTestAbsResidual,
            RequireTestConfirmation = options.RequireProfileTestConfirmation,
            WatchlistEnabled = options.WatchlistEnabled,
            WatchlistTrainSampleTolerance = options.WatchlistTrainSampleTolerance,
            WatchlistTestSampleTolerance = options.WatchlistTestSampleTolerance,
            WatchlistResidualTolerance = options.WatchlistResidualTolerance
        };
        var profileResult = await new AfterGoalTeamProfileBuilder().BuildAsync(profileOptions, cancellationToken);
        await AfterGoalTeamProfileReportWriter.WriteAsync(profilesDir, profileOptions, profileResult, cancellationToken);

        var gateOptions = new AfterGoalEntryGateOptions
        {
            EventsPath = frozenEventsPath,
            AnglesDirectory = anglesDir,
            ProfilesDirectory = profilesDir,
            OutputDirectory = gatesDir,
            TrainFromSeason = options.TrainFromSeason.ToString(CultureInfo.InvariantCulture),
            TrainToSeason = options.TrainToSeason.ToString(CultureInfo.InvariantCulture),
            TestSeason = options.ValidationSeason.ToString(CultureInfo.InvariantCulture),
            ProfileLeagueKey = options.ProfileLeagueKey,
            IncludeWatchlist = options.IncludeWatchlist,
            MinTrainStateSample = options.MinTrainStateSample,
            MinTestStateSample = options.MinTestStateSample,
            MinStateResidual = options.MinStateResidual,
            StrongStateResidual = options.StrongStateResidual,
            RequireTestConfirmation = options.RequireGateTestConfirmation,
            ConflictPolicy = options.ConflictPolicy,
            MarketGateRequired = options.MarketGateRequired
        };
        AfterGoalEntryGateResult gateResult = await new AfterGoalEntryGateBuilder().BuildAsync(gateOptions, cancellationToken);
        await AfterGoalEntryGateReportWriter.WriteAsync(gatesDir, gateOptions, gateResult, cancellationToken);

        var result = new ModelV4AfterGoalBacktestResult
        {
            LeagueKey = leagueKey,
            LeagueName = leagueName,
            InputEventsPath = Path.GetFullPath(options.EventsPath),
            WorkDir = workRoot,
            OutputDir = outputRoot,
            ValidationSeason = options.ValidationSeason,
            TestSeason = options.TestSeason,
            IncludeWatchlist = options.IncludeWatchlist,
            CandidateClasses = NormalizeCandidateClasses(options.CandidateClasses),
            TotalEventsRead = rows.Count,
            TrainingRows = rows.Count(x => trainingSeasons.Contains(ParseSeason(x.Season))),
            ValidationRows = rows.Count(x => ParseSeason(x.Season) == options.ValidationSeason),
            TestRows = rows.Count(x => ParseSeason(x.Season) == options.TestSeason),
            FrozenAnglesDir = anglesDir,
            FrozenProfilesDir = profilesDir,
            FrozenEntryGatesDir = gatesDir,
            TestEventDecisionsFile = Path.Combine(outputRoot, "model-v4-test-event-decisions.csv"),
            PerformanceSummaryFile = Path.Combine(outputRoot, "model-v4-performance-summary.csv"),
            RulePerformanceFile = Path.Combine(outputRoot, "model-v4-rule-performance.csv"),
            LeakageCheckPassed = true
        };
        result.TrainingSeasons.AddRange(trainingSeasons);
        result.RuleGenerationSeasons.AddRange(ruleGenerationSeasons);
        result.BaselineFitSeasons.AddRange(ruleGenerationSeasons);
        result.FinalTestOnlySeasons.Add(options.TestSeason);
        result.Warnings.AddRange(angleResult.Warnings);
        result.Warnings.AddRange(profileResult.Warnings);
        result.Warnings.AddRange(gateResult.Warnings);

        ValidateNoLeakage(result, gateResult, angleResult);

        var baselineRows = rows.Where(x => ruleGenerationSeasons.Contains(ParseSeason(x.Season))).Select(x => x.ToAngleRow()).ToList();
        var testRows = rows.Where(x => ParseSeason(x.Season) == options.TestSeason)
            .OrderBy(x => x.MatchDate)
            .ThenBy(x => x.MatchId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.GoalIndex)
            .ToList();
        bool hasMultipleLeagues = rows.Select(x => x.LeagueKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        var baseline = new AfterGoalBaselineModel(baselineRows, hasMultipleLeagues, Math.Max(1, options.MinSample));
        var evaluator = new AfterGoalEntryEvaluator();
        string entryRulesPath = Path.Combine(gatesDir, "after-goal-entry-rules.csv");
        string contextGatesPath = Path.Combine(gatesDir, "after-goal-profile-context-gates.csv");
        string summaryPath = Path.Combine(gatesDir, "after-goal-entry-gates-summary.json");
        var candidateClasses = ParseCandidateClasses(options.CandidateClasses);

        foreach (ModelV4BacktestEventRow row in testRows)
        {
            BaselineExpectation expectation = baseline.Expect(row.ToAngleRow());
            var evaluation = await evaluator.EvaluateAsync(new AfterGoalEntryEvaluationOptions
            {
                EntryRulesPath = entryRulesPath,
                ContextGatesPath = contextGatesPath,
                SummaryPath = summaryPath,
                LeagueKey = leagueKey,
                HomeTeam = row.HomeTeam,
                AwayTeam = row.AwayTeam,
                ScoringTeam = row.ScoringTeam,
                ConcedingTeam = row.ConcedingTeam,
                Minute = row.GoalMinuteDisplay,
                ScoreAfterHome = row.ScoreAfterHome,
                ScoreAfterAway = row.ScoreAfterAway,
                ConflictPolicy = options.ConflictPolicy
            }, cancellationToken);
            result.EventDecisions.Add(ModelV4BacktestEventDecision.From(row, evaluation, expectation, candidateClasses));
        }

        result.CandidateCount = result.EventDecisions.Count(x => x.IsCandidate);
        result.StrictCandidateCount = result.EventDecisions.Count(x => x.IsStrictCandidate);
        result.WeakCandidateCount = result.EventDecisions.Count(x => x.IsWeakCandidate);
        result.WatchlistCount = result.EventDecisions.Count(x => x.IsWatchlist);
        result.AvoidCount = result.EventDecisions.Count(x => x.IsAvoid);
        result.NoSignalCount = result.EventDecisions.Count(x => x.IsNoSignal);
        result.CandidateCoveragePct = result.TestRows == 0 ? 0 : (double)result.CandidateCount / result.TestRows;
        var candidateDecisions = result.EventDecisions.Where(x => x.IsCandidate && x.DirectionalResidual.HasValue).ToList();
        result.CandidateDirectionHitRate = Rate(candidateDecisions.Count(x => x.DirectionCorrect == true), candidateDecisions.Count);
        result.CandidateAvgDirectionalResidual = Avg(candidateDecisions.Select(x => x.DirectionalResidual));
        result.CandidateMedianDirectionalResidual = Median(candidateDecisions.Select(x => x.DirectionalResidual));

        result.PerformanceRows.AddRange(BuildPerformanceRows(result.EventDecisions));
        result.RulePerformanceRows.AddRange(BuildRuleRows(gateResult.EntryRules, result.EventDecisions));

        await ModelV4BacktestReportWriter.WriteAsync(result, options.Format, cancellationToken);
        await WriteFrozenConfigAsync(Path.Combine(workRoot, "frozen-model-v4-config.json"), options, result, cancellationToken);

        return result;
    }

    private static void ValidateOptions(ModelV4AfterGoalBacktestOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EventsPath) || !File.Exists(Path.GetFullPath(options.EventsPath)))
            throw new FileNotFoundException($"After-goal events file was not found: {Path.GetFullPath(options.EventsPath)}", Path.GetFullPath(options.EventsPath));
        if (string.IsNullOrWhiteSpace(options.WorkDirectory))
            throw new ArgumentException("Provide --work-dir or --profile.");
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            throw new ArgumentException("Provide --output-dir or --profile.");
        if (options.TrainFromSeason <= 0 || options.TrainToSeason <= 0 || options.ValidationSeason <= 0 || options.TestSeason <= 0)
            throw new ArgumentException("Provide train, validation and test seasons.");
        if (options.TrainToSeason < options.TrainFromSeason)
            throw new ArgumentException("--train-to-season must be >= --train-from-season.");
        if (options.ValidationSeason >= options.TestSeason)
            throw new ArgumentException("--validation-season must be before --test-season.");
        if (options.ValidationSeason >= options.TrainFromSeason && options.ValidationSeason <= options.TrainToSeason)
            throw new ArgumentException("Validation season overlaps training seasons.");
        if (options.TestSeason >= options.TrainFromSeason && options.TestSeason <= options.TrainToSeason)
            throw new ArgumentException("Test season overlaps training seasons.");
        if (options.TestSeason == options.ValidationSeason)
            throw new ArgumentException("Test season overlaps validation season.");
        _ = AfterGoalEntryGateBuilder.NormalizeConflictPolicy(options.ConflictPolicy);
        _ = ParseCandidateClasses(options.CandidateClasses);
        string format = options.Format.Trim().ToLowerInvariant();
        if (format is not ("csv" or "json" or "both"))
            throw new ArgumentException("--format must be csv, json, or both.");
    }

    private static void ValidateSplit(IReadOnlyList<ModelV4BacktestEventRow> rows, ModelV4AfterGoalBacktestOptions options)
    {
        if (rows.Count == 0)
            throw new ArgumentException("After-goal events file has no rows.");
        List<string> leagueKeys = rows.Select(x => x.LeagueKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (leagueKeys.Count != 1)
            throw new ArgumentException($"Backtest expects one LeagueKey. Found: {string.Join(", ", leagueKeys)}.");
        var seasons = rows.Select(x => ParseSeason(x.Season)).ToHashSet();
        foreach (int season in Enumerable.Range(options.TrainFromSeason, options.TrainToSeason - options.TrainFromSeason + 1))
        {
            if (!seasons.Contains(season))
                throw new ArgumentException($"Training season {season} has no rows.");
        }
        if (!seasons.Contains(options.ValidationSeason))
            throw new ArgumentException($"Validation season {options.ValidationSeason} has no rows.");
        if (!seasons.Contains(options.TestSeason))
            throw new ArgumentException($"Test season {options.TestSeason} has no rows.");
    }

    private static void ValidateNoLeakage(ModelV4AfterGoalBacktestResult result, AfterGoalEntryGateResult gateResult, AfterGoalAngleAnalysisResult angleResult)
    {
        string testSeason = result.TestSeason.ToString(CultureInfo.InvariantCulture);
        if (angleResult.TrainSeasons.Contains(testSeason, StringComparer.OrdinalIgnoreCase) || angleResult.TestSeason == testSeason)
            result.LeakageWarnings.Add($"Angle artifacts include final test season {testSeason}.");
        if (gateResult.TrainSeasons.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(testSeason, StringComparer.OrdinalIgnoreCase) ||
            gateResult.TestSeason.Equals(testSeason, StringComparison.OrdinalIgnoreCase))
            result.LeakageWarnings.Add($"Entry gate artifacts include final test season {testSeason}.");
        if (result.BaselineFitSeasons.Contains(result.TestSeason))
            result.LeakageWarnings.Add($"Baseline fit seasons include final test season {testSeason}.");

        result.LeakageCheckPassed = result.LeakageWarnings.Count == 0;
        if (!result.LeakageCheckPassed)
            throw new ArgumentException($"Leakage check failed: {string.Join(" ", result.LeakageWarnings)}");
    }

    private static IEnumerable<ModelV4PerformanceSummaryRow> BuildPerformanceRows(IReadOnlyList<ModelV4BacktestEventDecision> rows)
    {
        var output = new List<ModelV4PerformanceSummaryRow>();
        void Add(string type, string name, IEnumerable<ModelV4BacktestEventDecision> subset, string direction = "", string notes = "")
            => output.Add(ModelV4PerformanceSummaryRow.From(type, name, direction, rows.Count, subset.ToList(), notes));

        Add("Overall", "All test events", rows);
        Add("Overall", "Candidate all", rows.Where(x => x.IsCandidate));
        Add("Overall", "Candidate strict only", rows.Where(x => x.IsStrictCandidate));
        Add("Overall", "WeakCandidate", rows.Where(x => x.IsWeakCandidate));
        Add("Overall", "Watchlist", rows.Where(x => x.IsWatchlist));
        Add("Overall", "Avoid", rows.Where(x => x.IsAvoid));
        Add("Overall", "NoSignal", rows.Where(x => x.IsNoSignal));
        Add("Direction", "Candidate OVER", rows.Where(x => x.IsCandidate && x.ModelDirection == "OVER"), "OVER");
        Add("Direction", "Candidate UNDER", rows.Where(x => x.IsCandidate && x.ModelDirection == "UNDER"), "UNDER");
        Add("Direction", "Strict Candidate OVER", rows.Where(x => x.IsStrictCandidate && x.ModelDirection == "OVER"), "OVER");
        Add("Direction", "Strict Candidate UNDER", rows.Where(x => x.IsStrictCandidate && x.ModelDirection == "UNDER"), "UNDER");
        Add("Direction", "Watchlist OVER", rows.Where(x => x.IsWatchlist && x.ModelDirection == "OVER"), "OVER");
        Add("Direction", "Watchlist UNDER", rows.Where(x => x.IsWatchlist && x.ModelDirection == "UNDER"), "UNDER");
        Add("Trigger", "AfterScoring signal rows", rows.Where(x => !string.IsNullOrWhiteSpace(x.ScoringTriggerDirection)));
        Add("Trigger", "AfterConceding signal rows", rows.Where(x => !string.IsNullOrWhiteSpace(x.ConcedingTriggerDirection)));
        Add("Trigger", "BothTriggersSameDirection", rows.Where(x => x.HasBothTriggersSameDirection));
        Add("Trigger", "TriggerConflict", rows.Where(x => x.ConflictResult == "ConflictNoBet"));
        Add("Trigger", "SingleTriggerOnly", rows.Where(x => x.HasSingleTriggerOnly));

        foreach (var group in rows.GroupBy(x => x.MinuteBand).OrderBy(x => x.Key)) Add("MinuteBand", group.Key, group);
        foreach (var group in rows.GroupBy(x => x.ScoreGapAfterBand).OrderBy(x => x.Key)) Add("ScoreGapAfterBand", group.Key, group);
        foreach (var group in rows.GroupBy(x => x.TotalGoalsAfterBand).OrderBy(x => x.Key)) Add("TotalGoalsAfterBand", group.Key, group);
        foreach (var group in rows.GroupBy(x => x.GameStateAfter).OrderBy(x => x.Key)) Add("GameStateAfter", group.Key, group);
        foreach (var group in rows.GroupBy(x => x.ScoringTeam).OrderBy(x => x.Key)) Add("ScoringTeam", group.Key, group);
        foreach (var group in rows.GroupBy(x => x.ConcedingTeam).OrderBy(x => x.Key)) Add("ConcedingTeam", group.Key, group);
        foreach (var group in rows.SelectMany(x => x.ModelSignalTeams.Select(team => new { Team = team, Row = x })).GroupBy(x => x.Team).OrderBy(x => x.Key))
            Add("ModelSignalTeam", group.Key, group.Select(x => x.Row));

        return output;
    }

    private static IEnumerable<ModelV4RulePerformanceRow> BuildRuleRows(IReadOnlyList<AfterGoalEntryRuleRow> rules, IReadOnlyList<ModelV4BacktestEventDecision> events)
    {
        var rows = new List<ModelV4RulePerformanceRow>();
        foreach (AfterGoalEntryRuleRow rule in rules)
        {
            List<ModelV4BacktestEventDecision> matched = events.Where(x => x.MatchesRule(rule)).ToList();
            List<ModelV4BacktestEventDecision> directional = matched.Where(x => x.RuleDirectionalResidual(rule.Direction).HasValue).ToList();
            rows.Add(new ModelV4RulePerformanceRow
            {
                LeagueKey = rule.LeagueKey,
                LeagueName = rule.LeagueName,
                Team = rule.Team,
                TriggerType = rule.TriggerType,
                SignalClass = rule.SignalClass,
                Direction = rule.Direction,
                EntryRuleStatus = rule.EntryRuleStatus,
                EntryRuleConfidence = rule.EntryRuleConfidence,
                TestEventCount = matched.Count,
                CandidateEventCount = matched.Count(x => x.IsCandidate && x.ModelDirection.Equals(rule.Direction, StringComparison.OrdinalIgnoreCase)),
                AvoidEventCount = matched.Count(x => x.IsAvoid),
                DirectionCorrectCount = directional.Count(x => x.RuleDirectionCorrect(rule.Direction) == true),
                DirectionHitRate = Rate(directional.Count(x => x.RuleDirectionCorrect(rule.Direction) == true), directional.Count),
                AvgDirectionalResidual = Avg(directional.Select(x => x.RuleDirectionalResidual(rule.Direction))),
                MedianDirectionalResidual = Median(directional.Select(x => x.RuleDirectionalResidual(rule.Direction))),
                SumDirectionalResidual = directional.Select(x => x.RuleDirectionalResidual(rule.Direction)).Where(x => x.HasValue).Sum(x => x!.Value),
                AvgResidualVsBaseline = Avg(matched.Select(x => (double?)x.ResidualVsBaseline)),
                Notes = rule.Reason
            });
        }

        return rows
            .OrderByDescending(x => x.CandidateEventCount)
            .ThenByDescending(x => x.AvgDirectionalResidual ?? double.NegativeInfinity)
            .ThenByDescending(x => x.DirectionHitRate ?? double.NegativeInfinity);
    }

    private static async Task WriteFrozenConfigAsync(string path, ModelV4AfterGoalBacktestOptions options, ModelV4AfterGoalBacktestResult result, CancellationToken cancellationToken)
    {
        var config = new
        {
            result.LeagueKey,
            result.TrainingSeasons,
            result.ValidationSeason,
            result.TestSeason,
            options.IncludeWatchlist,
            CandidateClasses = NormalizeCandidateClasses(options.CandidateClasses),
            result.RuleGenerationSeasons,
            result.BaselineFitSeasons,
            result.FinalTestOnlySeasons,
            result.LeakageCheckPassed,
            Timestamp = DateTimeOffset.UtcNow
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, cancellationToken);
    }

    private static string NormalizeCandidateClasses(string raw)
        => string.Join(";", ParseCandidateClasses(raw));

    private static HashSet<string> ParseCandidateClasses(string raw)
    {
        var allowed = new HashSet<string>(["Candidate", "WeakCandidate", "Watchlist"], StringComparer.OrdinalIgnoreCase);
        var parsed = (string.IsNullOrWhiteSpace(raw) ? "Candidate;WeakCandidate;Watchlist" : raw)
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (parsed.Count == 0 || parsed.Any(x => !allowed.Contains(x)))
            throw new ArgumentException("--candidate-classes may contain only Candidate, WeakCandidate, Watchlist.");
        return parsed;
    }

    internal static int ParseSeason(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int season)
            ? season
            : throw new ArgumentException($"Season '{value}' is not an integer.");

    internal static double? Avg(IEnumerable<double?> values)
    {
        List<double> list = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return list.Count == 0 ? null : list.Average();
    }

    internal static double? Median(IEnumerable<double?> values)
    {
        List<double> list = values.Where(x => x.HasValue).Select(x => x!.Value).OrderBy(x => x).ToList();
        if (list.Count == 0)
            return null;
        int middle = list.Count / 2;
        return list.Count % 2 == 1 ? list[middle] : (list[middle - 1] + list[middle]) / 2.0;
    }

    internal static double? Rate(int numerator, int denominator)
        => denominator == 0 ? null : (double)numerator / denominator;
}

public sealed class ModelV4BacktestEventDecision
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public string MatchDate { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public int GoalIndex { get; set; }
    public string GoalMinuteDisplay { get; set; } = string.Empty;
    public int GoalMinuteBase { get; set; }
    public int GoalStoppageMinutes { get; set; }
    public int GoalMinuteElapsed { get; set; }
    public string Period { get; set; } = string.Empty;
    public string ScoringTeam { get; set; } = string.Empty;
    public string ConcedingTeam { get; set; } = string.Empty;
    public bool IsHomeGoal { get; set; }
    public int ScoreAfterHome { get; set; }
    public int ScoreAfterAway { get; set; }
    public int TotalGoalsAfter { get; set; }
    public int ScoreGapAfter { get; set; }
    public string MinuteBand { get; set; } = string.Empty;
    public string ScoreGapAfterBand { get; set; } = string.Empty;
    public string TotalGoalsAfterBand { get; set; } = string.Empty;
    public string GameStateAfter { get; set; } = string.Empty;
    public int FinalHomeGoals { get; set; }
    public int FinalAwayGoals { get; set; }
    public int FinalTotalGoals { get; set; }
    public double RemainingGoalsAfterGoal { get; set; }
    public double BaselineExpectedRemainingGoals { get; set; }
    public double ResidualVsBaseline { get; set; }
    public string ModelDecision { get; set; } = string.Empty;
    public string ModelDirection { get; set; } = string.Empty;
    public string ModelDecisionClass { get; set; } = string.Empty;
    public bool MarketGateRequired { get; set; }
    public string ScoringTriggerStatus { get; set; } = string.Empty;
    public string ScoringTriggerDirection { get; set; } = string.Empty;
    public string ScoringTriggerSignalClass { get; set; } = string.Empty;
    public string ConcedingTriggerStatus { get; set; } = string.Empty;
    public string ConcedingTriggerDirection { get; set; } = string.Empty;
    public string ConcedingTriggerSignalClass { get; set; } = string.Empty;
    public string ConflictResult { get; set; } = string.Empty;
    public string MinuteGateStatus { get; set; } = string.Empty;
    public string ScoreGapGateStatus { get; set; } = string.Empty;
    public string TotalGoalsGateStatus { get; set; } = string.Empty;
    public string GameStateGateStatus { get; set; } = string.Empty;
    public string FinalReason { get; set; } = string.Empty;
    public bool? DirectionCorrect { get; set; }
    public double? DirectionalResidual { get; set; }
    public bool IsCandidate { get; set; }
    public bool IsStrictCandidate { get; set; }
    public bool IsWeakCandidate { get; set; }
    public bool IsWatchlist { get; set; }
    public bool IsAvoid { get; set; }
    public bool IsNoSignal { get; set; }

    public bool HasBothTriggersSameDirection => !string.IsNullOrWhiteSpace(ScoringTriggerDirection) && ScoringTriggerDirection.Equals(ConcedingTriggerDirection, StringComparison.OrdinalIgnoreCase);
    public bool HasSingleTriggerOnly => string.IsNullOrWhiteSpace(ScoringTriggerDirection) != string.IsNullOrWhiteSpace(ConcedingTriggerDirection);
    public IEnumerable<string> ModelSignalTeams
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ScoringTriggerDirection)) yield return ScoringTeam;
            if (!string.IsNullOrWhiteSpace(ConcedingTriggerDirection)) yield return ConcedingTeam;
        }
    }

    internal static ModelV4BacktestEventDecision From(ModelV4BacktestEventRow row, AfterGoalEntryEvaluationResult evaluation, BaselineExpectation baseline, HashSet<string> candidateClasses)
    {
        double residual = row.RemainingGoalsAfterGoal - baseline.ExpectedRemainingGoals;
        string rawDecision = evaluation.FinalDecision == "ConflictNoBet" ? "Avoid" : evaluation.FinalDecision;
        bool isCandidateClass = candidateClasses.Contains(rawDecision);
        var triggerForGates = PickGateTrigger(evaluation);
        string modelDirection = rawDecision is "Candidate" or "WeakCandidate" or "Watchlist" ? evaluation.Direction : string.Empty;
        double? directionalResidual = modelDirection == "OVER" ? residual :
            modelDirection == "UNDER" ? -residual : null;

        return new ModelV4BacktestEventDecision
        {
            LeagueKey = row.LeagueKey,
            LeagueName = row.LeagueName,
            Season = row.Season,
            MatchId = row.MatchId,
            MatchDate = row.MatchDate,
            HomeTeam = row.HomeTeam,
            AwayTeam = row.AwayTeam,
            GoalIndex = row.GoalIndex,
            GoalMinuteDisplay = row.GoalMinuteDisplay,
            GoalMinuteBase = row.GoalMinuteBase,
            GoalStoppageMinutes = row.GoalStoppageMinutes,
            GoalMinuteElapsed = row.GoalMinuteElapsed,
            Period = row.Period,
            ScoringTeam = row.ScoringTeam,
            ConcedingTeam = row.ConcedingTeam,
            IsHomeGoal = row.IsHomeGoal,
            ScoreAfterHome = row.ScoreAfterHome,
            ScoreAfterAway = row.ScoreAfterAway,
            TotalGoalsAfter = row.TotalGoalsAfter,
            ScoreGapAfter = row.ScoreGapAfter,
            MinuteBand = evaluation.State.MinuteBand,
            ScoreGapAfterBand = evaluation.State.ScoreGapAfterBand,
            TotalGoalsAfterBand = evaluation.State.TotalGoalsAfterBand,
            GameStateAfter = evaluation.State.GameStateAfter,
            FinalHomeGoals = row.FinalHomeGoals,
            FinalAwayGoals = row.FinalAwayGoals,
            FinalTotalGoals = row.FinalTotalGoals,
            RemainingGoalsAfterGoal = row.RemainingGoalsAfterGoal,
            BaselineExpectedRemainingGoals = baseline.ExpectedRemainingGoals,
            ResidualVsBaseline = residual,
            ModelDecision = rawDecision,
            ModelDirection = modelDirection,
            ModelDecisionClass = rawDecision,
            MarketGateRequired = rawDecision is "Candidate" or "WeakCandidate" or "Watchlist" || evaluation.MarketGateRequired,
            ScoringTriggerStatus = evaluation.ScoringTrigger?.TriggerDecision ?? "NoSignal",
            ScoringTriggerDirection = evaluation.ScoringTrigger?.Direction ?? string.Empty,
            ScoringTriggerSignalClass = evaluation.ScoringTrigger?.SignalClass ?? string.Empty,
            ConcedingTriggerStatus = evaluation.ConcedingTrigger?.TriggerDecision ?? "NoSignal",
            ConcedingTriggerDirection = evaluation.ConcedingTrigger?.Direction ?? string.Empty,
            ConcedingTriggerSignalClass = evaluation.ConcedingTrigger?.SignalClass ?? string.Empty,
            ConflictResult = evaluation.FinalDecision == "ConflictNoBet" ? "ConflictNoBet" :
                evaluation.ScoringTrigger is not null && evaluation.ConcedingTrigger is not null &&
                !evaluation.ScoringTrigger.Direction.Equals(evaluation.ConcedingTrigger.Direction, StringComparison.OrdinalIgnoreCase) ? "ResolvedConflict" : string.Empty,
            MinuteGateStatus = Gate(triggerForGates, "MinuteBand"),
            ScoreGapGateStatus = Gate(triggerForGates, "ScoreGapAfterBand"),
            TotalGoalsGateStatus = Gate(triggerForGates, "TotalGoalsAfterBand"),
            GameStateGateStatus = Gate(triggerForGates, "GameStateAfter"),
            FinalReason = evaluation.Reason,
            DirectionCorrect = modelDirection == "OVER" ? residual > 0 :
                modelDirection == "UNDER" ? residual < 0 : null,
            DirectionalResidual = directionalResidual,
            IsCandidate = isCandidateClass,
            IsStrictCandidate = rawDecision == "Candidate",
            IsWeakCandidate = rawDecision == "WeakCandidate",
            IsWatchlist = rawDecision == "Watchlist",
            IsAvoid = rawDecision == "Avoid",
            IsNoSignal = rawDecision == "NoSignal"
        };
    }

    public bool MatchesRule(AfterGoalEntryRuleRow rule)
        => rule.TriggerType == "AfterScoring"
            ? ScoringTeam.Equals(rule.Team, StringComparison.OrdinalIgnoreCase) && ScoringTriggerDirection.Equals(rule.Direction, StringComparison.OrdinalIgnoreCase)
            : ConcedingTeam.Equals(rule.Team, StringComparison.OrdinalIgnoreCase) && ConcedingTriggerDirection.Equals(rule.Direction, StringComparison.OrdinalIgnoreCase);

    public bool? RuleDirectionCorrect(string direction)
        => direction == "OVER" ? ResidualVsBaseline > 0 :
            direction == "UNDER" ? ResidualVsBaseline < 0 : null;

    public double? RuleDirectionalResidual(string direction)
        => direction == "OVER" ? ResidualVsBaseline :
            direction == "UNDER" ? -ResidualVsBaseline : null;

    private static AfterGoalTriggerEvaluation? PickGateTrigger(AfterGoalEntryEvaluationResult evaluation)
        => !string.IsNullOrWhiteSpace(evaluation.Direction) && evaluation.ScoringTrigger?.Direction == evaluation.Direction ? evaluation.ScoringTrigger :
           !string.IsNullOrWhiteSpace(evaluation.Direction) && evaluation.ConcedingTrigger?.Direction == evaluation.Direction ? evaluation.ConcedingTrigger :
           evaluation.ScoringTrigger ?? evaluation.ConcedingTrigger;

    private static string Gate(AfterGoalTriggerEvaluation? trigger, string dimension)
        => trigger?.GateChecks.FirstOrDefault(x => x.StateDimension.Equals(dimension, StringComparison.OrdinalIgnoreCase))?.GateStatus ?? string.Empty;
}

public sealed class ModelV4PerformanceSummaryRow
{
    public string GroupType { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public int EventCount { get; set; }
    public int CandidateCount { get; set; }
    public double CoveragePct { get; set; }
    public int DirectionCorrectCount { get; set; }
    public double? DirectionHitRate { get; set; }
    public double? AvgResidualVsBaseline { get; set; }
    public double? MedianResidualVsBaseline { get; set; }
    public double? AvgDirectionalResidual { get; set; }
    public double? MedianDirectionalResidual { get; set; }
    public double? SumDirectionalResidual { get; set; }
    public double? AvgRemainingGoalsAfterGoal { get; set; }
    public double? AvgBaselineExpectedRemainingGoals { get; set; }
    public string Notes { get; set; } = string.Empty;

    public static ModelV4PerformanceSummaryRow From(string type, string name, string direction, int totalEvents, IReadOnlyList<ModelV4BacktestEventDecision> rows, string notes)
    {
        var directional = rows.Where(x => x.DirectionalResidual.HasValue).ToList();
        return new ModelV4PerformanceSummaryRow
        {
            GroupType = type,
            GroupName = name,
            Direction = direction,
            EventCount = rows.Count,
            CandidateCount = rows.Count(x => x.IsCandidate),
            CoveragePct = totalEvents == 0 ? 0 : (double)rows.Count / totalEvents,
            DirectionCorrectCount = directional.Count(x => x.DirectionCorrect == true),
            DirectionHitRate = ModelV4AfterGoalBacktestRunner.Rate(directional.Count(x => x.DirectionCorrect == true), directional.Count),
            AvgResidualVsBaseline = ModelV4AfterGoalBacktestRunner.Avg(rows.Select(x => (double?)x.ResidualVsBaseline)),
            MedianResidualVsBaseline = ModelV4AfterGoalBacktestRunner.Median(rows.Select(x => (double?)x.ResidualVsBaseline)),
            AvgDirectionalResidual = ModelV4AfterGoalBacktestRunner.Avg(directional.Select(x => x.DirectionalResidual)),
            MedianDirectionalResidual = ModelV4AfterGoalBacktestRunner.Median(directional.Select(x => x.DirectionalResidual)),
            SumDirectionalResidual = directional.Count == 0 ? null : directional.Sum(x => x.DirectionalResidual!.Value),
            AvgRemainingGoalsAfterGoal = ModelV4AfterGoalBacktestRunner.Avg(rows.Select(x => (double?)x.RemainingGoalsAfterGoal)),
            AvgBaselineExpectedRemainingGoals = ModelV4AfterGoalBacktestRunner.Avg(rows.Select(x => (double?)x.BaselineExpectedRemainingGoals)),
            Notes = notes
        };
    }
}

public sealed class ModelV4RulePerformanceRow
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public string SignalClass { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string EntryRuleStatus { get; set; } = string.Empty;
    public string EntryRuleConfidence { get; set; } = string.Empty;
    public int TestEventCount { get; set; }
    public int CandidateEventCount { get; set; }
    public int AvoidEventCount { get; set; }
    public int DirectionCorrectCount { get; set; }
    public double? DirectionHitRate { get; set; }
    public double? AvgDirectionalResidual { get; set; }
    public double? MedianDirectionalResidual { get; set; }
    public double? SumDirectionalResidual { get; set; }
    public double? AvgResidualVsBaseline { get; set; }
    public string Notes { get; set; } = string.Empty;
}

internal sealed class ModelV4BacktestEventRow
{
    public string LeagueKey { get; init; } = string.Empty;
    public string LeagueName { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string MatchId { get; init; } = string.Empty;
    public string MatchDate { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public int GoalIndex { get; init; }
    public string GoalMinuteDisplay { get; init; } = string.Empty;
    public int GoalMinuteBase { get; init; }
    public int GoalStoppageMinutes { get; init; }
    public int GoalMinuteElapsed { get; init; }
    public string Period { get; init; } = string.Empty;
    public string ScoringTeam { get; init; } = string.Empty;
    public string ConcedingTeam { get; init; } = string.Empty;
    public bool IsHomeGoal { get; init; }
    public int ScoreAfterHome { get; init; }
    public int ScoreAfterAway { get; init; }
    public int TotalGoalsAfter { get; init; }
    public int ScoreGapAfter { get; init; }
    public int HomeLeadAfter { get; init; }
    public int AwayLeadAfter { get; init; }
    public bool IsEqualAfter { get; init; }
    public double RemainingGoalsAfterGoal { get; init; }
    public int FinalHomeGoals { get; init; }
    public int FinalAwayGoals { get; init; }
    public int FinalTotalGoals { get; init; }
    public string MinutesToNextGoal { get; init; } = string.Empty;

    public AfterGoalEventCsvRow ToAngleRow() => new()
    {
        LeagueKey = LeagueKey,
        LeagueName = LeagueName,
        Season = Season,
        MatchId = MatchId,
        HomeTeam = HomeTeam,
        AwayTeam = AwayTeam,
        GoalIndex = GoalIndex,
        GoalMinuteBase = GoalMinuteBase,
        GoalStoppageMinutes = GoalStoppageMinutes,
        GoalMinuteElapsed = GoalMinuteElapsed,
        Period = Period,
        ScoringTeam = ScoringTeam,
        ConcedingTeam = ConcedingTeam,
        TotalGoalsAfter = TotalGoalsAfter,
        ScoreGapAfter = ScoreGapAfter,
        HomeLeadAfter = HomeLeadAfter,
        AwayLeadAfter = AwayLeadAfter,
        IsEqualAfter = IsEqualAfter,
        RemainingGoalsAfterGoal = RemainingGoalsAfterGoal,
        MinutesToNextGoal = MinutesToNextGoal
    };
}

internal static class ModelV4EventCsv
{
    public static async Task<List<ModelV4BacktestEventRow>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Path.GetFullPath(path), Encoding.UTF8, true);
        string? headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new ArgumentException($"Input file is empty: {path}");
        List<string> headers = CsvUtility.ParseLine(headerLine);
        var index = headers.Select((name, i) => new { name, i }).ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);
        string[] required =
        [
            "LeagueKey", "LeagueName", "Season", "MatchId", "MatchDate", "HomeTeam", "AwayTeam", "GoalIndex",
            "GoalMinuteDisplay", "GoalMinuteBase", "GoalStoppageMinutes", "GoalMinuteElapsed", "Period",
            "ScoringTeam", "ConcedingTeam", "IsHomeGoal", "ScoreAfterHome", "ScoreAfterAway", "TotalGoalsAfter",
            "ScoreGapAfter", "HomeLeadAfter", "AwayLeadAfter", "IsEqualAfter", "RemainingGoalsAfterGoal",
            "MinutesToNextGoal", "FinalHomeGoals", "FinalAwayGoals", "FinalTotalGoals"
        ];
        List<string> missing = required.Where(x => !index.ContainsKey(x)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"Input file is missing required columns: {string.Join(", ", missing)}");

        var rows = new List<ModelV4BacktestEventRow>();
        while (!reader.EndOfStream)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;
            List<string> values = CsvUtility.ParseLine(line);
            rows.Add(new ModelV4BacktestEventRow
            {
                LeagueKey = Get(values, index, "LeagueKey"),
                LeagueName = Get(values, index, "LeagueName"),
                Season = Get(values, index, "Season"),
                MatchId = Get(values, index, "MatchId"),
                MatchDate = Get(values, index, "MatchDate"),
                HomeTeam = Get(values, index, "HomeTeam"),
                AwayTeam = Get(values, index, "AwayTeam"),
                GoalIndex = Int(values, index, "GoalIndex"),
                GoalMinuteDisplay = Get(values, index, "GoalMinuteDisplay"),
                GoalMinuteBase = Int(values, index, "GoalMinuteBase"),
                GoalStoppageMinutes = Int(values, index, "GoalStoppageMinutes"),
                GoalMinuteElapsed = Int(values, index, "GoalMinuteElapsed"),
                Period = Get(values, index, "Period"),
                ScoringTeam = Get(values, index, "ScoringTeam"),
                ConcedingTeam = Get(values, index, "ConcedingTeam"),
                IsHomeGoal = Bool(values, index, "IsHomeGoal"),
                ScoreAfterHome = Int(values, index, "ScoreAfterHome"),
                ScoreAfterAway = Int(values, index, "ScoreAfterAway"),
                TotalGoalsAfter = Int(values, index, "TotalGoalsAfter"),
                ScoreGapAfter = Int(values, index, "ScoreGapAfter"),
                HomeLeadAfter = Int(values, index, "HomeLeadAfter"),
                AwayLeadAfter = Int(values, index, "AwayLeadAfter"),
                IsEqualAfter = Bool(values, index, "IsEqualAfter"),
                RemainingGoalsAfterGoal = Double(values, index, "RemainingGoalsAfterGoal"),
                MinutesToNextGoal = Get(values, index, "MinutesToNextGoal"),
                FinalHomeGoals = Int(values, index, "FinalHomeGoals"),
                FinalAwayGoals = Int(values, index, "FinalAwayGoals"),
                FinalTotalGoals = Int(values, index, "FinalTotalGoals")
            });
        }

        return rows;
    }

    public static async Task WriteFilteredAsync(string inputPath, string outputPath, HashSet<string> seasons, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? Directory.GetCurrentDirectory());
        using var reader = new StreamReader(Path.GetFullPath(inputPath), Encoding.UTF8, true);
        await using var writer = new StreamWriter(Path.GetFullPath(outputPath), false, new UTF8Encoding(false));
        string? headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new ArgumentException($"Input file is empty: {inputPath}");
        await writer.WriteLineAsync(headerLine);
        List<string> headers = CsvUtility.ParseLine(headerLine);
        int seasonIndex = headers.FindIndex(x => x.Equals("Season", StringComparison.OrdinalIgnoreCase));
        if (seasonIndex < 0)
            throw new ArgumentException("Input file is missing Season column.");
        while (!reader.EndOfStream)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;
            List<string> values = CsvUtility.ParseLine(line);
            string season = seasonIndex < values.Count ? values[seasonIndex] : string.Empty;
            if (seasons.Contains(season))
                await writer.WriteLineAsync(line);
        }
    }

    private static string Get(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> index, string name)
        => index[name] < values.Count ? values[index[name]] : string.Empty;
    private static int Int(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> index, string name)
        => int.TryParse(Get(values, index, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : throw new ArgumentException($"{name} must be integer.");
    private static double Double(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> index, string name)
        => double.TryParse(Get(values, index, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : throw new ArgumentException($"{name} must be numeric.");
    private static bool Bool(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> index, string name)
        => bool.TryParse(Get(values, index, name), out bool value) ? value : throw new ArgumentException($"{name} must be boolean.");
}

public static class ModelV4BacktestReportWriter
{
    public static async Task WriteAsync(ModelV4AfterGoalBacktestResult result, string format, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(result.OutputDir);
        string normalized = format.Trim().ToLowerInvariant();
        if (normalized is "csv" or "both")
        {
            await WriteEventDecisionsAsync(result.TestEventDecisionsFile, result.EventDecisions, cancellationToken);
            await WritePerformanceSummaryAsync(result.PerformanceSummaryFile, result.PerformanceRows, cancellationToken);
            await WriteRulePerformanceAsync(result.RulePerformanceFile, result.RulePerformanceRows, cancellationToken);
        }
        string summaryPath = Path.Combine(result.OutputDir, "model-v4-backtest-summary.json");
        if (normalized is "json" or "both" or "csv")
            await File.WriteAllTextAsync(summaryPath, SummaryJson(result), Encoding.UTF8, cancellationToken);
    }

    private static async Task WriteEventDecisionsAsync(string path, IReadOnlyList<ModelV4BacktestEventDecision> rows, CancellationToken cancellationToken)
    {
        string[] headers =
        [
            "LeagueKey","LeagueName","Season","MatchId","MatchDate","HomeTeam","AwayTeam","GoalIndex","GoalMinuteDisplay","GoalMinuteBase","GoalStoppageMinutes","GoalMinuteElapsed","Period","ScoringTeam","ConcedingTeam","IsHomeGoal","ScoreAfterHome","ScoreAfterAway","TotalGoalsAfter","ScoreGapAfter","MinuteBand","ScoreGapAfterBand","TotalGoalsAfterBand","GameStateAfter","FinalHomeGoals","FinalAwayGoals","FinalTotalGoals","RemainingGoalsAfterGoal","BaselineExpectedRemainingGoals","ResidualVsBaseline","ModelDecision","ModelDirection","ModelDecisionClass","MarketGateRequired","ScoringTriggerStatus","ScoringTriggerDirection","ScoringTriggerSignalClass","ConcedingTriggerStatus","ConcedingTriggerDirection","ConcedingTriggerSignalClass","ConflictResult","MinuteGateStatus","ScoreGapGateStatus","TotalGoalsGateStatus","GameStateGateStatus","FinalReason","DirectionCorrect","DirectionalResidual","IsCandidate","IsStrictCandidate","IsWeakCandidate","IsWatchlist","IsAvoid","IsNoSignal"
        ];
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync(string.Join(",", headers));
        foreach (ModelV4BacktestEventDecision row in rows)
            await writer.WriteLineAsync(CsvUtility.ToLine(EventValues(row)));
    }

    private static async Task WritePerformanceSummaryAsync(string path, IReadOnlyList<ModelV4PerformanceSummaryRow> rows, CancellationToken cancellationToken)
    {
        string[] headers =
        [
            "GroupType","GroupName","Direction","EventCount","CandidateCount","CoveragePct","DirectionCorrectCount","DirectionHitRate","AvgResidualVsBaseline","MedianResidualVsBaseline","AvgDirectionalResidual","MedianDirectionalResidual","SumDirectionalResidual","AvgRemainingGoalsAfterGoal","AvgBaselineExpectedRemainingGoals","Notes"
        ];
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync(string.Join(",", headers));
        foreach (ModelV4PerformanceSummaryRow row in rows)
            await writer.WriteLineAsync(CsvUtility.ToLine(SummaryValues(row)));
    }

    private static async Task WriteRulePerformanceAsync(string path, IReadOnlyList<ModelV4RulePerformanceRow> rows, CancellationToken cancellationToken)
    {
        string[] headers =
        [
            "LeagueKey","LeagueName","Team","TriggerType","SignalClass","Direction","EntryRuleStatus","EntryRuleConfidence","TestEventCount","CandidateEventCount","AvoidEventCount","DirectionCorrectCount","DirectionHitRate","AvgDirectionalResidual","MedianDirectionalResidual","SumDirectionalResidual","AvgResidualVsBaseline","Notes"
        ];
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        await writer.WriteLineAsync(string.Join(",", headers));
        foreach (ModelV4RulePerformanceRow row in rows)
            await writer.WriteLineAsync(CsvUtility.ToLine(RuleValues(row)));
    }

    private static IEnumerable<string> EventValues(ModelV4BacktestEventDecision row)
    {
        yield return row.LeagueKey; yield return row.LeagueName; yield return row.Season; yield return row.MatchId; yield return row.MatchDate; yield return row.HomeTeam; yield return row.AwayTeam; yield return I(row.GoalIndex); yield return row.GoalMinuteDisplay; yield return I(row.GoalMinuteBase); yield return I(row.GoalStoppageMinutes); yield return I(row.GoalMinuteElapsed); yield return row.Period; yield return row.ScoringTeam; yield return row.ConcedingTeam; yield return B(row.IsHomeGoal); yield return I(row.ScoreAfterHome); yield return I(row.ScoreAfterAway); yield return I(row.TotalGoalsAfter); yield return I(row.ScoreGapAfter); yield return row.MinuteBand; yield return row.ScoreGapAfterBand; yield return row.TotalGoalsAfterBand; yield return row.GameStateAfter; yield return I(row.FinalHomeGoals); yield return I(row.FinalAwayGoals); yield return I(row.FinalTotalGoals); yield return F(row.RemainingGoalsAfterGoal); yield return F(row.BaselineExpectedRemainingGoals); yield return F(row.ResidualVsBaseline); yield return row.ModelDecision; yield return row.ModelDirection; yield return row.ModelDecisionClass; yield return B(row.MarketGateRequired); yield return row.ScoringTriggerStatus; yield return row.ScoringTriggerDirection; yield return row.ScoringTriggerSignalClass; yield return row.ConcedingTriggerStatus; yield return row.ConcedingTriggerDirection; yield return row.ConcedingTriggerSignalClass; yield return row.ConflictResult; yield return row.MinuteGateStatus; yield return row.ScoreGapGateStatus; yield return row.TotalGoalsGateStatus; yield return row.GameStateGateStatus; yield return row.FinalReason; yield return BN(row.DirectionCorrect); yield return F(row.DirectionalResidual); yield return B(row.IsCandidate); yield return B(row.IsStrictCandidate); yield return B(row.IsWeakCandidate); yield return B(row.IsWatchlist); yield return B(row.IsAvoid); yield return B(row.IsNoSignal);
    }

    private static IEnumerable<string> SummaryValues(ModelV4PerformanceSummaryRow row)
    {
        yield return row.GroupType; yield return row.GroupName; yield return row.Direction; yield return I(row.EventCount); yield return I(row.CandidateCount); yield return F(row.CoveragePct); yield return I(row.DirectionCorrectCount); yield return F(row.DirectionHitRate); yield return F(row.AvgResidualVsBaseline); yield return F(row.MedianResidualVsBaseline); yield return F(row.AvgDirectionalResidual); yield return F(row.MedianDirectionalResidual); yield return F(row.SumDirectionalResidual); yield return F(row.AvgRemainingGoalsAfterGoal); yield return F(row.AvgBaselineExpectedRemainingGoals); yield return row.Notes;
    }

    private static IEnumerable<string> RuleValues(ModelV4RulePerformanceRow row)
    {
        yield return row.LeagueKey; yield return row.LeagueName; yield return row.Team; yield return row.TriggerType; yield return row.SignalClass; yield return row.Direction; yield return row.EntryRuleStatus; yield return row.EntryRuleConfidence; yield return I(row.TestEventCount); yield return I(row.CandidateEventCount); yield return I(row.AvoidEventCount); yield return I(row.DirectionCorrectCount); yield return F(row.DirectionHitRate); yield return F(row.AvgDirectionalResidual); yield return F(row.MedianDirectionalResidual); yield return F(row.SumDirectionalResidual); yield return F(row.AvgResidualVsBaseline); yield return row.Notes;
    }

    private static string SummaryJson(ModelV4AfterGoalBacktestResult result)
    {
        var summary = new
        {
            result.LeagueKey,
            result.LeagueName,
            result.InputEventsPath,
            result.WorkDir,
            result.OutputDir,
            result.TrainingSeasons,
            result.ValidationSeason,
            result.TestSeason,
            result.IncludeWatchlist,
            result.CandidateClasses,
            result.TotalEventsRead,
            result.TrainingRows,
            result.ValidationRows,
            result.TestRows,
            result.FrozenAnglesDir,
            result.FrozenProfilesDir,
            result.FrozenEntryGatesDir,
            result.TestEventDecisionsFile,
            result.PerformanceSummaryFile,
            result.RulePerformanceFile,
            result.CandidateCount,
            result.StrictCandidateCount,
            result.WeakCandidateCount,
            result.WatchlistCount,
            result.AvoidCount,
            result.NoSignalCount,
            CandidateCoveragePct = Math.Round(result.CandidateCoveragePct, 4),
            CandidateDirectionHitRate = Round(result.CandidateDirectionHitRate),
            CandidateAvgDirectionalResidual = Round(result.CandidateAvgDirectionalResidual),
            CandidateMedianDirectionalResidual = Round(result.CandidateMedianDirectionalResidual),
            result.LeakageCheckPassed,
            result.RuleGenerationSeasons,
            result.BaselineFitSeasons,
            result.FinalTestOnlySeasons,
            result.LeakageWarnings,
            result.Warnings,
            Timestamp = DateTimeOffset.UtcNow
        };
        return JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string B(bool value) => value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
    private static string BN(bool? value) => value.HasValue ? B(value.Value) : string.Empty;
    private static string F(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string F(double? value) => value.HasValue ? F(value.Value) : string.Empty;
    private static double? Round(double? value) => value.HasValue ? Math.Round(value.Value, 4) : null;
}
