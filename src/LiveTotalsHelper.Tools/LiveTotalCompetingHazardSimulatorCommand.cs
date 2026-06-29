using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Core.MonteCarlo;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalCompetingHazardCommandOptions
{
    public string CompetingHazardCurvesPath { get; init; } = "outputs/calibration/competing-hazard-curves.json";
    public string OutputPath { get; init; } = "outputs/debug/live-total-mc-v3.json";
    public string PathsOutputPath { get; init; } = string.Empty;
    public string League { get; init; } = string.Empty;
    public double Minute { get; init; }
    public double? UntilMinute { get; init; }
    public int HomeGoals { get; init; }
    public int AwayGoals { get; init; }
    public int HomeRedCards { get; init; }
    public int AwayRedCards { get; init; }
    public double? LastGoalMinute { get; init; }
    public string LastGoalSide { get; init; } = string.Empty;
    public double Line { get; init; }
    public double? OverOdds { get; init; }
    public double? UnderOdds { get; init; }
    public double? MarketTotal { get; init; }
    public double? PregameTotal { get; init; }
    public double? PregameTotalLine { get; init; }
    public double? PregameOverOdds { get; init; }
    public double? PregameUnderOdds { get; init; }
    public double? MarketBaselineLowTotalShrink { get; init; }
    public double? MarketBaselineHighTotalShrink { get; init; }
    public double? MarketBaselineMinMultiplier { get; init; }
    public double? MarketBaselineMaxMultiplier { get; init; }
    public double? MarketBaselineOddsSensitivityGoals { get; init; }
    public int SimulationCount { get; init; } = 20_000;
    public double StepMinutes { get; init; } = 0.25;
    public int? RandomSeed { get; init; } = 12_345;
    public int TracePathCount { get; init; }
    public double EstimatedEffectiveEndMinute { get; init; }
}

public sealed class LiveTotalCompetingHazardCommandResult
{
    public LiveMonteCarloSimulationResult Simulation { get; init; } = new();
    public string OutputPath { get; init; } = string.Empty;
    public string PathsOutputPath { get; init; } = string.Empty;
}

