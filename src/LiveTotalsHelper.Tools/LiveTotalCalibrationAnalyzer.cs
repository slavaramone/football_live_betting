using System.Globalization;
using System.Text;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalCalibrationAnalysisOptions
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public List<int> TrainingSeasonIds { get; } = [];
    public List<int> TestSeasonIds { get; } = [];
}

public sealed class LiveTotalCalibrationAnalysisResult
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public int RowsRead { get; set; }
    public int RowsAnalyzed { get; set; }
    public bool HasTrainTestSplit { get; set; }
    public List<LiveTotalCalibrationBucketResult> Buckets { get; } = [];
    public List<LiveTotalCalibrationTrainTestBucketResult> TrainTestBuckets { get; } = [];
}

public sealed class LiveTotalCalibrationBucketResult
{
    public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
    public string MinuteBand { get; set; } = string.Empty;
    public string DetailedScoreState { get; set; } = string.Empty;
    public string GoalChangeType { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double LeagueAverageFinalGoals { get; set; }
    public double TotalFinalGoals { get; set; }
    public double ActualRemainingGoals { get; set; }
    public double ActualRemainingGoalsPerRow { get; set; }
    public double AverageTimingRemainingShare { get; set; }
    public double BaselineRemainingGoalsPerRow { get; set; }
    public double? CorrectionFactor { get; set; }
}

public sealed class LiveTotalCalibrationTrainTestBucketResult
{
    public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
    public string MinuteBand { get; set; } = string.Empty;
    public string DetailedScoreState { get; set; } = string.Empty;
    public string GoalChangeType { get; set; } = string.Empty;

    public int TrainRows { get; set; }
    public int TrainMatches { get; set; }
    public double TrainLeagueAverageFinalGoals { get; set; }
    public double TrainActualRemainingGoalsPerRow { get; set; }
    public double TrainAverageTimingRemainingShare { get; set; }
    public double TrainBaselineRemainingGoalsPerRow { get; set; }
    public double? CorrectionFactor { get; set; }

    public int TestRows { get; set; }
    public int TestMatches { get; set; }
    public double TestLeagueAverageFinalGoals { get; set; }
    public double TestActualRemainingGoalsPerRow { get; set; }
    public double TestAverageTimingRemainingShare { get; set; }
    public double TestBaselineRemainingGoalsPerRow { get; set; }
    public double? TestCorrectedRemainingGoalsPerRow { get; set; }
    public double? TestBaselineSignedErrorPerRow { get; set; }
    public double? TestCorrectedSignedErrorPerRow { get; set; }
    public double? TestBaselineAbsErrorPerRow { get; set; }
    public double? TestCorrectedAbsErrorPerRow { get; set; }
}

public sealed class LiveTotalCalibrationAnalyzer
{
    private static readonly string[] ScoreStateOrder =
    [
        "NilNil",
        "LevelWithGoals",
        "OneGoalMargin",
        "TwoGoalMargin",
        "ThreePlusGoalMargin"
    ];

    private readonly LiveTotalCalibrationAnalysisOptions _options;

    public LiveTotalCalibrationAnalyzer(LiveTotalCalibrationAnalysisOptions options)
    {
        _options = options;
    }

    public async Task<LiveTotalCalibrationAnalysisResult> AnalyzeAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        List<LiveTotalCalibrationInputRow> rows = await ReadRowsAsync(_options.InputPath, cancellationToken);
        var analyzedRows = rows
            .Select(x => new RowWithBand(x, ResolveMinuteBand(x.StateTrigger, x.Minute)))
            .Where(x => !string.IsNullOrWhiteSpace(x.MinuteBand))
            .ToList();

        var result = new LiveTotalCalibrationAnalysisResult
        {
            InputPath = _options.InputPath,
            OutputPath = ResolveOutputPath(),
            RowsRead = rows.Count,
            RowsAnalyzed = analyzedRows.Count,
            HasTrainTestSplit = _options.TrainingSeasonIds.Count > 0 || _options.TestSeasonIds.Count > 0
        };

