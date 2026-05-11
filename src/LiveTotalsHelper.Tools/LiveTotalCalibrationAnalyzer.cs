using System.Globalization;
using System.Text;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalCalibrationAnalysisOptions
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
}

public sealed class LiveTotalCalibrationAnalysisResult
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public int RowsRead { get; set; }
    public int RowsAnalyzed { get; set; }
    public List<LiveTotalCalibrationBucketResult> Buckets { get; } = [];
}

public sealed class LiveTotalCalibrationBucketResult
{
    public string MinuteBand { get; set; } = string.Empty;
    public string DetailedScoreState { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double TotalFinalGoals { get; set; }
    public double ActualRemainingGoals { get; set; }
    public double? ActualRemainingShare { get; set; }
    public double AverageTimingRemainingShare { get; set; }
    public double? CorrectionFactor { get; set; }
    public double ActualRemainingGoalsPerRow { get; set; }
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
            .Select(x => new { Row = x, MinuteBand = ResolveMinuteBand(x.Minute) })
            .Where(x => !string.IsNullOrWhiteSpace(x.MinuteBand))
            .ToList();

        var result = new LiveTotalCalibrationAnalysisResult
        {
            InputPath = _options.InputPath,
            OutputPath = ResolveOutputPath(),
            RowsRead = rows.Count,
            RowsAnalyzed = analyzedRows.Count
        };

        foreach (var group in analyzedRows
            .GroupBy(x => new { x.MinuteBand, x.Row.DetailedScoreState })
            .OrderBy(x => MinuteBandOrder(x.Key.MinuteBand))
            .ThenBy(x => ScoreStateIndex(x.Key.DetailedScoreState)))
        {
            List<LiveTotalCalibrationInputRow> bucketRows = group.Select(x => x.Row).ToList();
            double totalFinalGoals = bucketRows.Sum(x => x.ActualFinalTotalGoals);
            double actualRemainingGoals = bucketRows.Sum(x => x.ActualRemainingGoals);
            double? actualRemainingShare = totalFinalGoals > 0 ? actualRemainingGoals / totalFinalGoals : null;
            double averageTimingRemainingShare = bucketRows.Average(x => x.TimingRemainingShare);
            double? correctionFactor = actualRemainingShare.HasValue && averageTimingRemainingShare > 0
                ? actualRemainingShare.Value / averageTimingRemainingShare
                : null;

            result.Buckets.Add(new LiveTotalCalibrationBucketResult
            {
                MinuteBand = group.Key.MinuteBand,
                DetailedScoreState = group.Key.DetailedScoreState,
                Rows = bucketRows.Count,
                Matches = bucketRows.Select(x => x.MatchId).Distinct().Count(),
                TotalFinalGoals = totalFinalGoals,
                ActualRemainingGoals = actualRemainingGoals,
                ActualRemainingShare = actualRemainingShare,
                AverageTimingRemainingShare = averageTimingRemainingShare,
                CorrectionFactor = correctionFactor,
                ActualRemainingGoalsPerRow = actualRemainingGoals / bucketRows.Count
            });
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(result.OutputPath)) ?? ".");
        await File.WriteAllTextAsync(result.OutputPath, ToCsv(result.Buckets), Encoding.UTF8, cancellationToken);
        return result;
    }

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

    private static string ResolveMinuteBand(int minute) => minute switch
    {
        >= 10 and <= 20 => "10-20",
        >= 25 and <= 35 => "25-35",
        >= 40 and <= 50 => "40-50",
        >= 55 and <= 65 => "55-65",
        >= 70 and <= 85 => "70-85",
        _ => string.Empty
    };

    private static int MinuteBandOrder(string band) => band switch
    {
        "10-20" => 1,
        "25-35" => 2,
        "40-50" => 3,
        "55-65" => 4,
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

            if (!TryGetInt(record, index, "MatchId", out int matchId) ||
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
                MatchId = matchId,
                Minute = minute,
                DetailedScoreState = detailedScoreState,
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
        sb.AppendLine("MinuteBand,DetailedScoreState,Rows,Matches,TotalFinalGoals,ActualRemainingGoals,ActualRemainingShare,AverageTimingRemainingShare,CorrectionFactor,ActualRemainingGoalsPerRow");
        foreach (LiveTotalCalibrationBucketResult row in rows)
        {
            sb.AppendLine(string.Join(',',
                EscapeCsv(row.MinuteBand),
                EscapeCsv(row.DetailedScoreState),
                row.Rows.ToString(CultureInfo.InvariantCulture),
                row.Matches.ToString(CultureInfo.InvariantCulture),
                D(row.TotalFinalGoals),
                D(row.ActualRemainingGoals),
                D(row.ActualRemainingShare),
                D(row.AverageTimingRemainingShare),
                D(row.CorrectionFactor),
                D(row.ActualRemainingGoalsPerRow)));
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
        public int MatchId { get; set; }
        public int Minute { get; set; }
        public string DetailedScoreState { get; set; } = string.Empty;
        public double TimingRemainingShare { get; set; }
        public double ActualFinalTotalGoals { get; set; }
        public double ActualRemainingGoals { get; set; }
    }
}
