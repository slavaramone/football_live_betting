using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalModelEvaluationOptions
{
    public string InputPath { get; set; } = string.Empty;
    public string StateCorrectionPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public List<int> TestSeasonIds { get; } = [];
    public bool RequireTeamVolumeHistory { get; set; }
}

public sealed class LiveTotalModelEvaluationResult
{
    public string InputPath { get; set; } = string.Empty;
    public string StateCorrectionPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public int RowsRead { get; set; }
    public int TestRows { get; set; }
    public int SupportedRows { get; set; }
    public List<LiveTotalModelEvaluationSummary> Summaries { get; } = [];
}

public sealed class LiveTotalModelEvaluationSummary
{
    public string StateTrigger { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double BaselineMae { get; set; }
    public double StateCorrectedMae { get; set; }
    public double StatePlusTeamMae { get; set; }
    public double BaselineBias { get; set; }
    public double StateCorrectedBias { get; set; }
    public double StatePlusTeamBias { get; set; }
    public double BaselineRmse { get; set; }
    public double StateCorrectedRmse { get; set; }
    public double StatePlusTeamRmse { get; set; }
    public double AverageTeamVolumeFactor { get; set; }
}

public sealed class LiveTotalModelEvaluator
{
    private readonly LiveTotalModelEvaluationOptions _options;

    public LiveTotalModelEvaluator(LiveTotalModelEvaluationOptions options)
    {
        _options = options;
    }

    public async Task<LiveTotalModelEvaluationResult> EvaluateAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        List<InputRow> rows = await ReadRowsAsync(_options.InputPath, cancellationToken);
        await using FileStream stream = File.OpenRead(_options.StateCorrectionPath);
        LiveTotalStateCorrectionFile correction = await JsonSerializer.DeserializeAsync<LiveTotalStateCorrectionFile>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, cancellationToken) ?? throw new InvalidOperationException("Could not read state correction JSON.");

        List<InputRow> testRows = rows
            .Where(x => _options.TestSeasonIds.Contains(x.SofaScoreSeasonId))
            .ToList();

        var observations = new List<Observation>();
        foreach (InputRow row in testRows)
        {
            LiveTotalStateCorrectionResolution resolved = LiveTotalStateCorrectionResolver.Resolve(
                correction,
                row.StateTrigger,
                row.Minute,
                row.HomeGoals,
                row.AwayGoals);

            if (!resolved.IsSupported)
                continue;

            if (_options.RequireTeamVolumeHistory && (row.HomePreviousMatches <= 0 || row.AwayPreviousMatches <= 0))
                continue;

            double baseline = correction.LeagueAverageFinalGoals * row.TimingRemainingShare;
            double stateCorrected = baseline * resolved.Factor;
            double statePlusTeam = stateCorrected * row.MatchTeamVolumeFactor;

            observations.Add(new Observation
            {
                Row = row,
                Baseline = baseline,
                StateCorrected = stateCorrected,
                StatePlusTeam = statePlusTeam
            });
        }

        var result = new LiveTotalModelEvaluationResult
        {
            InputPath = _options.InputPath,
            StateCorrectionPath = _options.StateCorrectionPath,
            OutputPath = ResolveOutputPath(),
            RowsRead = rows.Count,
            TestRows = testRows.Count,
            SupportedRows = observations.Count
        };

