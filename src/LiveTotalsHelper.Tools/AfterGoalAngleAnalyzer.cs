using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LiveTotalsHelper.Tools;

public sealed class AfterGoalAngleAnalysisOptions
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public string TrainFromSeason { get; set; } = string.Empty;
    public string TrainToSeason { get; set; } = string.Empty;
    public string TestSeason { get; set; } = string.Empty;
    public int MinSample { get; set; } = 30;
    public int StrongSample { get; set; } = 80;
    public double ShrinkK { get; set; } = 50;
    public bool IncludeOpponentPairs { get; set; }
    public string RawCommandLine { get; set; } = string.Empty;
}

public sealed class AfterGoalAngleAnalysisResult
{
    public int TotalRowsRead { get; set; }
    public int RowsUsed { get; set; }
    public int TrainRows { get; set; }
    public int TestRows { get; set; }
    public int IgnoredRows { get; set; }
    public List<string> LeagueKeys { get; } = [];
    public List<string> InputSeasons { get; } = [];
    public List<string> TrainSeasons { get; } = [];
    public List<string> IgnoredSeasons { get; } = [];
    public string TestSeason { get; set; } = string.Empty;
    public string SplitMode { get; set; } = string.Empty;
    public List<string> Warnings { get; } = [];
    public Dictionary<string, int> ReportRowCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AfterGoalAngleReportRow> LeagueRows { get; } = [];
    public List<AfterGoalAngleReportRow> LeagueMinuteRows { get; } = [];
    public List<AfterGoalAngleReportRow> TeamScoringRows { get; } = [];
    public List<AfterGoalAngleReportRow> TeamConcedingRows { get; } = [];
    public List<AfterGoalAngleReportRow> TeamMinuteScoringRows { get; } = [];
    public List<AfterGoalAngleReportRow> TeamMinuteConcedingRows { get; } = [];
    public List<AfterGoalAngleReportRow> OpponentPairRows { get; } = [];
}

