using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalEmpiricalSettlementFitOptions
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public List<int> TrainingSeasonIds { get; } = [];
    public int MinBucketRows { get; set; } = 80;
    public int MinBucketMatches { get; set; } = 40;
    public int MaxRemainingGoals { get; set; } = 8;
    public double Smoothing { get; set; } = 0.25;
}

public sealed class LiveTotalEmpiricalSettlementFitResult
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public int RowsRead { get; set; }
    public int TrainingRowsUsed { get; set; }
    public int TrainingMatchesUsed { get; set; }
    public List<int> TrainingSeasonIds { get; } = [];
    public List<LiveTotalEmpiricalSettlementBucket> Buckets { get; } = [];
}

public sealed class LiveTotalEmpiricalSettlementFile
{
    public string ModelType { get; set; } = "live-total-empirical-remaining-goals-settlement";
    public string League { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<int> TrainingSeasonIds { get; set; } = [];
    public int MinBucketRows { get; set; }
    public int MinBucketMatches { get; set; }
    public int MaxRemainingGoals { get; set; }
    public double Smoothing { get; set; }
    public List<LiveTotalEmpiricalSettlementBucket> Buckets { get; set; } = [];
}

public sealed class LiveTotalEmpiricalSettlementBucket
{
    public string BucketLevel { get; set; } = string.Empty;
    public string StateTrigger { get; set; } = string.Empty;
    public string MinuteBand { get; set; } = string.Empty;
    public string DetailedScoreState { get; set; } = string.Empty;
    public int? CurrentTotalGoals { get; set; }
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double AverageRemainingGoals { get; set; }
    public bool IsUsable { get; set; }
    public List<LiveTotalEmpiricalRemainingGoalProbability> Probabilities { get; set; } = [];
}

public sealed class LiveTotalEmpiricalRemainingGoalProbability
{
    public int RemainingGoals { get; set; }
    public int Count { get; set; }
    public double Probability { get; set; }
}

public sealed class LiveTotalEmpiricalSettlementResolution
{
    public bool IsSupported { get; set; }
    public string Source { get; set; } = "empirical settlement unavailable";
    public LiveTotalEmpiricalSettlementBucket? Bucket { get; set; }
    public IReadOnlyDictionary<int, double> Probabilities { get; set; } = new Dictionary<int, double>();
}

public static class LiveTotalEmpiricalSettlementResolver
{
    public static LiveTotalEmpiricalSettlementResolution Resolve(
        LiveTotalEmpiricalSettlementFile model,
        string stateTrigger,
        int minute,
        int homeGoals,
        int awayGoals)
    {
        stateTrigger = LiveTotalStateTrigger.Normalize(stateTrigger);
        string minuteBand = LiveTotalStateCorrectionResolver.MinuteBand(stateTrigger, minute);
        string detailedScoreState = LiveTotalStateCorrectionResolver.DetailedScoreState(homeGoals, awayGoals);
        int currentTotal = homeGoals + awayGoals;

        if (string.IsNullOrWhiteSpace(minuteBand))
            return new LiveTotalEmpiricalSettlementResolution { Source = $"unsupported - {stateTrigger} minute is outside settlement bands" };

        LiveTotalEmpiricalSettlementBucket? bucket =
            Find(model, "Exact", stateTrigger, minuteBand, detailedScoreState, currentTotal) ??
            Find(model, "ScoreState", stateTrigger, minuteBand, detailedScoreState, null) ??
            Find(model, "TriggerBand", stateTrigger, minuteBand, string.Empty, null) ??
            Find(model, "Trigger", stateTrigger, string.Empty, string.Empty, null) ??
            Find(model, "Global", string.Empty, string.Empty, string.Empty, null);

        if (bucket is null)
            return new LiveTotalEmpiricalSettlementResolution
            {
                Source = $"unsupported sparse empirical settlement bucket {stateTrigger}/{minuteBand}/{detailedScoreState}/total={currentTotal}"
            };

        return new LiveTotalEmpiricalSettlementResolution
        {
            IsSupported = true,
            Bucket = bucket,
            Source = $"empirical {bucket.BucketLevel} bucket {Describe(bucket)} ({bucket.Rows} rows, {bucket.Matches} matches)",
            Probabilities = bucket.Probabilities.ToDictionary(x => x.RemainingGoals, x => x.Probability)
        };
    }

