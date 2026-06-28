using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Core.MonteCarlo;

namespace LiveTotalsHelper.Tools;

public sealed class NextGoalSideModelFitterOptions
{
    public string InputPath { get; init; } = "outputs/calibration/state-weibull-exposures.csv";
    public string OutputPath { get; init; } = "outputs/calibration/next-goal-side-model.json";
    public string SummaryPath { get; init; } = "outputs/calibration/next-goal-side-summary.csv";
    public string League { get; init; } = string.Empty;
    public int MinExactGoals { get; init; } = 25;
    public int MinDirectionalOverallGoals { get; init; } = 50;
    public int MinPressureTimeGoals { get; init; } = 40;
    public int MinNeutralScoreTimeGoals { get; init; } = 25;
    public int MinTimeGoals { get; init; } = 50;
    public int MinLeagueGoals { get; init; } = 100;
    public double PriorWeightGoals { get; init; } = 6.0;
}

public sealed class NextGoalSideModelFitResult
{
    public int ExposureRowsRead { get; init; }
    public int GoalRowsRead { get; init; }
    public int EstimatesWritten { get; init; }
    public int ExactSupported { get; init; }
    public int DirectionalFallback { get; init; }
    public int PressureTimeFallback { get; init; }
    public int NeutralScoreTimeFallback { get; init; }
    public int TimeFallback { get; init; }
    public int LeagueFallback { get; init; }
    public int RuleBasedFallback { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string SummaryPath { get; init; } = string.Empty;
}

public sealed class NextGoalSideModelFitter
{
    public async Task<NextGoalSideModelFitResult> FitAsync(
        NextGoalSideModelFitterOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.InputPath))
            throw new ArgumentException("Input exposure CSV path is required.", nameof(options));
        if (!File.Exists(options.InputPath))
            throw new FileNotFoundException($"Exposure CSV was not found: {options.InputPath}", options.InputPath);
        if (options.PriorWeightGoals < 0)
            throw new ArgumentException("Prior weight must be non-negative.", nameof(options));

        List<SideExposureRow> rows = await ReadRowsAsync(options.InputPath, cancellationToken);
        if (rows.Count == 0)
            throw new ArgumentException($"Exposure CSV contains no data rows: {options.InputPath}");