public sealed class AfterGoalEventCsvRow
{
    public string LeagueKey { get; init; } = string.Empty;
    public string LeagueName { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string MatchId { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public int GoalIndex { get; init; }
    public int GoalMinuteBase { get; init; }
    public int GoalStoppageMinutes { get; init; }
    public int GoalMinuteElapsed { get; init; }
    public string Period { get; init; } = string.Empty;
    public string ScoringTeam { get; init; } = string.Empty;
    public string ConcedingTeam { get; init; } = string.Empty;
    public int TotalGoalsAfter { get; init; }
    public int ScoreGapAfter { get; init; }
    public int HomeLeadAfter { get; init; }
    public int AwayLeadAfter { get; init; }
    public bool IsEqualAfter { get; init; }
    public double RemainingGoalsAfterGoal { get; init; }
    public string MinutesToNextGoal { get; init; } = string.Empty;

    public string MinuteBand => AfterGoalStateBucketer.MinuteBand(this);
    public string Half => AfterGoalStateBucketer.Half(this);
    public string TotalGoalsAfterBand => AfterGoalStateBucketer.TotalGoalsAfterBand(TotalGoalsAfter);
    public string ScoreGapAfterBand => AfterGoalStateBucketer.ScoreGapAfterBand(ScoreGapAfter);
    public string GameStateAfter => AfterGoalStateBucketer.GameStateAfter(this);
}

public sealed class AfterGoalAngleReportRow
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string MinuteBand { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string ScoringTeam { get; set; } = string.Empty;
    public string ConcedingTeam { get; set; } = string.Empty;
    public string TrainSeasons { get; set; } = string.Empty;
    public string TestSeason { get; set; } = string.Empty;
    public int TrainSampleSize { get; set; }
    public double TrainAvgRemainingGoalsAfterGoal { get; set; }
    public double TrainAvgBaselineExpectedRemainingGoals { get; set; }
    public int TrainBaselineSampleSizeUsed { get; set; }
    public double TrainRawResidual { get; set; }
    public double? TrainRawLiftPct { get; set; }
    public double ShrinkWeight { get; set; }
    public double TrainShrunkResidual { get; set; }
    public double TrainShrunkExpectedRemainingGoals { get; set; }
    public string Direction { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public int TestSampleSize { get; set; }
    public double? TestAvgRemainingGoalsAfterGoal { get; set; }
    public double? TestAvgBaselineExpectedRemainingGoals { get; set; }
    public double? TestAvgResidualVsBaseline { get; set; }
    public double? TestAvgResidualVsAngle { get; set; }
    public bool? TestDirectionConfirmed { get; set; }
}

internal sealed record BaselineExpectation(double ExpectedRemainingGoals, int SampleSize, string Level);

internal static class AfterGoalStateBucketer
{
    public static string MinuteBand(AfterGoalEventCsvRow row)
    {
        if (row.Period.Equals("1H", StringComparison.OrdinalIgnoreCase))
        {
            if (row.GoalMinuteBase <= 15) return "00-15";
            if (row.GoalMinuteBase <= 30) return "16-30";
            return "31-45+";
        }

        if (row.GoalMinuteBase <= 60) return "46-60";
        if (row.GoalMinuteBase <= 75) return "61-75";
        return "76-90+";
    }

    public static string Half(AfterGoalEventCsvRow row)
        => row.Period.Equals("1H", StringComparison.OrdinalIgnoreCase) ? "1H" : "2H";

    public static string TotalGoalsAfterBand(int totalGoalsAfter)
        => totalGoalsAfter >= 5 ? "5+" : Math.Max(1, totalGoalsAfter).ToString(CultureInfo.InvariantCulture);

    public static string ScoreGapAfterBand(int scoreGapAfter)
        => scoreGapAfter <= 0 ? "Draw" :
            scoreGapAfter == 1 ? "Lead1" :
            scoreGapAfter == 2 ? "Lead2" : "Lead3Plus";

    public static string GameStateAfter(AfterGoalEventCsvRow row)
        => row.IsEqualAfter ? "EqualAfter" : row.HomeLeadAfter > 0 ? "HomeLeadAfter" : "AwayLeadAfter";
}

internal sealed class AfterGoalBaselineModel
{
    private readonly Dictionary<string, BaselineBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _hasMultipleLeagues;
    private readonly int _minSample;

    public AfterGoalBaselineModel(IEnumerable<AfterGoalEventCsvRow> trainRows, bool hasMultipleLeagues, int minSample)
    {
        _hasMultipleLeagues = hasMultipleLeagues;
        _minSample = minSample;

        foreach (AfterGoalEventCsvRow row in trainRows)
        {
            Add(Key("primary", row.LeagueKey, row.MinuteBand, row.TotalGoalsAfterBand, row.ScoreGapAfterBand, row.Half), row.RemainingGoalsAfterGoal);
            Add(Key("fallback1", row.LeagueKey, row.MinuteBand, row.TotalGoalsAfterBand, row.Half), row.RemainingGoalsAfterGoal);
            Add(Key("fallback2", row.LeagueKey, row.MinuteBand, row.Half), row.RemainingGoalsAfterGoal);
            Add(Key("fallback3", row.LeagueKey, row.Half), row.RemainingGoalsAfterGoal);
            Add(Key("fallback4", row.LeagueKey), row.RemainingGoalsAfterGoal);
            Add(Key("global"), row.RemainingGoalsAfterGoal);
        }
    }

    public BaselineExpectation Expect(AfterGoalEventCsvRow row)
    {
        var candidates = new List<(string Level, string Key)>
        {
            ("Primary", Key("primary", row.LeagueKey, row.MinuteBand, row.TotalGoalsAfterBand, row.ScoreGapAfterBand, row.Half)),
            ("LeagueMinuteTotalHalf", Key("fallback1", row.LeagueKey, row.MinuteBand, row.TotalGoalsAfterBand, row.Half)),
            ("LeagueMinuteHalf", Key("fallback2", row.LeagueKey, row.MinuteBand, row.Half)),
            ("LeagueHalf", Key("fallback3", row.LeagueKey, row.Half)),
            ("League", Key("fallback4", row.LeagueKey))
        };

        if (_hasMultipleLeagues)
            candidates.Add(("Global", Key("global")));

        foreach ((string level, string key) in candidates)
        {
            if (_buckets.TryGetValue(key, out BaselineBucket? bucket) && bucket.Count >= _minSample)
                return new BaselineExpectation(bucket.Average, bucket.Count, level);
        }

        foreach ((string level, string key) in candidates)
        {
            if (_buckets.TryGetValue(key, out BaselineBucket? bucket) && bucket.Count > 0)
                return new BaselineExpectation(bucket.Average, bucket.Count, level);
        }

        return new BaselineExpectation(0, 0, "None");
    }

    private void Add(string key, double value)
    {
        if (!_buckets.TryGetValue(key, out BaselineBucket? bucket))
        {
            bucket = new BaselineBucket();
            _buckets[key] = bucket;
        }

        bucket.Count++;
        bucket.Sum += value;
    }

    private static string Key(params string[] parts)
        => string.Join("|", parts.Select(x => x ?? string.Empty));

    private sealed class BaselineBucket
    {
        public int Count { get; set; }
        public double Sum { get; set; }
        public double Average => Count == 0 ? 0 : Sum / Count;
    }
}

public sealed class AfterGoalAngleAnalyzer
{
    private static readonly string[] RequiredColumns =
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

    public async Task<AfterGoalAngleAnalysisResult> AnalyzeAsync(AfterGoalAngleAnalysisOptions options, CancellationToken cancellationToken)
    {
        List<AfterGoalEventCsvRow> rows = await ReadRowsAsync(options.InputPath, cancellationToken);
        if (rows.Count == 0)
            throw new ArgumentException($"Input file has no data rows: {options.InputPath}");

        var result = new AfterGoalAngleAnalysisResult
        {
            TotalRowsRead = rows.Count
        };

        ValidateInputRows(rows, result);

        SplitSeasons(rows, options, result);
        if (result.TrainSeasons.Count == 0 || string.IsNullOrWhiteSpace(result.TestSeason))
            throw new ArgumentException("Could not infer train/test split. Provide --train-from-season/--train-to-season and --test-season, or provide none to use default latest-season test split.");

        if (result.TrainSeasons.Contains(result.TestSeason, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Test season {result.TestSeason} is also in train seasons. Refusing in-sample angle fitting.");

        List<AfterGoalEventCsvRow> trainRows = rows.Where(x => result.TrainSeasons.Contains(x.Season, StringComparer.OrdinalIgnoreCase)).ToList();
        List<AfterGoalEventCsvRow> testRows = rows.Where(x => x.Season.Equals(result.TestSeason, StringComparison.OrdinalIgnoreCase)).ToList();
        if (trainRows.Count == 0)
            throw new ArgumentException("Train split has no rows. Check --train-from-season/--train-to-season and the seasons present in the input CSV.");
        if (testRows.Count == 0)
            throw new ArgumentException($"Test season {result.TestSeason} has no rows. Check --test-season and the seasons present in the input CSV.");

        result.RowsUsed = trainRows.Count + testRows.Count;
        result.TrainRows = trainRows.Count;
        result.TestRows = testRows.Count;
        result.IgnoredRows = result.TotalRowsRead - result.RowsUsed;
        result.LeagueKeys.AddRange(rows.Select(x => x.LeagueKey).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));

        bool hasMultipleLeagues = result.LeagueKeys.Count > 1;
        var baselineModel = new AfterGoalBaselineModel(trainRows, hasMultipleLeagues, options.MinSample);

        result.LeagueRows.AddRange(BuildReport(
            "league-after-goal-angles.csv",
            trainRows,
            testRows,
            baselineModel,
            options,
            result,
            row => new AngleGroupKey(row.LeagueKey, row.LeagueName)));

        result.LeagueMinuteRows.AddRange(BuildReport(
            "league-minute-after-goal-angles.csv",
            trainRows,
            testRows,
            baselineModel,
            options,
            result,
            row => new AngleGroupKey(row.LeagueKey, row.LeagueName, MinuteBand: row.MinuteBand)));

        result.TeamScoringRows.AddRange(BuildReport(
            "team-after-scoring-angles.csv",
            trainRows,
            testRows,
            baselineModel,
            options,
            result,
            row => new AngleGroupKey(row.LeagueKey, row.LeagueName, Team: row.ScoringTeam)));

        result.TeamConcedingRows.AddRange(BuildReport(
            "team-after-conceding-angles.csv",
            trainRows,
            testRows,
            baselineModel,
            options,
            result,
            row => new AngleGroupKey(row.LeagueKey, row.LeagueName, Team: row.ConcedingTeam)));

        result.TeamMinuteScoringRows.AddRange(BuildReport(
            "team-minute-after-scoring-angles.csv",
            trainRows,
            testRows,
            baselineModel,
            options,
            result,
            row => new AngleGroupKey(row.LeagueKey, row.LeagueName, MinuteBand: row.MinuteBand, Team: row.ScoringTeam)));

        result.TeamMinuteConcedingRows.AddRange(BuildReport(
            "team-minute-after-conceding-angles.csv",
            trainRows,
            testRows,
            baselineModel,
            options,
            result,
            row => new AngleGroupKey(row.LeagueKey, row.LeagueName, MinuteBand: row.MinuteBand, Team: row.ConcedingTeam)));

        if (options.IncludeOpponentPairs)
        {
            result.OpponentPairRows.AddRange(BuildReport(
                "opponent-pair-after-goal-angles.csv",
                trainRows,
                testRows,
                baselineModel,
                options,
                result,
                row => new AngleGroupKey(row.LeagueKey, row.LeagueName, ScoringTeam: row.ScoringTeam, ConcedingTeam: row.ConcedingTeam)));
        }

        return result;
    }

    private static List<AfterGoalAngleReportRow> BuildReport(
        string reportName,
        IReadOnlyList<AfterGoalEventCsvRow> trainRows,
        IReadOnlyList<AfterGoalEventCsvRow> testRows,
        AfterGoalBaselineModel baselineModel,
        AfterGoalAngleAnalysisOptions options,
        AfterGoalAngleAnalysisResult result,
        Func<AfterGoalEventCsvRow, AngleGroupKey> groupSelector)
    {
        Dictionary<AngleGroupKey, List<ScoredTrainEvent>> trainGroups = trainRows
            .Select(row => new ScoredTrainEvent(row, baselineModel.Expect(row), groupSelector(row)))
            .GroupBy(x => x.GroupKey)
            .ToDictionary(x => x.Key, x => x.ToList());

        Dictionary<AngleGroupKey, List<ScoredTestEvent>> testGroups = testRows
            .Select(row => new ScoredTestEvent(row, baselineModel.Expect(row), groupSelector(row)))
            .GroupBy(x => x.GroupKey)
            .ToDictionary(x => x.Key, x => x.ToList());

        var reportRows = new List<AfterGoalAngleReportRow>();
        foreach ((AngleGroupKey key, List<ScoredTrainEvent> train) in trainGroups)
        {
            double avgRemaining = train.Average(x => x.Row.RemainingGoalsAfterGoal);
            double avgBaseline = train.Average(x => x.Baseline.ExpectedRemainingGoals);
            int avgBaselineSample = (int)Math.Round(train.Average(x => x.Baseline.SampleSize), MidpointRounding.AwayFromZero);
            double rawResidual = avgRemaining - avgBaseline;
            double? rawLiftPct = Math.Abs(avgBaseline) < 0.0000001 ? null : rawResidual / avgBaseline;
            double shrinkWeight = train.Count / (train.Count + options.ShrinkK);
            double shrunkResidual = rawResidual * shrinkWeight;
            double shrunkExpected = avgBaseline + shrunkResidual;
            string direction = Direction(shrunkResidual);

            testGroups.TryGetValue(key, out List<ScoredTestEvent>? test);
            test ??= [];
            double? testAvgRemaining = test.Count == 0 ? null : test.Average(x => x.Row.RemainingGoalsAfterGoal);
            double? testAvgBaseline = test.Count == 0 ? null : test.Average(x => x.Baseline.ExpectedRemainingGoals);
            double? testResidualVsBaseline = test.Count == 0 ? null : test.Average(x => x.Row.RemainingGoalsAfterGoal - x.Baseline.ExpectedRemainingGoals);
            double? testResidualVsAngle = test.Count == 0 ? null : test.Average(x => x.Row.RemainingGoalsAfterGoal - shrunkExpected);
            bool? confirmed = direction == "OVER" && testResidualVsBaseline.HasValue
                ? testResidualVsBaseline.Value > 0
                : direction == "UNDER" && testResidualVsBaseline.HasValue
                    ? testResidualVsBaseline.Value < 0
                    : null;

            reportRows.Add(new AfterGoalAngleReportRow
            {
                LeagueKey = key.LeagueKey,
                LeagueName = key.LeagueName,
                MinuteBand = key.MinuteBand,
                Team = key.Team,
                ScoringTeam = key.ScoringTeam,
                ConcedingTeam = key.ConcedingTeam,
                TrainSeasons = string.Join(";", result.TrainSeasons),
                TestSeason = result.TestSeason,
                TrainSampleSize = train.Count,
                TrainAvgRemainingGoalsAfterGoal = avgRemaining,
                TrainAvgBaselineExpectedRemainingGoals = avgBaseline,
                TrainBaselineSampleSizeUsed = avgBaselineSample,
                TrainRawResidual = rawResidual,
                TrainRawLiftPct = rawLiftPct,
                ShrinkWeight = shrinkWeight,
                TrainShrunkResidual = shrunkResidual,
                TrainShrunkExpectedRemainingGoals = shrunkExpected,
                Direction = direction,
                Confidence = Confidence(train.Count, options.MinSample, options.StrongSample),
                TestSampleSize = test.Count,
                TestAvgRemainingGoalsAfterGoal = testAvgRemaining,
                TestAvgBaselineExpectedRemainingGoals = testAvgBaseline,
                TestAvgResidualVsBaseline = testResidualVsBaseline,
                TestAvgResidualVsAngle = testResidualVsAngle,
                TestDirectionConfirmed = confirmed
            });
        }

        List<AfterGoalAngleReportRow> sorted = SortRows(reportRows).ToList();
        if (sorted.All(x => x.TestSampleSize == 0))
            result.Warnings.Add($"{reportName} has no matching test rows.");

        result.ReportRowCounts[reportName] = sorted.Count;
        return sorted;
    }

    private static IEnumerable<AfterGoalAngleReportRow> SortRows(IEnumerable<AfterGoalAngleReportRow> rows)
        => rows.OrderByDescending(x => ConfidenceRank(x.Confidence))
            .ThenByDescending(x => Math.Abs(x.TrainShrunkResidual))
            .ThenByDescending(x => x.TrainSampleSize);

    private static int ConfidenceRank(string confidence)
        => confidence switch
        {
            "HIGH" => 3,
            "MEDIUM" => 2,
            _ => 1
        };

    private static string Confidence(int sample, int minSample, int strongSample)
        => sample >= strongSample ? "HIGH" : sample >= minSample ? "MEDIUM" : "LOW";

    private static string Direction(double shrunkResidual)
        => shrunkResidual >= 0.10 ? "OVER" : shrunkResidual <= -0.10 ? "UNDER" : "NEUTRAL";

    private static void SplitSeasons(IReadOnlyList<AfterGoalEventCsvRow> rows, AfterGoalAngleAnalysisOptions options, AfterGoalAngleAnalysisResult result)
    {
        List<string> seasons = rows.Select(x => x.Season)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(SeasonSortKey)
            .ThenBy(x => x)
            .ToList();
        result.InputSeasons.AddRange(seasons);

        bool hasTrainFrom = !string.IsNullOrWhiteSpace(options.TrainFromSeason);
        bool hasTrainTo = !string.IsNullOrWhiteSpace(options.TrainToSeason);
        bool hasTest = !string.IsNullOrWhiteSpace(options.TestSeason);
        int splitOptionCount = new[] { hasTrainFrom, hasTrainTo, hasTest }.Count(x => x);

        if (splitOptionCount is > 0 and < 3)
            throw new ArgumentException("Provide all split options together: --train-from-season, --train-to-season, and --test-season; or provide none to use default latest-season test split.");

        if (splitOptionCount == 3)
        {
            result.SplitMode = "Explicit";
            result.TestSeason = options.TestSeason;

            if (!seasons.Contains(options.TestSeason, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Explicit test season {options.TestSeason} was requested, but no rows for this season exist in input. Available seasons: {string.Join(", ", seasons)}.");

            result.TrainSeasons.AddRange(seasons
                .Where(x => CompareSeason(x, options.TrainFromSeason) >= 0)
                .Where(x => CompareSeason(x, options.TrainToSeason) <= 0));

            if (result.TrainSeasons.Count == 0)
                throw new ArgumentException($"Explicit train range {options.TrainFromSeason}-{options.TrainToSeason} selected no rows. Available seasons: {string.Join(", ", seasons)}.");
        }
        else if (seasons.Count >= 2)
        {
            result.SplitMode = "DefaultInferred";
            result.TestSeason = seasons[^1];
            result.TrainSeasons.AddRange(seasons.Where(x => !x.Equals(result.TestSeason, StringComparison.OrdinalIgnoreCase)));
        }
        else
        {
            result.Warnings.Add("Train/test split cannot be inferred because fewer than two seasons are present.");
            return;
        }

        if (result.TrainSeasons.Contains(result.TestSeason, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Train seasons and test season overlap: {result.TestSeason}.");

        if (result.TrainSeasons.Count == 0)
            throw new ArgumentException($"Train season selection produced no seasons. Seasons found: {string.Join(", ", seasons)}.");

        result.IgnoredSeasons.AddRange(seasons
            .Where(x => !result.TrainSeasons.Contains(x, StringComparer.OrdinalIgnoreCase) && !x.Equals(result.TestSeason, StringComparison.OrdinalIgnoreCase)));
    }

    private static void ValidateInputRows(IReadOnlyList<AfterGoalEventCsvRow> rows, AfterGoalAngleAnalysisResult result)
    {
        int negativeMinutes = rows.Count(x => !string.IsNullOrWhiteSpace(x.MinutesToNextGoal)
            && int.TryParse(x.MinutesToNextGoal, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            && value < 0);
        if (negativeMinutes > 0)
            result.Warnings.Add($"{negativeMinutes} rows have negative MinutesToNextGoal; regenerate after-goal-events.csv with Patch 1.1.");

        int duplicateKeys = rows.GroupBy(x => $"{x.MatchId}|{x.GoalIndex}", StringComparer.OrdinalIgnoreCase).Count(x => x.Count() > 1);
        if (duplicateKeys > 0)
            result.Warnings.Add($"{duplicateKeys} duplicate MatchId + GoalIndex keys found.");
    }

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

    private static async Task<List<AfterGoalEventCsvRow>> ReadRowsAsync(string path, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"After-goal events input file was not found: {fullPath}", fullPath);

        using var reader = new StreamReader(fullPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new ArgumentException($"Input file is empty: {fullPath}");

        List<string> headers = ParseCsvLine(headerLine);
        var headerIndex = headers.Select((name, index) => new { name, index })
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

        List<string> missing = RequiredColumns.Where(x => !headerIndex.ContainsKey(x)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"Input file is missing required columns: {string.Join(", ", missing)}");

        var rows = new List<AfterGoalEventCsvRow>();
        while (!reader.EndOfStream)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            List<string> values = ParseCsvLine(line);
            rows.Add(new AfterGoalEventCsvRow
            {
                LeagueKey = Get(values, headerIndex, "LeagueKey"),
                LeagueName = Get(values, headerIndex, "LeagueName"),
                Season = Get(values, headerIndex, "Season"),
                MatchId = Get(values, headerIndex, "MatchId"),
                HomeTeam = Get(values, headerIndex, "HomeTeam"),
                AwayTeam = Get(values, headerIndex, "AwayTeam"),
                GoalIndex = GetInt(values, headerIndex, "GoalIndex"),
                GoalMinuteBase = GetInt(values, headerIndex, "GoalMinuteBase"),
                GoalStoppageMinutes = GetInt(values, headerIndex, "GoalStoppageMinutes"),
                GoalMinuteElapsed = GetInt(values, headerIndex, "GoalMinuteElapsed"),
                Period = Get(values, headerIndex, "Period"),
                ScoringTeam = Get(values, headerIndex, "ScoringTeam"),
                ConcedingTeam = Get(values, headerIndex, "ConcedingTeam"),
                TotalGoalsAfter = GetInt(values, headerIndex, "TotalGoalsAfter"),
                ScoreGapAfter = GetInt(values, headerIndex, "ScoreGapAfter"),
                HomeLeadAfter = GetInt(values, headerIndex, "HomeLeadAfter"),
                AwayLeadAfter = GetInt(values, headerIndex, "AwayLeadAfter"),
                IsEqualAfter = GetBool(values, headerIndex, "IsEqualAfter"),
                RemainingGoalsAfterGoal = GetDouble(values, headerIndex, "RemainingGoalsAfterGoal"),
                MinutesToNextGoal = Get(values, headerIndex, "MinutesToNextGoal")
            });
        }

        return rows;
    }

    private static string Get(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> headerIndex, string name)
    {
        int index = headerIndex[name];
        return index < values.Count ? values[index] : string.Empty;
    }

    private static int GetInt(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> headerIndex, string name)
        => int.TryParse(Get(values, headerIndex, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new ArgumentException($"Column {name} contains a non-integer value.");

    private static double GetDouble(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> headerIndex, string name)
        => double.TryParse(Get(values, headerIndex, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : throw new ArgumentException($"Column {name} contains a non-numeric value.");

    private static bool GetBool(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> headerIndex, string name)
        => bool.TryParse(Get(values, headerIndex, name), out bool parsed)
            ? parsed
            : throw new ArgumentException($"Column {name} contains a non-boolean value.");

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    builder.Append(ch);
                }
            }
            else if (ch == ',')
            {
                values.Add(builder.ToString());
                builder.Clear();
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else
            {
                builder.Append(ch);
            }
        }

        values.Add(builder.ToString());
        return values;
    }

    private sealed record ScoredTrainEvent(AfterGoalEventCsvRow Row, BaselineExpectation Baseline, AngleGroupKey GroupKey);
    private sealed record ScoredTestEvent(AfterGoalEventCsvRow Row, BaselineExpectation Baseline, AngleGroupKey GroupKey);
    private sealed record AngleGroupKey(
        string LeagueKey,
        string LeagueName,
        string MinuteBand = "",
        string Team = "",
        string ScoringTeam = "",
        string ConcedingTeam = "");
}

public static class AfterGoalAngleReportWriter
{
    public static async Task WriteAsync(string outputDirectory, AfterGoalAngleAnalysisOptions options, AfterGoalAngleAnalysisResult result, CancellationToken cancellationToken)
    {
        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);

        await WriteReportAsync(Path.Combine(fullDirectory, "league-after-goal-angles.csv"), result.LeagueRows, ReportColumns.League, cancellationToken);
        await WriteReportAsync(Path.Combine(fullDirectory, "league-minute-after-goal-angles.csv"), result.LeagueMinuteRows, ReportColumns.LeagueMinute, cancellationToken);
        await WriteReportAsync(Path.Combine(fullDirectory, "team-after-scoring-angles.csv"), result.TeamScoringRows, ReportColumns.Team, cancellationToken);
        await WriteReportAsync(Path.Combine(fullDirectory, "team-after-conceding-angles.csv"), result.TeamConcedingRows, ReportColumns.Team, cancellationToken);
        await WriteReportAsync(Path.Combine(fullDirectory, "team-minute-after-scoring-angles.csv"), result.TeamMinuteScoringRows, ReportColumns.TeamMinute, cancellationToken);
        await WriteReportAsync(Path.Combine(fullDirectory, "team-minute-after-conceding-angles.csv"), result.TeamMinuteConcedingRows, ReportColumns.TeamMinute, cancellationToken);
        if (options.IncludeOpponentPairs)
            await WriteReportAsync(Path.Combine(fullDirectory, "opponent-pair-after-goal-angles.csv"), result.OpponentPairRows, ReportColumns.OpponentPair, cancellationToken);

        var summary = new
        {
            InputPath = Path.GetFullPath(options.InputPath),
            OutputDirectory = fullDirectory,
            result.TotalRowsRead,
            result.RowsUsed,
            result.TrainRows,
            result.TestRows,
            result.IgnoredRows,
            LeagueKeys = result.LeagueKeys,
            SplitMode = result.SplitMode,
            AvailableSeasons = result.InputSeasons,
            InputSeasons = result.InputSeasons,
            RequestedTrainFromSeason = options.TrainFromSeason,
            RequestedTrainToSeason = options.TrainToSeason,
            RequestedTestSeason = options.TestSeason,
            ResolvedTrainSeasons = result.TrainSeasons,
            ResolvedTestSeason = result.TestSeason,
            TrainSeasons = result.TrainSeasons,
            result.TestSeason,
            IgnoredSeasons = result.IgnoredSeasons,
            MinSample = options.MinSample,
            StrongSample = options.StrongSample,
            ShrinkK = options.ShrinkK,
            EffectiveOptions = new
            {
                Input = Path.GetFullPath(options.InputPath),
                OutputDir = fullDirectory,
                options.TrainFromSeason,
                options.TrainToSeason,
                options.TestSeason,
                options.MinSample,
                options.StrongSample,
                options.ShrinkK,
                options.IncludeOpponentPairs
            },
            RawCommandLine = options.RawCommandLine,
            ReportRows = result.ReportRowCounts,
            Warnings = result.Warnings,
            Timestamp = DateTimeOffset.UtcNow
        };

        string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(fullDirectory, "after-goal-angle-analysis-summary.json"), json, Encoding.UTF8, cancellationToken);
    }

    private static async Task WriteReportAsync(string path, IReadOnlyList<AfterGoalAngleReportRow> rows, ReportColumns columns, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(string.Join(",", Header(columns)));
        foreach (AfterGoalAngleReportRow row in rows)
            await writer.WriteLineAsync(ToCsvLine(Values(row, columns)));
    }

    private static IEnumerable<string> Header(ReportColumns columns)
    {
        yield return "LeagueKey";
        yield return "LeagueName";
        if (columns is ReportColumns.LeagueMinute or ReportColumns.TeamMinute)
            yield return "MinuteBand";
        if (columns is ReportColumns.Team or ReportColumns.TeamMinute)
            yield return "Team";
        if (columns == ReportColumns.OpponentPair)
        {
            yield return "ScoringTeam";
            yield return "ConcedingTeam";
        }

        foreach (string common in CommonHeaders)
            yield return common;
    }

    private static IEnumerable<string> Values(AfterGoalAngleReportRow row, ReportColumns columns)
    {
        yield return row.LeagueKey;
        yield return row.LeagueName;
        if (columns is ReportColumns.LeagueMinute or ReportColumns.TeamMinute)
            yield return row.MinuteBand;
        if (columns is ReportColumns.Team or ReportColumns.TeamMinute)
            yield return row.Team;
        if (columns == ReportColumns.OpponentPair)
        {
            yield return row.ScoringTeam;
            yield return row.ConcedingTeam;
        }

        yield return row.TrainSeasons;
        yield return row.TestSeason;
        yield return row.TrainSampleSize.ToString(CultureInfo.InvariantCulture);
        yield return Format(row.TrainAvgRemainingGoalsAfterGoal);
        yield return Format(row.TrainAvgBaselineExpectedRemainingGoals);
        yield return row.TrainBaselineSampleSizeUsed.ToString(CultureInfo.InvariantCulture);
        yield return Format(row.TrainRawResidual);
        yield return Format(row.TrainRawLiftPct);
        yield return Format(row.ShrinkWeight);
        yield return Format(row.TrainShrunkResidual);
        yield return Format(row.TrainShrunkExpectedRemainingGoals);
        yield return row.Direction;
        yield return row.Confidence;
        yield return row.TestSampleSize.ToString(CultureInfo.InvariantCulture);
        yield return Format(row.TestAvgRemainingGoalsAfterGoal);
        yield return Format(row.TestAvgBaselineExpectedRemainingGoals);
        yield return Format(row.TestAvgResidualVsBaseline);
        yield return Format(row.TestAvgResidualVsAngle);
        yield return row.TestDirectionConfirmed?.ToString(CultureInfo.InvariantCulture).ToLowerInvariant() ?? string.Empty;
    }

    private static readonly string[] CommonHeaders =
    [
        "TrainSeasons",
        "TestSeason",
        "TrainSampleSize",
        "TrainAvgRemainingGoalsAfterGoal",
        "TrainAvgBaselineExpectedRemainingGoals",
        "TrainBaselineSampleSizeUsed",
        "TrainRawResidual",
        "TrainRawLiftPct",
        "ShrinkWeight",
        "TrainShrunkResidual",
        "TrainShrunkExpectedRemainingGoals",
        "Direction",
        "Confidence",
        "TestSampleSize",
        "TestAvgRemainingGoalsAfterGoal",
        "TestAvgBaselineExpectedRemainingGoals",
        "TestAvgResidualVsBaseline",
        "TestAvgResidualVsAngle",
        "TestDirectionConfirmed"
    ];

    private static string Format(double? value)
        => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;

    private static string ToCsvLine(IEnumerable<string> values)
        => string.Join(",", values.Select(Csv));

    private static string Csv(string? value)
    {
        string text = value ?? string.Empty;
        return text.Contains('"') || text.Contains(',') || text.Contains('\r') || text.Contains('\n')
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }

    private enum ReportColumns
    {
        League,
        LeagueMinute,
        Team,
        TeamMinute,
        OpponentPair
    }
}