    private static LiveTotalEmpiricalSettlementBucket? Find(
        LiveTotalEmpiricalSettlementFile model,
        string level,
        string stateTrigger,
        string minuteBand,
        string detailedScoreState,
        int? currentTotalGoals)
    {
        return model.Buckets.FirstOrDefault(x =>
            x.IsUsable &&
            x.BucketLevel.Equals(level, StringComparison.OrdinalIgnoreCase) &&
            Matches(x.StateTrigger, stateTrigger) &&
            x.MinuteBand.Equals(minuteBand, StringComparison.OrdinalIgnoreCase) &&
            x.DetailedScoreState.Equals(detailedScoreState, StringComparison.OrdinalIgnoreCase) &&
            x.CurrentTotalGoals == currentTotalGoals);
    }

    private static bool Matches(string bucketValue, string requestedValue)
    {
        if (string.IsNullOrWhiteSpace(bucketValue))
            return string.IsNullOrWhiteSpace(requestedValue);

        return LiveTotalStateTrigger.Normalize(bucketValue).Equals(requestedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static string Describe(LiveTotalEmpiricalSettlementBucket bucket)
    {
        var parts = new[] { bucket.StateTrigger, bucket.MinuteBand, bucket.DetailedScoreState, bucket.CurrentTotalGoals?.ToString(CultureInfo.InvariantCulture) ?? string.Empty }
            .Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join("/", parts);
    }
}

public sealed class LiveTotalEmpiricalSettlementFitter
{
    private readonly LiveTotalEmpiricalSettlementFitOptions _options;

    public LiveTotalEmpiricalSettlementFitter(LiveTotalEmpiricalSettlementFitOptions options)
    {
        _options = options;
    }

    public async Task<LiveTotalEmpiricalSettlementFitResult> FitAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        List<InputRow> rows = await ReadRowsAsync(_options.InputPath, cancellationToken);
        List<InputRow> trainingRows = rows
            .Where(x => _options.TrainingSeasonIds.Contains(x.SofaScoreSeasonId))
            .ToList();

        if (trainingRows.Count == 0)
            throw new ArgumentException("No rows matched --training-season-ids.");

        string outputPath = ResolveOutputPath();
        var result = new LiveTotalEmpiricalSettlementFitResult
        {
            InputPath = _options.InputPath,
            OutputPath = outputPath,
            League = trainingRows.Select(x => x.LeagueName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty,
            RowsRead = rows.Count,
            TrainingRowsUsed = trainingRows.Count,
            TrainingMatchesUsed = trainingRows.Select(x => x.MatchId).Distinct().Count()
        };
        result.TrainingSeasonIds.AddRange(_options.TrainingSeasonIds.OrderBy(x => x));
        result.Buckets.AddRange(BuildBuckets(trainingRows));

        var model = new LiveTotalEmpiricalSettlementFile
        {
            League = result.League,
            CreatedAtUtc = DateTime.UtcNow,
            TrainingSeasonIds = result.TrainingSeasonIds.ToList(),
            MinBucketRows = _options.MinBucketRows,
            MinBucketMatches = _options.MinBucketMatches,
            MaxRemainingGoals = _options.MaxRemainingGoals,
            Smoothing = _options.Smoothing,
            Buckets = result.Buckets.ToList()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(model, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        }), Encoding.UTF8, cancellationToken);

        return result;
    }

    private IEnumerable<LiveTotalEmpiricalSettlementBucket> BuildBuckets(IReadOnlyCollection<InputRow> rows)
    {
        var bandedRows = rows
            .Select(x => new { Row = x, MinuteBand = LiveTotalStateCorrectionResolver.MinuteBand(x.StateTrigger, x.Minute) })
            .Where(x => !string.IsNullOrWhiteSpace(x.MinuteBand))
            .ToList();

        foreach (IGrouping<object, InputRow> group in bandedRows
            .GroupBy(x => new { x.Row.StateTrigger, x.MinuteBand, x.Row.DetailedScoreState, x.Row.CurrentTotalGoals }, x => x.Row))
        {
            dynamic key = group.Key;
            yield return BuildBucket("Exact", key.StateTrigger, key.MinuteBand, key.DetailedScoreState, key.CurrentTotalGoals, group.ToList());
        }

        foreach (IGrouping<object, InputRow> group in bandedRows
            .GroupBy(x => new { x.Row.StateTrigger, x.MinuteBand, x.Row.DetailedScoreState }, x => x.Row))
        {
            dynamic key = group.Key;
            yield return BuildBucket("ScoreState", key.StateTrigger, key.MinuteBand, key.DetailedScoreState, null, group.ToList());
        }

        foreach (IGrouping<object, InputRow> group in bandedRows
            .GroupBy(x => new { x.Row.StateTrigger, x.MinuteBand }, x => x.Row))
        {
            dynamic key = group.Key;
            yield return BuildBucket("TriggerBand", key.StateTrigger, key.MinuteBand, string.Empty, null, group.ToList());
        }

        foreach (IGrouping<string, InputRow> group in bandedRows.GroupBy(x => x.Row.StateTrigger, x => x.Row))
            yield return BuildBucket("Trigger", group.Key, string.Empty, string.Empty, null, group.ToList());

        yield return BuildBucket("Global", string.Empty, string.Empty, string.Empty, null, bandedRows.Select(x => x.Row).ToList());
    }

    private LiveTotalEmpiricalSettlementBucket BuildBucket(
        string level,
        string stateTrigger,
        string minuteBand,
        string detailedScoreState,
        int? currentTotalGoals,
        IReadOnlyCollection<InputRow> rows)
    {
        int matches = rows.Select(x => x.MatchId).Distinct().Count();
        Dictionary<int, int> counts = rows
            .GroupBy(x => Math.Clamp(x.ActualRemainingGoals, 0, _options.MaxRemainingGoals))
            .ToDictionary(x => x.Key, x => x.Count());

        double denominator = rows.Count + (_options.MaxRemainingGoals + 1) * _options.Smoothing;
        var probabilities = new List<LiveTotalEmpiricalRemainingGoalProbability>();
        for (int goals = 0; goals <= _options.MaxRemainingGoals; goals++)
        {
            counts.TryGetValue(goals, out int count);
            probabilities.Add(new LiveTotalEmpiricalRemainingGoalProbability
            {
                RemainingGoals = goals,
                Count = count,
                Probability = denominator > 0 ? (count + _options.Smoothing) / denominator : 0.0
            });
        }

        return new LiveTotalEmpiricalSettlementBucket
        {
            BucketLevel = level,
            StateTrigger = stateTrigger,
            MinuteBand = minuteBand,
            DetailedScoreState = detailedScoreState,
            CurrentTotalGoals = currentTotalGoals,
            Rows = rows.Count,
            Matches = matches,
            AverageRemainingGoals = rows.Average(x => x.ActualRemainingGoals),
            IsUsable = rows.Count >= _options.MinBucketRows && matches >= _options.MinBucketMatches,
            Probabilities = probabilities
        };
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.InputPath))
            throw new ArgumentException("Missing required argument --input.");
        if (!File.Exists(_options.InputPath))
            throw new FileNotFoundException("Live total calibration dataset CSV was not found.", _options.InputPath);
        if (_options.TrainingSeasonIds.Count == 0)
            throw new ArgumentException("Missing required argument --training-season-ids.");
        if (_options.MinBucketRows < 1)
            throw new ArgumentException("--min-bucket-rows must be >= 1.");
        if (_options.MinBucketMatches < 1)
            throw new ArgumentException("--min-bucket-matches must be >= 1.");
        if (_options.MaxRemainingGoals < 1)
            throw new ArgumentException("--max-remaining-goals must be >= 1.");
        if (_options.Smoothing < 0)
            throw new ArgumentException("--smoothing must be >= 0.");
    }

