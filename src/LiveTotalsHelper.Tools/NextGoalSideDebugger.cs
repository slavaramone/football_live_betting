using System.Globalization;
using System.Text.Json;
using LiveTotalsHelper.Core.MonteCarlo;

namespace LiveTotalsHelper.Tools;

public sealed class NextGoalSideDebugOptions
{
    public string ModelPath { get; init; } = "outputs/calibration/next-goal-side-model.json";
    public string League { get; init; } = string.Empty;
    public int HomeGoals { get; init; }
    public int AwayGoals { get; init; }
    public double Minute { get; init; }
}

public sealed class NextGoalSideDebugResult
{
    public string League { get; init; } = string.Empty;
    public string ExactScore { get; init; } = string.Empty;
    public string DirectionalScoreBucket { get; init; } = string.Empty;
    public string NeutralScoreBucket { get; init; } = string.Empty;
    public string PressureBucket { get; init; } = string.Empty;
    public string TimeBucket { get; init; } = string.Empty;
    public double Minute { get; init; }
    public NextGoalSideEstimate? Estimate { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class NextGoalSideDebugger
{
    private const double Epsilon = 0.000001;

    public async Task<NextGoalSideDebugResult> DebugAsync(
        NextGoalSideDebugOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ModelPath))
            throw new ArgumentException("Model JSON path is required.", nameof(options));
        if (!File.Exists(options.ModelPath))
            throw new FileNotFoundException($"Next-goal-side model JSON was not found: {options.ModelPath}", options.ModelPath);
        if (options.Minute < 0)
            throw new ArgumentException("Minute must be non-negative.", nameof(options));

        string json = await File.ReadAllTextAsync(options.ModelPath, cancellationToken);
        NextGoalSideModelSet model = JsonSerializer.Deserialize<NextGoalSideModelSet>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException($"Could not read next-goal-side model JSON: {options.ModelPath}");

        if (model.Estimates.Count == 0)
            throw new ArgumentException($"Next-goal-side model contains no estimates: {options.ModelPath}");

        string directional = StateWeibullScoreBucketer.ResolveDirectionalScoreBucket(options.HomeGoals, options.AwayGoals);
        string neutral = StateWeibullScoreBucketer.ResolveScoreBucket(options.HomeGoals, options.AwayGoals);
        string pressure = StateWeibullScoreBucketer.ResolvePressureBucket(options.HomeGoals, options.AwayGoals);
        string exactScore = StateWeibullScoreBucketer.ResolveExactScore(options.HomeGoals, options.AwayGoals);

        NextGoalSideEstimate? estimate = model.Estimates
            .Where(x => x.DirectionalScoreBucket.Equals(directional, StringComparison.OrdinalIgnoreCase)
                        && options.Minute >= x.BucketStartMinute - Epsilon
                        && options.Minute < x.BucketEndMinute - Epsilon)
            .OrderBy(x => x.BucketStartMinute)
            .FirstOrDefault();

        if (estimate is null)
        {
            estimate = model.Estimates
                .Where(x => x.DirectionalScoreBucket.Equals(directional, StringComparison.OrdinalIgnoreCase)
                            && Math.Abs(options.Minute - x.BucketEndMinute) <= Epsilon)
                .OrderByDescending(x => x.BucketEndMinute)
                .FirstOrDefault();
        }

        if (estimate is null)
            throw new ArgumentException($"Model contains no estimate for directional score bucket '{directional}' at minute {Format(options.Minute)}.");

        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.League)
            && !string.IsNullOrWhiteSpace(model.League)
            && !options.League.Equals(model.League, StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Requested league/profile '{options.League}' differs from model file league '{model.League}'. Model file league is used.");

        if (!string.IsNullOrWhiteSpace(estimate.Warning))
            warnings.Add(estimate.Warning);

        return new NextGoalSideDebugResult
        {
            League = model.League,
            ExactScore = exactScore,
            DirectionalScoreBucket = directional,
            NeutralScoreBucket = neutral,
            PressureBucket = pressure,
            TimeBucket = estimate.TimeBucket,
            Minute = options.Minute,
            Estimate = estimate,
            Warnings = warnings
        };
    }

    private static string Format(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);
}
