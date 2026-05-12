using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveTotalsHelper.Tools;

public static class LiveTotalStateTrigger
{
    public const string FixedMinute = "FixedMinute";
    public const string AfterGoal = "AfterGoal";
    public const string AfterRedCard = "AfterRedCard";

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return FixedMinute;

        if (value.Equals(FixedMinute, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("fixed-minute", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("fixed", StringComparison.OrdinalIgnoreCase))
            return FixedMinute;

        if (value.Equals(AfterGoal, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("after-goal", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("goal", StringComparison.OrdinalIgnoreCase))
            return AfterGoal;

        if (value.Equals(AfterRedCard, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("after-red-card", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("red-card", StringComparison.OrdinalIgnoreCase))
            return AfterRedCard;

        throw new ArgumentException($"Unknown state trigger '{value}'. Use fixed-minute, after-goal, or after-red-card.");
    }
}

public sealed class LiveTotalStateCorrectionFitOptions
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public List<int> TrainingSeasonIds { get; } = [];
    public int MinBucketMatches { get; set; } = 100;
    public int AfterGoalMinBucketMatches { get; set; } = 60;
    public double MinFactor { get; set; } = 0.50;
    public double MaxFactor { get; set; } = 2.50;
}

public sealed class LiveTotalStateCorrectionFitResult
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public int RowsRead { get; set; }
    public int TrainingRowsUsed { get; set; }
    public int TrainingMatchesUsed { get; set; }
    public double LeagueAverageFinalGoals { get; set; }
    public List<int> TrainingSeasonIds { get; } = [];
    public List<LiveTotalStateCorrectionBucket> Buckets { get; } = [];
}

public sealed class LiveTotalStateCorrectionFile
{
    public string ModelType { get; set; } = "live-total-state-correction";
    public string League { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public List<int> TrainingSeasonIds { get; set; } = [];
    public double LeagueAverageFinalGoals { get; set; }
    public int MinBucketMatches { get; set; }
    public int AfterGoalMinBucketMatches { get; set; }
    public double MinFactor { get; set; }
    public double MaxFactor { get; set; }
    public List<LiveTotalStateCorrectionBucket> Buckets { get; set; } = [];
}

public sealed class LiveTotalStateCorrectionBucket
{
    public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
    public string MinuteBand { get; set; } = string.Empty;
    public string DetailedScoreState { get; set; } = string.Empty;
    public string GoalChangeType { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double ActualRemainingGoalsPerRow { get; set; }
    public double AverageTimingRemainingShare { get; set; }
    public double BaselineRemainingGoalsPerRow { get; set; }
    public double RawFactor { get; set; }
    public double Factor { get; set; }
    public bool IsUsable { get; set; }
}

public sealed class LiveTotalStateCorrectionResolution
{
    public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
    public string DetailedScoreState { get; set; } = string.Empty;
    public string MinuteBand { get; set; } = string.Empty;
    public string GoalChangeType { get; set; } = string.Empty;
    public double Factor { get; set; } = 1.0;
    public bool IsSupported { get; set; }
    public string Source { get; set; } = "unsupported - no exact usable bucket";
}

public static class LiveTotalStateCorrectionResolver
{
    public static string DetailedScoreState(int homeGoals, int awayGoals)
    {
        if (homeGoals == 0 && awayGoals == 0) return "NilNil";
        if (homeGoals == awayGoals) return "LevelWithGoals";
        int margin = Math.Abs(homeGoals - awayGoals);
        return margin switch
        {
            1 => "OneGoalMargin",
            2 => "TwoGoalMargin",
            _ => "ThreePlusGoalMargin"
        };
    }

    public static string MinuteBand(string stateTrigger, int minute)
    {
        stateTrigger = LiveTotalStateTrigger.Normalize(stateTrigger);
        if (stateTrigger.Equals(LiveTotalStateTrigger.FixedMinute, StringComparison.OrdinalIgnoreCase))
        {
            return minute switch
            {
                >= 10 and <= 20 => "10-20",
                >= 25 and <= 35 => "25-35",
                >= 40 and <= 50 => "40-50",
                >= 55 and <= 65 => "55-65",
                >= 70 and <= 85 => "70-85",
                _ => string.Empty
            };
        }

        return minute switch
        {
            >= 1 and <= 20 => "1-20",
            >= 21 and <= 35 => "21-35",
            >= 36 and <= 50 => "36-50",
            >= 51 and <= 65 => "51-65",
            >= 66 and <= 90 => "66-90",
            _ => string.Empty
        };
    }

    public static string MinuteBand(int minute) => MinuteBand(LiveTotalStateTrigger.FixedMinute, minute);

    public static LiveTotalStateCorrectionResolution Resolve(LiveTotalStateCorrectionFile model, string stateTrigger, int minute, int homeGoals, int awayGoals, string goalChangeType = "")
    {
        stateTrigger = LiveTotalStateTrigger.Normalize(stateTrigger);
        string detailedScoreState = DetailedScoreState(homeGoals, awayGoals);
        string minuteBand = MinuteBand(stateTrigger, minute);
        string normalizedGoalChangeType = LiveTotalStateTrigger.Normalize(stateTrigger).Equals(LiveTotalStateTrigger.AfterGoal, StringComparison.OrdinalIgnoreCase)
            ? LiveTotalGoalChangeClassifier.Normalize(goalChangeType)
            : LiveTotalGoalChangeClassifier.None;

        if (!string.IsNullOrWhiteSpace(minuteBand))
        {
            LiveTotalStateCorrectionBucket? bucket = model.Buckets.FirstOrDefault(x =>
                x.IsUsable &&
                LiveTotalStateTrigger.Normalize(x.StateTrigger).Equals(stateTrigger, StringComparison.OrdinalIgnoreCase) &&
                x.MinuteBand.Equals(minuteBand, StringComparison.OrdinalIgnoreCase) &&
                x.DetailedScoreState.Equals(detailedScoreState, StringComparison.OrdinalIgnoreCase) &&
                LiveTotalGoalChangeClassifier.Normalize(x.GoalChangeType).Equals(normalizedGoalChangeType, StringComparison.OrdinalIgnoreCase));

            if (bucket is not null)
            {
                return new LiveTotalStateCorrectionResolution
                {
                    StateTrigger = stateTrigger,
                    DetailedScoreState = detailedScoreState,
                    MinuteBand = minuteBand,
                    GoalChangeType = normalizedGoalChangeType,
                    Factor = bucket.Factor,
                    IsSupported = true,
                    Source = string.IsNullOrWhiteSpace(normalizedGoalChangeType)
                        ? $"bucket {stateTrigger}/{minuteBand}/{detailedScoreState}"
                        : $"bucket {stateTrigger}/{minuteBand}/{detailedScoreState}/{normalizedGoalChangeType}"
                };
            }
        }

        return new LiveTotalStateCorrectionResolution
        {
            StateTrigger = stateTrigger,
            DetailedScoreState = detailedScoreState,
            MinuteBand = minuteBand,
            GoalChangeType = normalizedGoalChangeType,
            Factor = 1.0,
            IsSupported = false,
            Source = string.IsNullOrWhiteSpace(minuteBand)
                ? $"unsupported - {stateTrigger} minute is outside betting bands"
                : string.IsNullOrWhiteSpace(normalizedGoalChangeType)
                    ? $"unsupported sparse bucket {stateTrigger}/{minuteBand}/{detailedScoreState}"
                    : $"unsupported sparse bucket {stateTrigger}/{minuteBand}/{detailedScoreState}/{normalizedGoalChangeType}"
        };
    }
}

public sealed class LiveTotalStateCorrectionFitter
{
    private static readonly string[] ScoreStateOrder =
    [
        "NilNil",
        "LevelWithGoals",
        "OneGoalMargin",
        "TwoGoalMargin",
        "ThreePlusGoalMargin"
    ];

    private readonly LiveTotalStateCorrectionFitOptions _options;

    public LiveTotalStateCorrectionFitter(LiveTotalStateCorrectionFitOptions options)
    {
        _options = options;
    }

    public async Task<LiveTotalStateCorrectionFitResult> FitAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        List<InputRow> rows = await ReadRowsAsync(_options.InputPath, cancellationToken);
        List<InputRow> trainingRows = rows
            .Where(x => _options.TrainingSeasonIds.Contains(x.SofaScoreSeasonId))
            .ToList();

        if (trainingRows.Count == 0)
            throw new ArgumentException("No rows matched --training-season-ids.");

        double leagueAverageFinalGoals = trainingRows
            .GroupBy(x => x.MatchId)
            .Select(x => x.First().ActualFinalTotalGoals)
            .Average();

        string league = trainingRows.Select(x => x.LeagueName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        string outputPath = ResolveOutputPath();

        var result = new LiveTotalStateCorrectionFitResult
        {
            InputPath = _options.InputPath,
            OutputPath = outputPath,
            League = league,
            RowsRead = rows.Count,
            TrainingRowsUsed = trainingRows.Count,
            TrainingMatchesUsed = trainingRows.Select(x => x.MatchId).Distinct().Count(),
            LeagueAverageFinalGoals = leagueAverageFinalGoals
        };
        result.TrainingSeasonIds.AddRange(_options.TrainingSeasonIds.OrderBy(x => x));

        result.Buckets.AddRange(BuildBuckets(trainingRows, leagueAverageFinalGoals));

        var modelFile = new LiveTotalStateCorrectionFile
        {
            League = result.League,
            CreatedAtUtc = DateTime.UtcNow,
            TrainingSeasonIds = result.TrainingSeasonIds.ToList(),
            LeagueAverageFinalGoals = result.LeagueAverageFinalGoals,
            MinBucketMatches = _options.MinBucketMatches,
            AfterGoalMinBucketMatches = _options.AfterGoalMinBucketMatches,
            MinFactor = _options.MinFactor,
            MaxFactor = _options.MaxFactor,
            Buckets = result.Buckets.ToList()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(modelFile, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        }), Encoding.UTF8, cancellationToken);

        return result;
    }

    private List<LiveTotalStateCorrectionBucket> BuildBuckets(IReadOnlyCollection<InputRow> trainingRows, double leagueAverageFinalGoals)
    {
        return trainingRows
            .Select(x => new { Row = x, MinuteBand = LiveTotalStateCorrectionResolver.MinuteBand(x.StateTrigger, x.Minute) })
            .Where(x => !string.IsNullOrWhiteSpace(x.MinuteBand))
            .GroupBy(x => new { x.Row.StateTrigger, x.MinuteBand, x.Row.DetailedScoreState, GoalChangeType = CorrectionGoalChangeType(x.Row.StateTrigger, x.Row.GoalChangeType) })
            .OrderBy(x => TriggerOrder(x.Key.StateTrigger))
            .ThenBy(x => MinuteBandOrder(x.Key.MinuteBand))
            .ThenBy(x => ScoreStateIndex(x.Key.DetailedScoreState))
            .ThenBy(x => GoalChangeTypeOrder(x.Key.GoalChangeType))
            .Select(group =>
            {
                List<InputRow> bucketRows = group.Select(x => x.Row).ToList();
                int matches = bucketRows.Select(x => x.MatchId).Distinct().Count();
                double actual = bucketRows.Average(x => x.ActualRemainingGoals);
                double avgTiming = bucketRows.Average(x => x.TimingRemainingShare);
                double baseline = leagueAverageFinalGoals * avgTiming;
                double raw = baseline > 0 ? actual / baseline : 1.0;
                return new LiveTotalStateCorrectionBucket
                {
                    StateTrigger = group.Key.StateTrigger,
                    MinuteBand = group.Key.MinuteBand,
                    DetailedScoreState = group.Key.DetailedScoreState,
                    GoalChangeType = group.Key.GoalChangeType,
                    Rows = bucketRows.Count,
                    Matches = matches,
                    ActualRemainingGoalsPerRow = actual,
                    AverageTimingRemainingShare = avgTiming,
                    BaselineRemainingGoalsPerRow = baseline,
                    RawFactor = raw,
                    Factor = ClampFactor(raw),
                    IsUsable = matches >= RequiredMatches(group.Key.StateTrigger) && baseline > 0
                };
            })
            .ToList();
    }

    private int RequiredMatches(string stateTrigger)
    {
        return LiveTotalStateTrigger.Normalize(stateTrigger).Equals(LiveTotalStateTrigger.AfterGoal, StringComparison.OrdinalIgnoreCase)
            ? _options.AfterGoalMinBucketMatches
            : _options.MinBucketMatches;
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
        if (_options.TrainingSeasonIds.Count == 0)
            throw new ArgumentException("Missing required argument --training-season-ids.");
        if (_options.MinBucketMatches < 1)
            throw new ArgumentException("--min-bucket-matches must be >= 1.");
        if (_options.AfterGoalMinBucketMatches < 1)
            throw new ArgumentException("--after-goal-min-bucket-matches must be >= 1.");
        if (_options.MinFactor <= 0 || _options.MaxFactor <= 0 || _options.MinFactor > _options.MaxFactor)
            throw new ArgumentException("--min-factor and --max-factor must be positive and min <= max.");
    }

    private string ResolveOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
            return _options.OutputPath;

        string directory = Path.GetDirectoryName(_options.InputPath) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(_options.InputPath);
        return Path.Combine(directory, $"{fileName}-state-correction.json");
    }

    private double ClampFactor(double factor) => Math.Clamp(factor, _options.MinFactor, _options.MaxFactor);

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

    private static async Task<List<InputRow>> ReadRowsAsync(string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        List<List<string>> records = ParseCsv(text);
        if (records.Count == 0)
            return [];

        string[] headers = records[0].Select(x => x.Trim()).ToArray();
        var index = headers.Select((name, position) => new { name, position })
            .ToDictionary(x => x.name, x => x.position, StringComparer.OrdinalIgnoreCase);

        foreach (string required in new[] { "LeagueName", "SofaScoreSeasonId", "MatchId", "Minute", "DetailedScoreState", "TimingRemainingShare", "ActualFinalTotalGoals", "ActualRemainingGoals" })
        {
            if (!index.ContainsKey(required))
                throw new ArgumentException($"Input CSV is missing required column '{required}'.");
        }

        var rows = new List<InputRow>();
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

            rows.Add(new InputRow
            {
                StateTrigger = index.ContainsKey("StateTrigger")
                    ? LiveTotalStateTrigger.Normalize(GetString(record, index, "StateTrigger"))
                    : LiveTotalStateTrigger.FixedMinute,
                LeagueName = GetString(record, index, "LeagueName"),
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
        public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
        public string LeagueName { get; set; } = string.Empty;
        public int SofaScoreSeasonId { get; set; }
        public int MatchId { get; set; }
        public int Minute { get; set; }
        public string DetailedScoreState { get; set; } = string.Empty;
        public string GoalChangeType { get; set; } = string.Empty;
        public double TimingRemainingShare { get; set; }
        public double ActualFinalTotalGoals { get; set; }
        public double ActualRemainingGoals { get; set; }
    }
}