public sealed class LiveTotalCompetingHazardSimulatorCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<LiveTotalCompetingHazardCommandResult> RunAsync(
        LiveTotalCompetingHazardCommandOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.CompetingHazardCurvesPath))
            throw new ArgumentException("Competing-hazard curves JSON path is required.", nameof(options));
        if (!File.Exists(options.CompetingHazardCurvesPath))
            throw new FileNotFoundException($"Competing-hazard curves JSON was not found: {options.CompetingHazardCurvesPath}", options.CompetingHazardCurvesPath);
        if (options.SimulationCount <= 0)
            throw new ArgumentException("Simulation count must be positive.", nameof(options));
        if (options.StepMinutes <= 0)
            throw new ArgumentException("Step minutes must be positive.", nameof(options));

        CompetingHazardCurveSet curves = await ReadJsonAsync<CompetingHazardCurveSet>(options.CompetingHazardCurvesPath, cancellationToken);

        var request = new LiveMonteCarloRequest
        {
            LeagueKey = string.IsNullOrWhiteSpace(options.League) ? curves.League : options.League,
            CurrentMinute = options.Minute,
            HomeGoals = options.HomeGoals,
            AwayGoals = options.AwayGoals,
            HomeRedCards = options.HomeRedCards,
            AwayRedCards = options.AwayRedCards,
            LastGoalMinute = options.LastGoalMinute,
            LastGoalSide = options.LastGoalSide,
            Line = options.Line,
            OverOdds = options.OverOdds,
            UnderOdds = options.UnderOdds,
            MarketTotal = options.MarketTotal,
            PregameTotal = options.PregameTotal,
            PregameTotalLine = options.PregameTotalLine,
            PregameOverOdds = options.PregameOverOdds,
            PregameUnderOdds = options.PregameUnderOdds,
            MarketBaselineLowTotalShrink = options.MarketBaselineLowTotalShrink,
            MarketBaselineHighTotalShrink = options.MarketBaselineHighTotalShrink,
            MarketBaselineMinMultiplier = options.MarketBaselineMinMultiplier,
            MarketBaselineMaxMultiplier = options.MarketBaselineMaxMultiplier,
            MarketBaselineOddsSensitivityGoals = options.MarketBaselineOddsSensitivityGoals,
            SimulationCount = options.SimulationCount,
            StepMinutes = options.StepMinutes,
            RandomSeed = options.RandomSeed
        };

        double effectiveEnd = options.UntilMinute ?? options.EstimatedEffectiveEndMinute;
        if (effectiveEnd <= 0)
            effectiveEnd = curves.Curves.Count == 0 ? 96.0 : curves.Curves.Max(x => x.BucketEndMinute);

        var simulator = new LiveCompetingHazardMonteCarloSimulator();
        LiveMonteCarloSimulationResult simulation = simulator.Run(new LiveCompetingHazardMonteCarloSimulationOptions
        {
            Request = request,
            Curves = curves,
            EffectiveEndMinute = effectiveEnd,
            TracePathCount = options.TracePathCount
        });

        string outputPath = await WriteJsonAsync(options.OutputPath, simulation, cancellationToken);
        string pathsOutputPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(options.PathsOutputPath))
            pathsOutputPath = await WriteTraceEventsAsync(options.PathsOutputPath, simulation.TraceEvents, cancellationToken);

        return new LiveTotalCompetingHazardCommandResult
        {
            Simulation = simulation,
            OutputPath = outputPath,
            PathsOutputPath = pathsOutputPath
        };
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        string json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new ArgumentException($"Could not read JSON file: {path}");
    }

    private static async Task<string> WriteJsonAsync(
        string path,
        LiveMonteCarloSimulationResult result,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(result, JsonOptions), Encoding.UTF8, cancellationToken);
        return fullPath;
    }

    private static async Task<string> WriteTraceEventsAsync(
        string path,
        IReadOnlyList<LiveMonteCarloPathEvent> events,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        builder.AppendLine("simulation,goal_index,goal_minute,scorer,score_before,score_after,score_bucket_before,score_bucket_after,time_bucket,curve_status,curve_source,side_probability_source,p_home_next_goal,expected_goals_in_step,p_goal_in_step,after_goal_bucket,after_goal_home_multiplier,after_goal_away_multiplier,goal_draw_factor,goal_draw_multiplier,market_baseline_multiplier");

        foreach (LiveMonteCarloPathEvent item in events)
        {
            builder.Append(item.Simulation.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(item.GoalIndex.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(Format(item.GoalMinute)); builder.Append(',');
            builder.Append(Csv(item.Scorer)); builder.Append(',');
            builder.Append(Csv(item.ScoreBefore)); builder.Append(',');
            builder.Append(Csv(item.ScoreAfter)); builder.Append(',');
            builder.Append(Csv(item.ScoreBucketBefore)); builder.Append(',');
            builder.Append(Csv(item.ScoreBucketAfter)); builder.Append(',');
            builder.Append(Csv(item.TimeBucket)); builder.Append(',');
            builder.Append(Csv(item.CurveStatus)); builder.Append(',');
            builder.Append(Csv(item.CurveSource)); builder.Append(',');
            builder.Append(Csv(item.SideProbabilitySource)); builder.Append(',');
            builder.Append(Format(item.ProbabilityHomeNextGoal)); builder.Append(',');
            builder.Append(Format(item.ExpectedGoalsInStep)); builder.Append(',');
            builder.Append(Format(item.GoalProbabilityInStep)); builder.Append(',');
            builder.Append(Csv(item.AfterGoalBucket)); builder.Append(',');
            builder.Append(Format(item.AfterGoalHomeMultiplier)); builder.Append(',');
            builder.Append(Format(item.AfterGoalAwayMultiplier)); builder.Append(',');
            builder.Append(Csv(item.GoalDrawFactorKey)); builder.Append(',');
            builder.Append(Format(item.GoalDrawMultiplier)); builder.Append(',');
            builder.Append(Format(item.MarketBaselineMultiplier));
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(fullPath, builder.ToString(), Encoding.UTF8, cancellationToken);
        return fullPath;
    }

    private static string Format(double value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Csv(string value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