        if (result.HasTrainTestSplit)
        {
            if (_options.TrainingSeasonIds.Count == 0 || _options.TestSeasonIds.Count == 0)
                throw new ArgumentException("For train/test mode, provide both --training-season-ids and --test-season-ids.");

            BuildTrainTestAnalysis(result, analyzedRows);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(result.OutputPath)) ?? ".");
            await File.WriteAllTextAsync(result.OutputPath, ToTrainTestCsv(result.TrainTestBuckets), Encoding.UTF8, cancellationToken);
            return result;
        }

        result.Buckets.AddRange(BuildBucketResults(analyzedRows.Select(x => x.Row).ToList(), analyzedRows));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(result.OutputPath)) ?? ".");
        await File.WriteAllTextAsync(result.OutputPath, ToCsv(result.Buckets), Encoding.UTF8, cancellationToken);
        return result;
    }

    private void BuildTrainTestAnalysis(LiveTotalCalibrationAnalysisResult result, IReadOnlyList<RowWithBand> analyzedRows)
    {
        List<RowWithBand> train = analyzedRows
            .Where(x => _options.TrainingSeasonIds.Contains(x.Row.SofaScoreSeasonId))
            .ToList();
        List<RowWithBand> test = analyzedRows
            .Where(x => _options.TestSeasonIds.Contains(x.Row.SofaScoreSeasonId))
            .ToList();

        Dictionary<(string StateTrigger, string MinuteBand, string DetailedScoreState, string GoalChangeType), LiveTotalCalibrationBucketResult> trainBuckets = BuildBucketResults(
                train.Select(x => x.Row).ToList(),
                train)
            .ToDictionary(x => (x.StateTrigger, x.MinuteBand, x.DetailedScoreState, x.GoalChangeType));

        Dictionary<(string StateTrigger, string MinuteBand, string DetailedScoreState, string GoalChangeType), LiveTotalCalibrationBucketResult> testBuckets = BuildBucketResults(
                test.Select(x => x.Row).ToList(),
                test)
            .ToDictionary(x => (x.StateTrigger, x.MinuteBand, x.DetailedScoreState, x.GoalChangeType));

        var keys = trainBuckets.Keys
            .Union(testBuckets.Keys)
            .OrderBy(x => TriggerOrder(x.StateTrigger))
            .ThenBy(x => MinuteBandOrder(x.MinuteBand))
            .ThenBy(x => ScoreStateIndex(x.DetailedScoreState))
            .ThenBy(x => GoalChangeTypeOrder(x.GoalChangeType))
            .ToList();

        foreach ((string StateTrigger, string MinuteBand, string DetailedScoreState, string GoalChangeType) key in keys)
        {
            trainBuckets.TryGetValue(key, out LiveTotalCalibrationBucketResult? tr);
            testBuckets.TryGetValue(key, out LiveTotalCalibrationBucketResult? te);

            double? corrected = te is not null && tr?.CorrectionFactor is not null
                ? te.BaselineRemainingGoalsPerRow * tr.CorrectionFactor.Value
                : null;

            double? baselineSignedError = te is not null
                ? te.BaselineRemainingGoalsPerRow - te.ActualRemainingGoalsPerRow
                : null;

            double? correctedSignedError = te is not null && corrected.HasValue
                ? corrected.Value - te.ActualRemainingGoalsPerRow
                : null;

            result.TrainTestBuckets.Add(new LiveTotalCalibrationTrainTestBucketResult
            {
                StateTrigger = key.StateTrigger,
                MinuteBand = key.MinuteBand,
                DetailedScoreState = key.DetailedScoreState,
                GoalChangeType = key.GoalChangeType,

                TrainRows = tr?.Rows ?? 0,
                TrainMatches = tr?.Matches ?? 0,
                TrainLeagueAverageFinalGoals = tr?.LeagueAverageFinalGoals ?? 0.0,
                TrainActualRemainingGoalsPerRow = tr?.ActualRemainingGoalsPerRow ?? 0.0,
                TrainAverageTimingRemainingShare = tr?.AverageTimingRemainingShare ?? 0.0,
                TrainBaselineRemainingGoalsPerRow = tr?.BaselineRemainingGoalsPerRow ?? 0.0,
                CorrectionFactor = tr?.CorrectionFactor,

                TestRows = te?.Rows ?? 0,
                TestMatches = te?.Matches ?? 0,
                TestLeagueAverageFinalGoals = te?.LeagueAverageFinalGoals ?? 0.0,
                TestActualRemainingGoalsPerRow = te?.ActualRemainingGoalsPerRow ?? 0.0,
                TestAverageTimingRemainingShare = te?.AverageTimingRemainingShare ?? 0.0,
                TestBaselineRemainingGoalsPerRow = te?.BaselineRemainingGoalsPerRow ?? 0.0,
                TestCorrectedRemainingGoalsPerRow = corrected,
                TestBaselineSignedErrorPerRow = baselineSignedError,
                TestCorrectedSignedErrorPerRow = correctedSignedError,
                TestBaselineAbsErrorPerRow = baselineSignedError.HasValue ? Math.Abs(baselineSignedError.Value) : null,
                TestCorrectedAbsErrorPerRow = correctedSignedError.HasValue ? Math.Abs(correctedSignedError.Value) : null
            });
        }
    }

    private static List<LiveTotalCalibrationBucketResult> BuildBucketResults(IReadOnlyList<LiveTotalCalibrationInputRow> allRowsForLeagueAverage, IReadOnlyList<RowWithBand> rowsWithBands)
    {
        double leagueAverageFinalGoals = allRowsForLeagueAverage
            .GroupBy(x => x.MatchId)
            .Select(x => x.First().ActualFinalTotalGoals)
            .DefaultIfEmpty(0.0)
            .Average();

        var result = new List<LiveTotalCalibrationBucketResult>();

        foreach (var group in rowsWithBands
            .GroupBy(x => new { x.Row.StateTrigger, x.MinuteBand, x.Row.DetailedScoreState, GoalChangeType = CorrectionGoalChangeType(x.Row.StateTrigger, x.Row.GoalChangeType) })
            .OrderBy(x => TriggerOrder(x.Key.StateTrigger))
            .ThenBy(x => MinuteBandOrder(x.Key.MinuteBand))
            .ThenBy(x => ScoreStateIndex(x.Key.DetailedScoreState))
            .ThenBy(x => GoalChangeTypeOrder(x.Key.GoalChangeType)))
        {
            List<LiveTotalCalibrationInputRow> bucketRows = group.Select(x => x.Row).ToList();
            double totalFinalGoals = bucketRows.Sum(x => x.ActualFinalTotalGoals);
            double actualRemainingGoals = bucketRows.Sum(x => x.ActualRemainingGoals);
            double actualRemainingGoalsPerRow = actualRemainingGoals / bucketRows.Count;
            double averageTimingRemainingShare = bucketRows.Average(x => x.TimingRemainingShare);
            double baselineRemainingGoalsPerRow = leagueAverageFinalGoals * averageTimingRemainingShare;
            double? correctionFactor = baselineRemainingGoalsPerRow > 0
                ? actualRemainingGoalsPerRow / baselineRemainingGoalsPerRow
                : null;

            result.Add(new LiveTotalCalibrationBucketResult
            {
                StateTrigger = group.Key.StateTrigger,
                MinuteBand = group.Key.MinuteBand,
                DetailedScoreState = group.Key.DetailedScoreState,
                GoalChangeType = group.Key.GoalChangeType,
                Rows = bucketRows.Count,
                Matches = bucketRows.Select(x => x.MatchId).Distinct().Count(),
                LeagueAverageFinalGoals = leagueAverageFinalGoals,
                TotalFinalGoals = totalFinalGoals,
                ActualRemainingGoals = actualRemainingGoals,
                ActualRemainingGoalsPerRow = actualRemainingGoalsPerRow,
                AverageTimingRemainingShare = averageTimingRemainingShare,
                BaselineRemainingGoalsPerRow = baselineRemainingGoalsPerRow,
                CorrectionFactor = correctionFactor
            });
        }

        return result;
    }

    private static string CorrectionGoalChangeType(string stateTrigger, string goalChangeType)
    {
        return LiveTotalStateTrigger.Normalize(stateTrigger).Equals(LiveTotalStateTrigger.AfterGoal, StringComparison.OrdinalIgnoreCase)
            ? LiveTotalGoalChangeClassifier.Normalize(goalChangeType)
            : LiveTotalGoalChangeClassifier.None;
    }

    private static int GoalChangeTypeOrder(string value) => LiveTotalGoalChangeClassifier.Normalize(value) switch
    {
        LiveTotalGoalChangeClassifier.GoAheadGoal => 1,
        LiveTotalGoalChangeClassifier.Equalizer => 2,
        LiveTotalGoalChangeClassifier.MarginIncrease => 3,
        LiveTotalGoalChangeClassifier.MarginDecrease => 4,
        _ => 99
    };

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.InputPath))
            throw new ArgumentException("Missing required argument --input.");
        if (!File.Exists(_options.InputPath))
            throw new FileNotFoundException("Live total calibration dataset CSV was not found.", _options.InputPath);
    }

    private string ResolveOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
            return _options.OutputPath;

        string directory = Path.GetDirectoryName(_options.InputPath) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(_options.InputPath);
        return Path.Combine(directory, $"{fileName}-analysis.csv");
    }

    private static string ResolveMinuteBand(string stateTrigger, int minute) =>
        LiveTotalStateCorrectionResolver.MinuteBand(stateTrigger, minute);

    private static int TriggerOrder(string stateTrigger) => LiveTotalStateTrigger.Normalize(stateTrigger) switch
    {
        LiveTotalStateTrigger.FixedMinute => 1,
        LiveTotalStateTrigger.AfterGoal => 2,
        LiveTotalStateTrigger.AfterRedCard => 3,
        _ => 99
    };

    private static int MinuteBandOrder(string band) => band switch
    {
        "1-20" => 1,
        "10-20" => 1,
        "21-35" => 2,
        "25-35" => 2,
        "36-50" => 3,
        "40-50" => 3,
        "51-65" => 4,
        "55-65" => 4,
        "66-90" => 5,
        "70-85" => 5,
        _ => 99
    };

    private static int ScoreStateIndex(string scoreState)
    {
        int index = Array.IndexOf(ScoreStateOrder, scoreState);
        return index >= 0 ? index : int.MaxValue;
    }

    private static async Task<List<LiveTotalCalibrationInputRow>> ReadRowsAsync(string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        List<List<string>> records = ParseCsv(text);
        if (records.Count == 0)
            return [];

        string[] headers = records[0].Select(x => x.Trim()).ToArray();
        var index = headers
            .Select((name, position) => new { name, position })
            .ToDictionary(x => x.name, x => x.position, StringComparer.OrdinalIgnoreCase);

        Require(index, "SofaScoreSeasonId");
        Require(index, "MatchId");
        Require(index, "Minute");
        Require(index, "DetailedScoreState");
        Require(index, "TimingRemainingShare");
        Require(index, "ActualFinalTotalGoals");
        Require(index, "ActualRemainingGoals");

        var rows = new List<LiveTotalCalibrationInputRow>();
        foreach (List<string> record in records.Skip(1))
        {
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0]))
                continue;

            if (!TryGetInt(record, index, "SofaScoreSeasonId", out int seasonId) ||
                !TryGetInt(record, index, "MatchId", out int matchId) ||
                !TryGetInt(record, index, "Minute", out int minute) ||
                !TryGetDouble(record, index, "TimingRemainingShare", out double timingRemainingShare) ||
                !TryGetDouble(record, index, "ActualFinalTotalGoals", out double actualFinalTotalGoals) ||
                !TryGetDouble(record, index, "ActualRemainingGoals", out double actualRemainingGoals))
                continue;

            string detailedScoreState = GetString(record, index, "DetailedScoreState");
            if (string.IsNullOrWhiteSpace(detailedScoreState))
                continue;

            rows.Add(new LiveTotalCalibrationInputRow
            {
                StateTrigger = index.ContainsKey("StateTrigger")
                    ? LiveTotalStateTrigger.Normalize(GetString(record, index, "StateTrigger"))
                    : LiveTotalStateTrigger.FixedMinute,
                SofaScoreSeasonId = seasonId,
                MatchId = matchId,
                Minute = minute,
                DetailedScoreState = detailedScoreState,
                GoalChangeType = index.ContainsKey("GoalChangeType")
                    ? LiveTotalGoalChangeClassifier.Normalize(GetString(record, index, "GoalChangeType"))
                    : LiveTotalGoalChangeClassifier.None,
                TimingRemainingShare = timingRemainingShare,
                ActualFinalTotalGoals = actualFinalTotalGoals,
                ActualRemainingGoals = actualRemainingGoals
            });
        }

        return rows;
    }

    private static void Require(IReadOnlyDictionary<string, int> index, string column)
    {
        if (!index.ContainsKey(column))
            throw new ArgumentException($"Input CSV is missing required column '{column}'.");
    }

    private static bool TryGetInt(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column, out int value)
    {
        value = 0;
        return index.TryGetValue(column, out int position) &&
               position < record.Count &&
               int.TryParse(record[position], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetDouble(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column, out double value)
    {
        value = 0;
        return index.TryGetValue(column, out int position) &&
               position < record.Count &&
               double.TryParse(record[position], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string GetString(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column) =>
        index.TryGetValue(column, out int position) && position < record.Count ? record[position] : string.Empty;

    private static string ToCsv(IReadOnlyCollection<LiveTotalCalibrationBucketResult> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("StateTrigger,MinuteBand,DetailedScoreState,GoalChangeType,Rows,Matches,LeagueAverageFinalGoals,TotalFinalGoals,ActualRemainingGoals,ActualRemainingGoalsPerRow,AverageTimingRemainingShare,BaselineRemainingGoalsPerRow,CorrectionFactor");
        foreach (LiveTotalCalibrationBucketResult row in rows)
        {
            sb.AppendLine(string.Join(',',
                EscapeCsv(row.StateTrigger),
                EscapeCsv(row.MinuteBand),
                EscapeCsv(row.DetailedScoreState),
                EscapeCsv(row.GoalChangeType),
                row.Rows.ToString(CultureInfo.InvariantCulture),
                row.Matches.ToString(CultureInfo.InvariantCulture),
                D(row.LeagueAverageFinalGoals),
                D(row.TotalFinalGoals),
                D(row.ActualRemainingGoals),
                D(row.ActualRemainingGoalsPerRow),
                D(row.AverageTimingRemainingShare),
                D(row.BaselineRemainingGoalsPerRow),
                D(row.CorrectionFactor)));
        }
        return sb.ToString();
    }

    private static string ToTrainTestCsv(IReadOnlyCollection<LiveTotalCalibrationTrainTestBucketResult> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("StateTrigger,MinuteBand,DetailedScoreState,GoalChangeType,TrainRows,TrainMatches,TrainLeagueAverageFinalGoals,TrainActualRemainingGoalsPerRow,TrainAverageTimingRemainingShare,TrainBaselineRemainingGoalsPerRow,CorrectionFactor,TestRows,TestMatches,TestLeagueAverageFinalGoals,TestActualRemainingGoalsPerRow,TestAverageTimingRemainingShare,TestBaselineRemainingGoalsPerRow,TestCorrectedRemainingGoalsPerRow,TestBaselineSignedErrorPerRow,TestCorrectedSignedErrorPerRow,TestBaselineAbsErrorPerRow,TestCorrectedAbsErrorPerRow");
        foreach (LiveTotalCalibrationTrainTestBucketResult row in rows)
        {
            sb.AppendLine(string.Join(',',
                EscapeCsv(row.StateTrigger),
                EscapeCsv(row.MinuteBand),
                EscapeCsv(row.DetailedScoreState),
                EscapeCsv(row.GoalChangeType),
                row.TrainRows.ToString(CultureInfo.InvariantCulture),
                row.TrainMatches.ToString(CultureInfo.InvariantCulture),
                D(row.TrainLeagueAverageFinalGoals),
                D(row.TrainActualRemainingGoalsPerRow),
                D(row.TrainAverageTimingRemainingShare),
                D(row.TrainBaselineRemainingGoalsPerRow),
                D(row.CorrectionFactor),
                row.TestRows.ToString(CultureInfo.InvariantCulture),
                row.TestMatches.ToString(CultureInfo.InvariantCulture),
                D(row.TestLeagueAverageFinalGoals),
                D(row.TestActualRemainingGoalsPerRow),
                D(row.TestAverageTimingRemainingShare),
                D(row.TestBaselineRemainingGoalsPerRow),
                D(row.TestCorrectedRemainingGoalsPerRow),
                D(row.TestBaselineSignedErrorPerRow),
                D(row.TestCorrectedSignedErrorPerRow),
                D(row.TestBaselineAbsErrorPerRow),
                D(row.TestCorrectedAbsErrorPerRow)));
        }
        return sb.ToString();
    }

    private static string D(double? value) => value?.ToString("0.######", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }

    private sealed class LiveTotalCalibrationInputRow
    {
        public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
        public int SofaScoreSeasonId { get; set; }
        public int MatchId { get; set; }
        public int Minute { get; set; }
        public string DetailedScoreState { get; set; } = string.Empty;
        public string GoalChangeType { get; set; } = string.Empty;
        public double TimingRemainingShare { get; set; }
        public double ActualFinalTotalGoals { get; set; }
        public double ActualRemainingGoals { get; set; }
    }

    private sealed record RowWithBand(LiveTotalCalibrationInputRow Row, string MinuteBand);
}
