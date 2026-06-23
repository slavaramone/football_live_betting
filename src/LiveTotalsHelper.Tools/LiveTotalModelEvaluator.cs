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
    public string DecisionScope { get; set; } = LiveTotalDecisionScope.FullModel;
    public string StateCorrectionScope { get; set; } = LiveTotalStateCorrectionScope.FixedMinute;
    public string StateCorrectionDirectionGuard { get; set; } = LiveTotalStateCorrectionDirectionGuard.UpOnly;
    public LiveTotalLateGameCorrectionOptions LateGameCorrection { get; set; } = LiveTotalLateGameCorrectionOptions.Disabled();
    public bool CompareScopes { get; set; }
}

public sealed class LiveTotalModelEvaluationResult
{
    public string InputPath { get; set; } = string.Empty;
    public string StateCorrectionPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public int RowsRead { get; set; }
    public int TestRows { get; set; }
    public int RowsSkippedMissingExpectedFinalGoals { get; set; }
    public int SupportedRows { get; set; }
    public int StateCorrectionAppliedRows { get; set; }
    public int StateCorrectionGatedRows { get; set; }
    public int LateGameBoostedRows { get; set; }
    public string StateCorrectionScope { get; set; } = LiveTotalStateCorrectionScope.FixedMinute;
    public string StateCorrectionDirectionGuard { get; set; } = LiveTotalStateCorrectionDirectionGuard.UpOnly;
    public string LateGameCorrectionSummary { get; set; } = string.Empty;
    public List<string> ScopesEvaluated { get; } = [];
    public List<LiveTotalModelEvaluationSummary> Summaries { get; } = [];
}