        List<SideExposureRow> goalRows = rows
            .Where(x => x.GoalHappened && (x.GoalSide.Equals("home", StringComparison.OrdinalIgnoreCase) || x.GoalSide.Equals("away", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        string league = !string.IsNullOrWhiteSpace(options.League)
            ? options.League.Trim()
            : ResolveLeague(rows);

        List<StateWeibullCurveBucketInfo> timeBuckets = rows
            .GroupBy(x => x.TimeBucket, StringComparer.OrdinalIgnoreCase)
            .Select(g => new StateWeibullCurveBucketInfo
            {
                TimeBucket = g.Key,
                StartMinute = g.Min(x => x.BucketStartMinute),
                EndMinute = g.Max(x => x.BucketEndMinute),
                LengthMinutes = g.Max(x => x.BucketEndMinute) - g.Min(x => x.BucketStartMinute)
            })
            .OrderBy(x => x.StartMinute)
            .ThenBy(x => x.EndMinute)
            .ToList();

        List<string> directionalBuckets = StateWeibullScoreBucketer.StandardDirectionalBuckets.ToList();
        foreach (string observed in rows.Select(x => x.DirectionalScoreBucket).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        {
            if (!directionalBuckets.Contains(observed, StringComparer.OrdinalIgnoreCase))
                directionalBuckets.Add(observed);
        }

        NextGoalSideAggregate leagueOverall = ToAggregate("league_overall", "league_overall", goalRows, priorProbability: 0.5);
        if (leagueOverall.GoalCount == 0)
        {
            leagueOverall = new NextGoalSideAggregate
            {
                Key = "league_overall",
                Source = "rule_based_default",
                ProbabilityHomeNextGoal = 0.5
            };
        }

        Dictionary<string, NextGoalSideAggregate> directionalOverall = BuildAggregateMap(
            goalRows,
            x => x.DirectionalScoreBucket,
            "directional_overall",
            priorProbability: leagueOverall.ProbabilityHomeNextGoal);

        Dictionary<string, NextGoalSideAggregate> pressureTime = BuildAggregateMap(
            goalRows,
            x => Key(x.PressureBucket, x.TimeBucket),
            "pressure_time",
            priorProbability: leagueOverall.ProbabilityHomeNextGoal);

        Dictionary<string, NextGoalSideAggregate> neutralScoreTime = BuildAggregateMap(
            goalRows,
            x => Key(x.NeutralScoreBucket, x.TimeBucket),
            "neutral_score_time",
            priorProbability: leagueOverall.ProbabilityHomeNextGoal);

        Dictionary<string, NextGoalSideAggregate> timeFallbacks = BuildAggregateMap(
            goalRows,
            x => x.TimeBucket,
            "time_bucket",
            priorProbability: leagueOverall.ProbabilityHomeNextGoal);

        var estimates = new List<NextGoalSideEstimate>();

        foreach (string directionalBucket in directionalBuckets)
        {
            foreach (StateWeibullCurveBucketInfo timeBucket in timeBuckets)
            {
                List<SideExposureRow> matchingRows = rows
                    .Where(x => x.DirectionalScoreBucket.Equals(directionalBucket, StringComparison.OrdinalIgnoreCase)
                                && x.TimeBucket.Equals(timeBucket.TimeBucket, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                SideExposureRow? sample = matchingRows.FirstOrDefault()
                    ?? rows.FirstOrDefault(x => x.DirectionalScoreBucket.Equals(directionalBucket, StringComparison.OrdinalIgnoreCase));

                string neutralBucket = sample?.NeutralScoreBucket ?? NeutralFromDirectional(directionalBucket);
                string pressureBucket = sample?.PressureBucket ?? PressureFromDirectional(directionalBucket);
                double ruleBased = RuleBasedFromDirectional(directionalBucket);

                SideCount exact = CountGoals(matchingRows);
                NextGoalSideAggregate fallback = ResolveFallback(
                    directionalBucket,
                    neutralBucket,
                    pressureBucket,
                    timeBucket.TimeBucket,
                    exact.GoalCount,
                    exact.HomeGoalCount,
                    exact.AwayGoalCount,
                    ruleBased,
                    directionalOverall,
                    pressureTime,
                    neutralScoreTime,
                    timeFallbacks,
                    leagueOverall,
                    options,
                    out string status,
                    out string source,
                    out string warning);

                double probability;
                double? exactRaw = exact.GoalCount > 0 ? exact.HomeGoalCount / (double)exact.GoalCount : null;

                if (exact.GoalCount >= options.MinExactGoals)
                {
                    status = "ExactSupported";
                    source = "exact_directional_time";
                    probability = Smooth(exact.HomeGoalCount, exact.AwayGoalCount, fallback.ProbabilityHomeNextGoal, options.PriorWeightGoals);
                    warning = string.Empty;
                }
                else
                {
                    probability = fallback.ProbabilityHomeNextGoal;
                }

                estimates.Add(new NextGoalSideEstimate
                {
                    League = league,
                    DirectionalScoreBucket = directionalBucket,
                    NeutralScoreBucket = neutralBucket,
                    PressureBucket = pressureBucket,
                    TimeBucket = timeBucket.TimeBucket,
                    BucketStartMinute = timeBucket.StartMinute,
                    BucketEndMinute = timeBucket.EndMinute,
                    Status = status,
                    ProbabilitySource = source,
                    ProbabilityHomeNextGoal = ClampProbability(probability),
                    ExactHomeGoalCount = exact.HomeGoalCount,
                    ExactAwayGoalCount = exact.AwayGoalCount,
                    ExactRawProbabilityHomeNextGoal = exactRaw,
                    FallbackSource = fallback.Source,
                    FallbackHomeGoalCount = fallback.HomeGoalCount,
                    FallbackAwayGoalCount = fallback.AwayGoalCount,
                    FallbackProbabilityHomeNextGoal = fallback.ProbabilityHomeNextGoal,
                    RuleBasedProbabilityHomeNextGoal = ruleBased,
                    Warning = warning
                });
            }
        }

        var model = new NextGoalSideModelSet
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SourceExposureFile = Path.GetFullPath(options.InputPath),
            League = league,
            DirectionalScoreBuckets = directionalBuckets,
            TimeBuckets = timeBuckets,
            Settings = new NextGoalSideModelSettings
            {
                MinExactGoals = options.MinExactGoals,
                MinDirectionalOverallGoals = options.MinDirectionalOverallGoals,
                MinPressureTimeGoals = options.MinPressureTimeGoals,
                MinNeutralScoreTimeGoals = options.MinNeutralScoreTimeGoals,
                MinTimeGoals = options.MinTimeGoals,
                MinLeagueGoals = options.MinLeagueGoals,
                PriorWeightGoals = options.PriorWeightGoals
            },
            LeagueOverall = leagueOverall,
            DirectionalOverall = directionalOverall.Values.OrderBy(x => x.Key).ToList(),
            PressureTime = pressureTime.Values.OrderBy(x => x.Key).ToList(),
            NeutralScoreTime = neutralScoreTime.Values.OrderBy(x => x.Key).ToList(),
            TimeFallbacks = timeFallbacks.Values.OrderBy(x => x.Key).ToList(),
            Estimates = estimates
                .OrderBy(x => x.BucketStartMinute)
                .ThenBy(x => x.DirectionalScoreBucket)
                .ToList()
        };

        await WriteJsonAsync(model, options.OutputPath, cancellationToken);
        await WriteSummaryCsvAsync(model.Estimates, options.SummaryPath, cancellationToken);

        return new NextGoalSideModelFitResult
        {
            ExposureRowsRead = rows.Count,
            GoalRowsRead = goalRows.Count,
            EstimatesWritten = estimates.Count,
            ExactSupported = estimates.Count(x => x.Status == "ExactSupported"),
            DirectionalFallback = estimates.Count(x => x.Status == "DirectionalOverallFallback"),
            PressureTimeFallback = estimates.Count(x => x.Status == "PressureTimeFallback"),
            NeutralScoreTimeFallback = estimates.Count(x => x.Status == "NeutralScoreTimeFallback"),
            TimeFallback = estimates.Count(x => x.Status == "TimeBucketFallback"),
            LeagueFallback = estimates.Count(x => x.Status == "LeagueOverallFallback"),
            RuleBasedFallback = estimates.Count(x => x.Status == "RuleBasedFallback"),
            OutputPath = Path.GetFullPath(options.OutputPath),
            SummaryPath = Path.GetFullPath(options.SummaryPath)
        };
    }

    private static NextGoalSideAggregate ResolveFallback(
        string directionalBucket,
        string neutralBucket,
        string pressureBucket,
        string timeBucket,
        int exactGoalCount,
        int exactHomeGoalCount,
        int exactAwayGoalCount,
        double ruleBasedProbability,
        IReadOnlyDictionary<string, NextGoalSideAggregate> directionalOverall,
        IReadOnlyDictionary<string, NextGoalSideAggregate> pressureTime,
        IReadOnlyDictionary<string, NextGoalSideAggregate> neutralScoreTime,
        IReadOnlyDictionary<string, NextGoalSideAggregate> timeFallbacks,
        NextGoalSideAggregate leagueOverall,
        NextGoalSideModelFitterOptions options,
        out string status,
        out string source,
        out string warning)
    {
        if (directionalOverall.TryGetValue(directionalBucket, out NextGoalSideAggregate? directional) && directional.GoalCount >= options.MinDirectionalOverallGoals)
        {
            status = "DirectionalOverallFallback";
            source = "fallback_directional_overall";
            warning = SparseWarning(exactGoalCount, exactHomeGoalCount, exactAwayGoalCount, source);
            return directional;
        }

        string pressureTimeKey = Key(pressureBucket, timeBucket);
        if (pressureTime.TryGetValue(pressureTimeKey, out NextGoalSideAggregate? pressure) && pressure.GoalCount >= options.MinPressureTimeGoals)
        {
            status = "PressureTimeFallback";
            source = "fallback_pressure_time";
            warning = SparseWarning(exactGoalCount, exactHomeGoalCount, exactAwayGoalCount, source);
            return pressure;
        }

        string neutralTimeKey = Key(neutralBucket, timeBucket);
        if (neutralScoreTime.TryGetValue(neutralTimeKey, out NextGoalSideAggregate? neutral) && neutral.GoalCount >= options.MinNeutralScoreTimeGoals)
        {
            status = "NeutralScoreTimeFallback";
            source = "fallback_neutral_score_time";
            warning = SparseWarning(exactGoalCount, exactHomeGoalCount, exactAwayGoalCount, source);
            return neutral;
        }

        if (timeFallbacks.TryGetValue(timeBucket, out NextGoalSideAggregate? time) && time.GoalCount >= options.MinTimeGoals)
        {
            status = "TimeBucketFallback";
            source = "fallback_time_bucket";
            warning = SparseWarning(exactGoalCount, exactHomeGoalCount, exactAwayGoalCount, source);
            return time;
        }

        if (leagueOverall.GoalCount >= options.MinLeagueGoals)
        {
            status = "LeagueOverallFallback";
            source = "fallback_league_overall";
            warning = SparseWarning(exactGoalCount, exactHomeGoalCount, exactAwayGoalCount, source);
            return leagueOverall;
        }

        status = "RuleBasedFallback";
        source = "fallback_rule_based";
        warning = SparseWarning(exactGoalCount, exactHomeGoalCount, exactAwayGoalCount, source);
        return new NextGoalSideAggregate
        {
            Key = "rule_based",
            Source = "rule_based",
            ProbabilityHomeNextGoal = ruleBasedProbability
        };
    }

    private static string SparseWarning(int exactGoalCount, int exactHomeGoalCount, int exactAwayGoalCount, string source)
        => $"Exact directional/time bucket too sparse ({exactGoalCount} goals: home={exactHomeGoalCount}, away={exactAwayGoalCount}); {source} used.";

    private static Dictionary<string, NextGoalSideAggregate> BuildAggregateMap(
        IReadOnlyList<SideExposureRow> goalRows,
        Func<SideExposureRow, string> keySelector,
        string source,
        double priorProbability)
    {
        var result = new Dictionary<string, NextGoalSideAggregate>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, SideExposureRow> group in goalRows.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase))
        {
            List<SideExposureRow> rows = group.ToList();
            result[group.Key] = ToAggregate(group.Key, source, rows, priorProbability);
        }

        return result;
    }

    private static NextGoalSideAggregate ToAggregate(
        string key,
        string source,
        IReadOnlyList<SideExposureRow> goalRows,
        double priorProbability)
    {
        SideCount count = CountGoals(goalRows);
        SideExposureRow? sample = goalRows.FirstOrDefault();
        return new NextGoalSideAggregate
        {
            Key = key,
            Source = source,
            DirectionalScoreBucket = sample?.DirectionalScoreBucket ?? string.Empty,
            NeutralScoreBucket = sample?.NeutralScoreBucket ?? string.Empty,
            PressureBucket = sample?.PressureBucket ?? string.Empty,
            TimeBucket = sample?.TimeBucket ?? string.Empty,
            HomeGoalCount = count.HomeGoalCount,
            AwayGoalCount = count.AwayGoalCount,
            ProbabilityHomeNextGoal = count.GoalCount > 0
                ? ClampProbability(count.HomeGoalCount / (double)count.GoalCount)
                : ClampProbability(priorProbability)
        };
    }

    private static SideCount CountGoals(IReadOnlyList<SideExposureRow> rows)
    {
        int home = rows.Count(x => x.GoalHappened && x.GoalSide.Equals("home", StringComparison.OrdinalIgnoreCase));
        int away = rows.Count(x => x.GoalHappened && x.GoalSide.Equals("away", StringComparison.OrdinalIgnoreCase));
        return new SideCount(home, away);
    }

    private static double Smooth(double homeCount, double awayCount, double priorProbability, double priorWeight)
    {
        double total = homeCount + awayCount;
        if (total <= 0)
            return ClampProbability(priorProbability);

        double numerator = homeCount + priorProbability * priorWeight;
        double denominator = total + priorWeight;
        return ClampProbability(numerator / denominator);
    }

    private static double ClampProbability(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0.5;

        return Math.Clamp(value, 0.05, 0.95);
    }

    private static string NeutralFromDirectional(string directionalBucket)
    {
        if (directionalBucket.Equals("draw_0_0", StringComparison.OrdinalIgnoreCase))
            return "draw_0_0";
        if (directionalBucket.Equals("draw_1_1_plus", StringComparison.OrdinalIgnoreCase))
            return "draw_1_1_plus";
        if (directionalBucket.Contains("lead1_low", StringComparison.OrdinalIgnoreCase))
            return "margin1_total1_2";
        if (directionalBucket.Contains("lead1_high", StringComparison.OrdinalIgnoreCase))
            return "margin1_total3_plus";
        if (directionalBucket.Contains("lead2", StringComparison.OrdinalIgnoreCase))
            return "margin2";
        return "margin3_plus";
    }

    private static string PressureFromDirectional(string directionalBucket)
    {
        if (directionalBucket.StartsWith("draw", StringComparison.OrdinalIgnoreCase))
            return "draw";
        if (directionalBucket.StartsWith("home_lead1", StringComparison.OrdinalIgnoreCase))
            return "home_lead1";
        if (directionalBucket.StartsWith("away_lead1", StringComparison.OrdinalIgnoreCase))
            return "away_lead1";
        if (directionalBucket.StartsWith("home_", StringComparison.OrdinalIgnoreCase))
            return "home_lead2_plus";
        return "away_lead2_plus";
    }

    private static double RuleBasedFromDirectional(string directionalBucket)
    {
        if (directionalBucket.StartsWith("draw", StringComparison.OrdinalIgnoreCase))
            return 0.53;
        if (directionalBucket.StartsWith("home_lead1", StringComparison.OrdinalIgnoreCase))
            return 0.48;
        if (directionalBucket.StartsWith("home_lead2", StringComparison.OrdinalIgnoreCase))
            return 0.43;
        if (directionalBucket.StartsWith("home_lead3", StringComparison.OrdinalIgnoreCase))
            return 0.40;
        if (directionalBucket.StartsWith("away_lead1", StringComparison.OrdinalIgnoreCase))
            return 0.58;
        if (directionalBucket.StartsWith("away_lead2", StringComparison.OrdinalIgnoreCase))
            return 0.63;
        return 0.66;
    }

    private static string Key(string first, string second) => $"{first}|{second}";

    private static string ResolveLeague(IReadOnlyList<SideExposureRow> rows)
    {
        string? league = rows
            .Select(x => !string.IsNullOrWhiteSpace(x.LeagueSlug) ? x.LeagueSlug : x.League)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => g.Key)
            .FirstOrDefault();

        return league ?? string.Empty;
    }

    private static async Task<List<SideExposureRow>> ReadRowsAsync(string inputPath, CancellationToken cancellationToken)
    {
        string[] lines = await File.ReadAllLinesAsync(inputPath, cancellationToken);
        if (lines.Length <= 1)
            return [];

        string[] headers = SplitCsvLine(lines[0]);
        var index = headers
            .Select((name, i) => (name: name.Trim().ToLowerInvariant(), i))
            .ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);

        string[] required =
        [
            "league", "league_slug", "time_bucket", "bucket_start_minute", "bucket_end_minute",
            "score_bucket", "home_goals_at_start", "away_goals_at_start", "goal_happened", "goal_side"
        ];

        foreach (string column in required)
        {
            if (!index.ContainsKey(column))
                throw new ArgumentException($"Exposure CSV missing required column '{column}'.");
        }

        var rows = new List<SideExposureRow>();
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            if (string.IsNullOrWhiteSpace(lines[lineIndex]))
                continue;

            string[] parts = SplitCsvLine(lines[lineIndex]);
            string Get(string name) => index.TryGetValue(name, out int i) && i < parts.Length ? parts[i] : string.Empty;

            int homeGoals = ParseInt(Get("home_goals_at_start"));
            int awayGoals = ParseInt(Get("away_goals_at_start"));
            string neutral = Get("score_bucket");
            if (string.IsNullOrWhiteSpace(neutral))
                neutral = StateWeibullScoreBucketer.ResolveScoreBucket(homeGoals, awayGoals);

            rows.Add(new SideExposureRow
            {
                League = Get("league"),
                LeagueSlug = Get("league_slug"),
                TimeBucket = Get("time_bucket"),
                BucketStartMinute = ParseDouble(Get("bucket_start_minute")),
                BucketEndMinute = ParseDouble(Get("bucket_end_minute")),
                NeutralScoreBucket = neutral,
                DirectionalScoreBucket = StateWeibullScoreBucketer.ResolveDirectionalScoreBucket(homeGoals, awayGoals),
                PressureBucket = StateWeibullScoreBucketer.ResolvePressureBucket(homeGoals, awayGoals),
                HomeGoalsAtStart = homeGoals,
                AwayGoalsAtStart = awayGoals,
                GoalHappened = ParseBool01(Get("goal_happened")),
                GoalSide = Get("goal_side")
            });
        }