        result.Summaries.Add(BuildSummary("All", observations));
        foreach (IGrouping<string, Observation> group in observations
            .GroupBy(x => x.Row.StateTrigger)
            .OrderBy(x => TriggerOrder(x.Key)))
        {
            result.Summaries.Add(BuildSummary(group.Key, group.ToList()));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(result.OutputPath)) ?? ".");
        await File.WriteAllTextAsync(result.OutputPath, ToCsv(result.Summaries), Encoding.UTF8, cancellationToken);
        return result;
    }

    private LiveTotalModelEvaluationSummary BuildSummary(string stateTrigger, IReadOnlyCollection<Observation> rows)
    {
        if (rows.Count == 0)
            return new LiveTotalModelEvaluationSummary { StateTrigger = stateTrigger };

        return new LiveTotalModelEvaluationSummary
        {
            StateTrigger = stateTrigger,
            Rows = rows.Count,
            Matches = rows.Select(x => x.Row.MatchId).Distinct().Count(),
            BaselineMae = rows.Average(x => Math.Abs(x.Baseline - x.Row.ActualRemainingGoals)),
            StateCorrectedMae = rows.Average(x => Math.Abs(x.StateCorrected - x.Row.ActualRemainingGoals)),
            StatePlusTeamMae = rows.Average(x => Math.Abs(x.StatePlusTeam - x.Row.ActualRemainingGoals)),
            BaselineBias = rows.Average(x => x.Baseline - x.Row.ActualRemainingGoals),
            StateCorrectedBias = rows.Average(x => x.StateCorrected - x.Row.ActualRemainingGoals),
            StatePlusTeamBias = rows.Average(x => x.StatePlusTeam - x.Row.ActualRemainingGoals),
            BaselineRmse = Math.Sqrt(rows.Average(x => Squared(x.Baseline - x.Row.ActualRemainingGoals))),
            StateCorrectedRmse = Math.Sqrt(rows.Average(x => Squared(x.StateCorrected - x.Row.ActualRemainingGoals))),
            StatePlusTeamRmse = Math.Sqrt(rows.Average(x => Squared(x.StatePlusTeam - x.Row.ActualRemainingGoals))),
            AverageTeamVolumeFactor = rows.Average(x => x.Row.MatchTeamVolumeFactor)
        };
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.InputPath))
            throw new ArgumentException("Missing required argument --input.");
        if (!File.Exists(_options.InputPath))
            throw new FileNotFoundException("Live total calibration dataset CSV was not found.", _options.InputPath);
        if (string.IsNullOrWhiteSpace(_options.StateCorrectionPath))
            throw new ArgumentException("Missing required argument --state-correction.");
        if (!File.Exists(_options.StateCorrectionPath))
            throw new FileNotFoundException("State correction JSON was not found.", _options.StateCorrectionPath);
        if (_options.TestSeasonIds.Count == 0)
            throw new ArgumentException("Missing required argument --test-season-ids.");
    }

    private string ResolveOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
            return _options.OutputPath;

        string directory = Path.GetDirectoryName(_options.InputPath) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(_options.InputPath);
        return Path.Combine(directory, $"{fileName}-model-evaluation.csv");
    }

    private static async Task<List<InputRow>> ReadRowsAsync(string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        List<List<string>> records = ParseCsv(text);
        if (records.Count == 0)
            return [];

        string[] headers = records[0].Select(x => x.Trim()).ToArray();
        var index = headers.Select((name, position) => new { name, position })
            .ToDictionary(x => x.name, x => x.position, StringComparer.OrdinalIgnoreCase);

        foreach (string required in new[]
        {
            "SofaScoreSeasonId", "MatchId", "StateTrigger", "Minute", "HomeGoals", "AwayGoals",
            "TimingRemainingShare", "ActualRemainingGoals", "HomePreviousMatches", "AwayPreviousMatches", "MatchTeamVolumeFactor"
        })
        {
            if (!index.ContainsKey(required))
                throw new ArgumentException($"Input CSV is missing required column '{required}'. Rebuild the calibration dataset with the latest builder.");
        }

        var rows = new List<InputRow>();
        foreach (List<string> record in records.Skip(1))
        {
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0]))
                continue;

            if (!TryGetInt(record, index, "SofaScoreSeasonId", out int seasonId) ||
                !TryGetInt(record, index, "MatchId", out int matchId) ||
                !TryGetInt(record, index, "Minute", out int minute) ||
                !TryGetInt(record, index, "HomeGoals", out int homeGoals) ||
                !TryGetInt(record, index, "AwayGoals", out int awayGoals) ||
                !TryGetDouble(record, index, "TimingRemainingShare", out double timingRemainingShare) ||
                !TryGetDouble(record, index, "ActualRemainingGoals", out double actualRemainingGoals) ||
                !TryGetInt(record, index, "HomePreviousMatches", out int homePreviousMatches) ||
                !TryGetInt(record, index, "AwayPreviousMatches", out int awayPreviousMatches) ||
                !TryGetDouble(record, index, "MatchTeamVolumeFactor", out double matchTeamVolumeFactor))
                continue;

            rows.Add(new InputRow
            {
                SofaScoreSeasonId = seasonId,
                MatchId = matchId,
                StateTrigger = LiveTotalStateTrigger.Normalize(GetString(record, index, "StateTrigger")),
                Minute = minute,
                HomeGoals = homeGoals,
                AwayGoals = awayGoals,
                TimingRemainingShare = timingRemainingShare,
                ActualRemainingGoals = actualRemainingGoals,
                HomePreviousMatches = homePreviousMatches,
                AwayPreviousMatches = awayPreviousMatches,
                MatchTeamVolumeFactor = matchTeamVolumeFactor
            });
        }

        return rows;
    }

    private static string ToCsv(IReadOnlyCollection<LiveTotalModelEvaluationSummary> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("StateTrigger,Rows,Matches,BaselineMae,StateCorrectedMae,StatePlusTeamMae,BaselineBias,StateCorrectedBias,StatePlusTeamBias,BaselineRmse,StateCorrectedRmse,StatePlusTeamRmse,AverageTeamVolumeFactor");
        foreach (LiveTotalModelEvaluationSummary row in rows)
        {
            sb.AppendLine(string.Join(',',
                EscapeCsv(row.StateTrigger),
                row.Rows.ToString(CultureInfo.InvariantCulture),
                row.Matches.ToString(CultureInfo.InvariantCulture),
                D(row.BaselineMae),
                D(row.StateCorrectedMae),
                D(row.StatePlusTeamMae),
                D(row.BaselineBias),
                D(row.StateCorrectedBias),
                D(row.StatePlusTeamBias),
                D(row.BaselineRmse),
                D(row.StateCorrectedRmse),
                D(row.StatePlusTeamRmse),
                D(row.AverageTeamVolumeFactor)));
        }

        return sb.ToString();
    }

    private static double Squared(double value) => value * value;

    private static int TriggerOrder(string trigger) => trigger switch
    {
        "All" => 0,
        LiveTotalStateTrigger.FixedMinute => 1,
        LiveTotalStateTrigger.AfterGoal => 2,
        LiveTotalStateTrigger.AfterRedCard => 3,
        _ => 99
    };

    private static string D(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
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

    private sealed class InputRow
    {
        public int SofaScoreSeasonId { get; set; }
        public int MatchId { get; set; }
        public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
        public int Minute { get; set; }
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }
        public double TimingRemainingShare { get; set; }
        public double ActualRemainingGoals { get; set; }
        public int HomePreviousMatches { get; set; }
        public int AwayPreviousMatches { get; set; }
        public double MatchTeamVolumeFactor { get; set; } = 1.0;
    }

    private sealed class Observation
    {
        public InputRow Row { get; set; } = new();
        public double Baseline { get; set; }
        public double StateCorrected { get; set; }
        public double StatePlusTeam { get; set; }
    }
}