    private string ResolveOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
            return _options.OutputPath;

        string directory = Path.GetDirectoryName(_options.InputPath) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(_options.InputPath);
        return Path.Combine(directory, $"{fileName}-empirical-settlement.json");
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

        foreach (string required in new[] { "LeagueName", "SofaScoreSeasonId", "MatchId", "StateTrigger", "Minute", "DetailedScoreState", "CurrentTotalGoals", "ActualRemainingGoals" })
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
                !TryGetInt(record, index, "CurrentTotalGoals", out int currentTotalGoals) ||
                !TryGetInt(record, index, "ActualRemainingGoals", out int actualRemainingGoals))
                continue;

            rows.Add(new InputRow
            {
                LeagueName = GetString(record, index, "LeagueName"),
                SofaScoreSeasonId = seasonId,
                MatchId = matchId,
                StateTrigger = LiveTotalStateTrigger.Normalize(GetString(record, index, "StateTrigger")),
                Minute = minute,
                DetailedScoreState = GetString(record, index, "DetailedScoreState"),
                CurrentTotalGoals = currentTotalGoals,
                ActualRemainingGoals = actualRemainingGoals
            });
        }

        return rows;
    }

    private static bool TryGetInt(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column, out int value)
    {
        value = 0;
        return index.TryGetValue(column, out int position) &&
               position < record.Count &&
               int.TryParse(record[position], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
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
        public string LeagueName { get; set; } = string.Empty;
        public int SofaScoreSeasonId { get; set; }
        public int MatchId { get; set; }
        public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
        public int Minute { get; set; }
        public string DetailedScoreState { get; set; } = string.Empty;
        public int CurrentTotalGoals { get; set; }
        public int ActualRemainingGoals { get; set; }
    }
}