        return rows;
    }

    private static async Task WriteJsonAsync(NextGoalSideModelSet model, string outputPath, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            fullPath,
            JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8,
            cancellationToken);
    }

    private static async Task WriteSummaryCsvAsync(IReadOnlyList<NextGoalSideEstimate> estimates, string summaryPath, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(summaryPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        builder.AppendLine("league,directional_score_bucket,neutral_score_bucket,pressure_bucket,time_bucket,bucket_start_minute,bucket_end_minute,status,probability_source,p_home_next_goal,p_away_next_goal,exact_home_goals,exact_away_goals,exact_goal_count,exact_raw_p_home,fallback_source,fallback_home_goals,fallback_away_goals,fallback_goal_count,fallback_p_home,rule_based_p_home,warning");

        foreach (NextGoalSideEstimate estimate in estimates)
        {
            builder.Append(Csv(estimate.League)); builder.Append(',');
            builder.Append(Csv(estimate.DirectionalScoreBucket)); builder.Append(',');
            builder.Append(Csv(estimate.NeutralScoreBucket)); builder.Append(',');
            builder.Append(Csv(estimate.PressureBucket)); builder.Append(',');
            builder.Append(Csv(estimate.TimeBucket)); builder.Append(',');
            builder.Append(Format(estimate.BucketStartMinute)); builder.Append(',');
            builder.Append(Format(estimate.BucketEndMinute)); builder.Append(',');
            builder.Append(Csv(estimate.Status)); builder.Append(',');
            builder.Append(Csv(estimate.ProbabilitySource)); builder.Append(',');
            builder.Append(Format(estimate.ProbabilityHomeNextGoal)); builder.Append(',');
            builder.Append(Format(estimate.ProbabilityAwayNextGoal)); builder.Append(',');
            builder.Append(estimate.ExactHomeGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(estimate.ExactAwayGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(estimate.ExactGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(estimate.ExactRawProbabilityHomeNextGoal.HasValue ? Format(estimate.ExactRawProbabilityHomeNextGoal.Value) : string.Empty); builder.Append(',');
            builder.Append(Csv(estimate.FallbackSource)); builder.Append(',');
            builder.Append(estimate.FallbackHomeGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(estimate.FallbackAwayGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(estimate.FallbackGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(Format(estimate.FallbackProbabilityHomeNextGoal)); builder.Append(',');
            builder.Append(Format(estimate.RuleBasedProbabilityHomeNextGoal)); builder.Append(',');
            builder.Append(Csv(estimate.Warning));
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(fullPath, builder.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static string[] SplitCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                values.Add(builder.ToString());
                builder.Clear();
            }
            else
            {
                builder.Append(ch);
            }
        }

        values.Add(builder.ToString());
        return values.ToArray();
    }

    private static int ParseInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

    private static double ParseDouble(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0.0;

    private static bool ParseBool01(string value)
        => value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string Format(double value)
        => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Csv(string value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private sealed record SideExposureRow
    {
        public string League { get; init; } = string.Empty;
        public string LeagueSlug { get; init; } = string.Empty;
        public string TimeBucket { get; init; } = string.Empty;
        public double BucketStartMinute { get; init; }
        public double BucketEndMinute { get; init; }
        public string NeutralScoreBucket { get; init; } = string.Empty;
        public string DirectionalScoreBucket { get; init; } = string.Empty;
        public string PressureBucket { get; init; } = string.Empty;
        public int HomeGoalsAtStart { get; init; }
        public int AwayGoalsAtStart { get; init; }
        public bool GoalHappened { get; init; }
        public string GoalSide { get; init; } = string.Empty;
    }

    private sealed record SideCount(int HomeGoalCount, int AwayGoalCount)
    {
        public int GoalCount => HomeGoalCount + AwayGoalCount;
    }
}