public sealed class LiveTotalModelEvaluationSummary
{
    public string Scope { get; set; } = LiveTotalDecisionScope.FullModel;
    public string StateTrigger { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double BaselineMae { get; set; }
    public double StateCorrectedMae { get; set; }
    public double BaselineBias { get; set; }
    public double StateCorrectedBias { get; set; }
    public double BaselineRmse { get; set; }
    public double StateCorrectedRmse { get; set; }
    public int StateCorrectionAppliedRows { get; set; }
    public int StateCorrectionGatedRows { get; set; }
    public int LateGameBoostedRows { get; set; }
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
            .Where(x => _options.TestSeasonIds.Contains(x.SeasonId))
            .ToList();

        string[] scopes = _options.CompareScopes
            ? LiveTotalDecisionScope.ComparisonScopes
            : [LiveTotalDecisionScope.Normalize(_options.DecisionScope)];

        var observations = new List<Observation>();
        int rowsSkippedMissingExpectedFinalGoals = 0;
        foreach (InputRow row in testRows)
        {
            if (!row.ExpectedFinalGoals.HasValue || row.ExpectedFinalGoals.Value <= 0.0)
            {
                rowsSkippedMissingExpectedFinalGoals++;
                continue;
            }

            LiveTotalStateCorrectionResolution resolved = LiveTotalStateCorrectionGate.Resolve(
                correction,
                _options.StateCorrectionScope,
                _options.StateCorrectionDirectionGuard,
                _options.LateGameCorrection,
                row.StateTrigger,
                row.Minute,
                row.HomeGoals,
                row.AwayGoals);

            if (!resolved.IsSupported)
                continue;

            double baseline = row.ExpectedFinalGoals.Value * row.TimingRemainingShare;
            double stateCorrected = baseline * resolved.Factor;

            foreach (string scope in scopes)
            {
                if (!LiveTotalDecisionScope.IsEligible(scope, row.StateTrigger, row.Minute))
                    continue;

                observations.Add(new Observation
                {
                    Scope = scope,
                    Row = row,
                    Baseline = baseline,
                    StateCorrected = stateCorrected,
                    StateCorrectionApplied = LiveTotalStateCorrectionGate.IsApplied(resolved),
                    StateCorrectionGated = LiveTotalStateCorrectionGate.IsGatedOut(resolved),
                    LateGameBoosted = LiveTotalStateCorrectionGate.IsLateGameBoosted(resolved)
                });
            }
        }

        var result = new LiveTotalModelEvaluationResult
        {
            InputPath = _options.InputPath,
            StateCorrectionPath = _options.StateCorrectionPath,
            OutputPath = ResolveOutputPath(),
            RowsRead = rows.Count,
            TestRows = testRows.Count,
            RowsSkippedMissingExpectedFinalGoals = rowsSkippedMissingExpectedFinalGoals,
            SupportedRows = observations.Count,
            StateCorrectionAppliedRows = observations.Count(x => x.StateCorrectionApplied),
            StateCorrectionGatedRows = observations.Count(x => x.StateCorrectionGated),
            LateGameBoostedRows = observations.Count(x => x.LateGameBoosted),
            StateCorrectionScope = LiveTotalStateCorrectionScope.Normalize(_options.StateCorrectionScope),
            StateCorrectionDirectionGuard = LiveTotalStateCorrectionDirectionGuard.Normalize(_options.StateCorrectionDirectionGuard),
            LateGameCorrectionSummary = _options.LateGameCorrection.Summary()
        };
        result.ScopesEvaluated.AddRange(scopes);

        foreach (IGrouping<string, Observation> scopeGroup in observations
            .GroupBy(x => x.Scope)
            .OrderBy(x => LiveTotalDecisionScope.Order(x.Key)))
        {
            List<Observation> scoped = scopeGroup.ToList();
            result.Summaries.Add(BuildSummary(scopeGroup.Key, "All", scoped));
            foreach (IGrouping<string, Observation> group in scoped
                .GroupBy(x => x.Row.StateTrigger)
                .OrderBy(x => TriggerOrder(x.Key)))
            {
                result.Summaries.Add(BuildSummary(scopeGroup.Key, group.Key, group.ToList()));
            }

            List<Observation> lateFixedMinuteRows = scoped.Where(IsLateFixedMinute).ToList();
            if (lateFixedMinuteRows.Count > 0)
                result.Summaries.Add(BuildSummary(scopeGroup.Key, "FixedMinuteLateGame", lateFixedMinuteRows));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(result.OutputPath)) ?? ".");
        await File.WriteAllTextAsync(result.OutputPath, ToCsv(result.Summaries), Encoding.UTF8, cancellationToken);
        return result;
    }

    private LiveTotalModelEvaluationSummary BuildSummary(string scope, string stateTrigger, IReadOnlyCollection<Observation> rows)
    {
        if (rows.Count == 0)
            return new LiveTotalModelEvaluationSummary { Scope = scope, StateTrigger = stateTrigger };

        return new LiveTotalModelEvaluationSummary
        {
            Scope = scope,
            StateTrigger = stateTrigger,
            Rows = rows.Count,
            Matches = rows.Select(x => x.Row.MatchId).Distinct().Count(),
            BaselineMae = rows.Average(x => Math.Abs(x.Baseline - x.Row.ActualRemainingGoals)),
            StateCorrectedMae = rows.Average(x => Math.Abs(x.StateCorrected - x.Row.ActualRemainingGoals)),
            BaselineBias = rows.Average(x => x.Baseline - x.Row.ActualRemainingGoals),
            StateCorrectedBias = rows.Average(x => x.StateCorrected - x.Row.ActualRemainingGoals),
            BaselineRmse = Math.Sqrt(rows.Average(x => Squared(x.Baseline - x.Row.ActualRemainingGoals))),
            StateCorrectedRmse = Math.Sqrt(rows.Average(x => Squared(x.StateCorrected - x.Row.ActualRemainingGoals))),
            StateCorrectionAppliedRows = rows.Count(x => x.StateCorrectionApplied),
            StateCorrectionGatedRows = rows.Count(x => x.StateCorrectionGated),
            LateGameBoostedRows = rows.Count(x => x.LateGameBoosted)
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
        _ = LiveTotalDecisionScope.Normalize(_options.DecisionScope);
        _ = LiveTotalStateCorrectionScope.Normalize(_options.StateCorrectionScope);
        _ = LiveTotalStateCorrectionDirectionGuard.Normalize(_options.StateCorrectionDirectionGuard);
        _ = _options.LateGameCorrection.Normalized();
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
            "SeasonId", "MatchId", "StateTrigger", "Minute", "HomeGoals", "AwayGoals",
            "TimingRemainingShare", "ExpectedFinalGoals", "ActualRemainingGoals"
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

            if (!TryGetInt(record, index, "SeasonId", out int seasonId) ||
                !TryGetInt(record, index, "MatchId", out int matchId) ||
                !TryGetInt(record, index, "Minute", out int minute) ||
                !TryGetInt(record, index, "HomeGoals", out int homeGoals) ||
                !TryGetInt(record, index, "AwayGoals", out int awayGoals) ||
                !TryGetDouble(record, index, "TimingRemainingShare", out double timingRemainingShare) ||
                !TryGetOptionalDouble(record, index, "ExpectedFinalGoals", out double? expectedFinalGoals) ||
                !TryGetDouble(record, index, "ActualRemainingGoals", out double actualRemainingGoals))
                continue;

            rows.Add(new InputRow
            {
                SeasonId = seasonId,
                MatchId = matchId,
                StateTrigger = LiveTotalStateTrigger.Normalize(GetString(record, index, "StateTrigger")),
                Minute = minute,
                HomeGoals = homeGoals,
                AwayGoals = awayGoals,
                TimingRemainingShare = timingRemainingShare,
                ExpectedFinalGoals = expectedFinalGoals,
                ActualRemainingGoals = actualRemainingGoals
            });
        }

        return rows;
    }

    private static string ToCsv(IReadOnlyCollection<LiveTotalModelEvaluationSummary> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Scope,StateTrigger,Rows,Matches,BaselineMae,StateCorrectedMae,BaselineBias,StateCorrectedBias,BaselineRmse,StateCorrectedRmse,StateCorrectionAppliedRows,StateCorrectionGatedRows,LateGameBoostedRows");
        foreach (LiveTotalModelEvaluationSummary row in rows)
        {
            sb.AppendLine(string.Join(',',
                EscapeCsv(row.Scope),
                EscapeCsv(row.StateTrigger),
                row.Rows.ToString(CultureInfo.InvariantCulture),
                row.Matches.ToString(CultureInfo.InvariantCulture),
                D(row.BaselineMae),
                D(row.StateCorrectedMae),
                D(row.BaselineBias),
                D(row.StateCorrectedBias),
                D(row.BaselineRmse),
                D(row.StateCorrectedRmse),
                row.StateCorrectionAppliedRows.ToString(CultureInfo.InvariantCulture),
                row.StateCorrectionGatedRows.ToString(CultureInfo.InvariantCulture),
                row.LateGameBoostedRows.ToString(CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    private static bool IsLateFixedMinute(Observation observation) =>
        observation.Row.StateTrigger.Equals(LiveTotalStateTrigger.FixedMinute, StringComparison.OrdinalIgnoreCase) &&
        observation.Row.Minute >= _LateGameSummaryStartMinute;

    private const int _LateGameSummaryStartMinute = 70;

    private static double Squared(double value) => value * value;

    private static int TriggerOrder(string trigger) => trigger switch
    {
        "All" => 0,
        LiveTotalStateTrigger.FixedMinute => 1,
        "FixedMinuteLateGame" => 2,
        LiveTotalStateTrigger.AfterGoal => 3,
        LiveTotalStateTrigger.AfterRedCard => 4,
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

    private static bool TryGetOptionalDouble(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column, out double? value)
    {
        value = null;
        if (!index.TryGetValue(column, out int position) || position >= record.Count)
            return false;
        if (string.IsNullOrWhiteSpace(record[position]))
            return true;
        if (double.TryParse(record[position], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            value = parsed;
            return true;
        }
        return false;
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
        public int SeasonId { get; set; }
        public int MatchId { get; set; }
        public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
        public int Minute { get; set; }
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }
        public double TimingRemainingShare { get; set; }
        public double? ExpectedFinalGoals { get; set; }
        public double ActualRemainingGoals { get; set; }
    }

    private sealed class Observation
    {
        public string Scope { get; set; } = LiveTotalDecisionScope.FullModel;
        public InputRow Row { get; set; } = new();
        public double Baseline { get; set; }
        public double StateCorrected { get; set; }
        public bool StateCorrectionApplied { get; set; }
        public bool StateCorrectionGated { get; set; }
        public bool LateGameBoosted { get; set; }
    }
}
