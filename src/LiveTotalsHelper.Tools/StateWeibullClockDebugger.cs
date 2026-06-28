using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Core.MonteCarlo;

namespace LiveTotalsHelper.Tools;

public sealed class StateWeibullClockDebugOptions
{
    public string CurvesPath { get; init; } = "outputs/calibration/state-weibull-curves.json";
    public string OutputPath { get; init; } = "outputs/debug/state-weibull-clock.csv";
    public string League { get; init; } = string.Empty;
    public int HomeGoals { get; init; }
    public int AwayGoals { get; init; }
    public double Minute { get; init; }
    public double? UntilMinute { get; init; }
    public double StepMinutes { get; init; } = 1.0;
}

public sealed class StateWeibullClockDebugResult
{
    public string League { get; init; } = string.Empty;
    public string ScoreBucket { get; init; } = string.Empty;
    public string ExactScore { get; init; } = string.Empty;
    public double StartMinute { get; init; }
    public double UntilMinute { get; init; }
    public double StepMinutes { get; init; }
    public StateWeibullCurve? StartingCurve { get; init; }
    public double ExpectedRemainingToUntil { get; init; }
    public int RowsWritten { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class StateWeibullClockDebugger
{
    private const double Epsilon = 0.000001;
    private const double RateStartClampMinutes = 0.01;

    public async Task<StateWeibullClockDebugResult> DebugAsync(
        StateWeibullClockDebugOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.CurvesPath))
            throw new ArgumentException("Curve JSON path is required.", nameof(options));
        if (!File.Exists(options.CurvesPath))
            throw new FileNotFoundException($"Curve JSON was not found: {options.CurvesPath}", options.CurvesPath);
        if (options.Minute < 0)
            throw new ArgumentException("Minute must be non-negative.", nameof(options));
        if (options.StepMinutes <= 0)
            throw new ArgumentException("Step minutes must be positive.", nameof(options));

        string json = await File.ReadAllTextAsync(options.CurvesPath, cancellationToken);
        StateWeibullCurveSet curveSet = JsonSerializer.Deserialize<StateWeibullCurveSet>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException($"Could not read curve JSON: {options.CurvesPath}");

        if (curveSet.Curves.Count == 0)
            throw new ArgumentException($"Curve JSON contains no curves: {options.CurvesPath}");

        string scoreBucket = StateWeibullScoreBucketer.ResolveScoreBucket(options.HomeGoals, options.AwayGoals);
        string exactScore = StateWeibullScoreBucketer.ResolveExactScore(options.HomeGoals, options.AwayGoals);

        List<StateWeibullCurve> scoreCurves = curveSet.Curves
            .Where(x => x.ScoreBucket.Equals(scoreBucket, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.BucketStartMinute)
            .ThenBy(x => x.BucketEndMinute)
            .ToList();

        if (scoreCurves.Count == 0)
            throw new ArgumentException($"Curve JSON contains no curves for score bucket '{scoreBucket}'.");

        double maxEnd = scoreCurves.Max(x => x.BucketEndMinute);
        double until = options.UntilMinute ?? maxEnd;
        if (until <= options.Minute)
            throw new ArgumentException("Argument --until must be greater than --minute.");

        var warnings = new List<string>();
        if (until > maxEnd + Epsilon)
        {
            warnings.Add($"Requested until minute {Format(until)} is beyond last fitted bucket end {Format(maxEnd)}; output is capped at {Format(maxEnd)}.");
            until = maxEnd;
        }

        StateWeibullCurve startingCurve = ResolveCurve(scoreCurves, options.Minute)
            ?? throw new ArgumentException($"No curve covers minute {Format(options.Minute)} for score bucket '{scoreBucket}'.");

        if (!string.IsNullOrWhiteSpace(options.League)
            && !string.IsNullOrWhiteSpace(curveSet.League)
            && !options.League.Equals(curveSet.League, StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Requested league/profile '{options.League}' differs from curve file league '{curveSet.League}'. Curve file league is used for calculations.");

        if (!string.IsNullOrWhiteSpace(startingCurve.Warning))
            warnings.Add(startingCurve.Warning);

        List<double> outputMinutes = BuildOutputMinutes(options.Minute, until, options.StepMinutes, scoreCurves);
        var rows = new List<StateWeibullClockDebugRow>();

        foreach (double minute in outputMinutes)
        {
            StateWeibullCurve? curve = ResolveCurve(scoreCurves, minute);
            if (curve is null)
                continue;

            double cumulative = CumulativeExpectedGoalsInBucket(curve, minute);
            double remainingBucket = Math.Max(0.0, curve.ExpectedGoalsInBucket - cumulative);
            double remainingToUntil = ExpectedRemainingBetween(scoreCurves, minute, until);

            rows.Add(new StateWeibullClockDebugRow
            {
                Minute = minute,
                League = curveSet.League,
                ExactScore = exactScore,
                ScoreBucket = scoreBucket,
                TimeBucket = curve.TimeBucket,
                BucketStartMinute = curve.BucketStartMinute,
                BucketEndMinute = curve.BucketEndMinute,
                Status = curve.Status,
                CurveSource = curve.CurveSource,
                ExpectedGoalsSource = curve.ExpectedGoalsSource,
                ShapeKSource = curve.ShapeKSource,
                FullBucketExposures = curve.FullBucketExposures,
                GoalCount = curve.GoalCount,
                ExpectedGoalsInBucket = curve.ExpectedGoalsInBucket,
                ShapeK = curve.ShapeK,
                RatePerMinute = RatePerMinute(curve, minute),
                CumulativeBucketGoals = cumulative,
                RemainingBucketGoals = remainingBucket,
                ExpectedRemainingToUntil = remainingToUntil,
                Warning = curve.Warning
            });
        }

        await WriteRowsAsync(rows, options.OutputPath, cancellationToken);

        return new StateWeibullClockDebugResult
        {
            League = curveSet.League,
            ScoreBucket = scoreBucket,
            ExactScore = exactScore,
            StartMinute = options.Minute,
            UntilMinute = until,
            StepMinutes = options.StepMinutes,
            StartingCurve = startingCurve,
            ExpectedRemainingToUntil = ExpectedRemainingBetween(scoreCurves, options.Minute, until),
            RowsWritten = rows.Count,
            OutputPath = Path.GetFullPath(options.OutputPath),
            Warnings = warnings
        };
    }

    private static List<double> BuildOutputMinutes(
        double startMinute,
        double untilMinute,
        double stepMinutes,
        IReadOnlyCollection<StateWeibullCurve> curves)
    {
        var values = new SortedSet<double>();
        values.Add(RoundMinute(startMinute));
        values.Add(RoundMinute(untilMinute));

        for (double minute = startMinute + stepMinutes; minute < untilMinute - Epsilon; minute += stepMinutes)
            values.Add(RoundMinute(minute));

        foreach (StateWeibullCurve curve in curves)
        {
            if (curve.BucketStartMinute > startMinute + Epsilon && curve.BucketStartMinute < untilMinute - Epsilon)
                values.Add(RoundMinute(curve.BucketStartMinute));
            if (curve.BucketEndMinute > startMinute + Epsilon && curve.BucketEndMinute < untilMinute - Epsilon)
                values.Add(RoundMinute(curve.BucketEndMinute));
        }

        return values.Where(x => x >= startMinute - Epsilon && x <= untilMinute + Epsilon).ToList();
    }

    private static StateWeibullCurve? ResolveCurve(IEnumerable<StateWeibullCurve> curves, double minute)
    {
        StateWeibullCurve? active = curves
            .Where(x => minute >= x.BucketStartMinute - Epsilon && minute < x.BucketEndMinute - Epsilon)
            .OrderBy(x => x.BucketStartMinute)
            .FirstOrDefault();

        if (active is not null)
            return active;

        return curves
            .Where(x => Math.Abs(minute - x.BucketEndMinute) <= Epsilon)
            .OrderByDescending(x => x.BucketEndMinute)
            .FirstOrDefault();
    }

    private static double ExpectedRemainingBetween(
        IReadOnlyList<StateWeibullCurve> curves,
        double fromMinute,
        double untilMinute)
    {
        double total = 0.0;

        foreach (StateWeibullCurve curve in curves)
        {
            double start = Math.Max(fromMinute, curve.BucketStartMinute);
            double end = Math.Min(untilMinute, curve.BucketEndMinute);
            if (end <= start + Epsilon)
                continue;

            total += CumulativeExpectedGoalsInBucket(curve, end) - CumulativeExpectedGoalsInBucket(curve, start);
        }

        return Math.Max(0.0, total);
    }

    private static double RatePerMinute(StateWeibullCurve curve, double minute)
    {
        double length = Math.Max(curve.BucketLengthMinutes, Epsilon);
        double localMinute = Math.Clamp(minute - curve.BucketStartMinute, RateStartClampMinutes, length);
        double x = localMinute / length;

        return curve.ExpectedGoalsInBucket
            * curve.ShapeK
            / length
            * Math.Pow(x, curve.ShapeK - 1.0);
    }

    private static double CumulativeExpectedGoalsInBucket(StateWeibullCurve curve, double minute)
    {
        double length = Math.Max(curve.BucketLengthMinutes, Epsilon);
        double localMinute = Math.Clamp(minute - curve.BucketStartMinute, 0.0, length);
        double x = localMinute / length;

        return curve.ExpectedGoalsInBucket * Math.Pow(x, curve.ShapeK);
    }

    private static async Task WriteRowsAsync(
        IReadOnlyList<StateWeibullClockDebugRow> rows,
        string outputPath,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        builder.AppendLine("minute,league,exact_score,score_bucket,time_bucket,bucket_start_minute,bucket_end_minute,status,curve_source,mu_source,k_source,full_bucket_exposures,goal_count,expected_goals_in_bucket,shape_k,rate_per_minute,cumulative_bucket_goals,remaining_bucket_goals,expected_remaining_to_until,warning");

        foreach (StateWeibullClockDebugRow row in rows)
        {
            builder.Append(Format(row.Minute)); builder.Append(',');
            builder.Append(Csv(row.League)); builder.Append(',');
            builder.Append(Csv(row.ExactScore)); builder.Append(',');
            builder.Append(Csv(row.ScoreBucket)); builder.Append(',');
            builder.Append(Csv(row.TimeBucket)); builder.Append(',');
            builder.Append(Format(row.BucketStartMinute)); builder.Append(',');
            builder.Append(Format(row.BucketEndMinute)); builder.Append(',');
            builder.Append(Csv(row.Status)); builder.Append(',');
            builder.Append(Csv(row.CurveSource)); builder.Append(',');
            builder.Append(Csv(row.ExpectedGoalsSource)); builder.Append(',');
            builder.Append(Csv(row.ShapeKSource)); builder.Append(',');
            builder.Append(Format(row.FullBucketExposures)); builder.Append(',');
            builder.Append(row.GoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(Format(row.ExpectedGoalsInBucket)); builder.Append(',');
            builder.Append(Format(row.ShapeK)); builder.Append(',');
            builder.Append(Format(row.RatePerMinute)); builder.Append(',');
            builder.Append(Format(row.CumulativeBucketGoals)); builder.Append(',');
            builder.Append(Format(row.RemainingBucketGoals)); builder.Append(',');
            builder.Append(Format(row.ExpectedRemainingToUntil)); builder.Append(',');
            builder.Append(Csv(row.Warning));
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(fullPath, builder.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static double RoundMinute(double value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string Format(double value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Csv(string value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private sealed class StateWeibullClockDebugRow
    {
        public double Minute { get; init; }
        public string League { get; init; } = string.Empty;
        public string ExactScore { get; init; } = string.Empty;
        public string ScoreBucket { get; init; } = string.Empty;
        public string TimeBucket { get; init; } = string.Empty;
        public double BucketStartMinute { get; init; }
        public double BucketEndMinute { get; init; }
        public string Status { get; init; } = string.Empty;
        public string CurveSource { get; init; } = string.Empty;
        public string ExpectedGoalsSource { get; init; } = string.Empty;
        public string ShapeKSource { get; init; } = string.Empty;
        public double FullBucketExposures { get; init; }
        public int GoalCount { get; init; }
        public double ExpectedGoalsInBucket { get; init; }
        public double ShapeK { get; init; }
        public double RatePerMinute { get; init; }
        public double CumulativeBucketGoals { get; init; }
        public double RemainingBucketGoals { get; init; }
        public double ExpectedRemainingToUntil { get; init; }
        public string Warning { get; init; } = string.Empty;
    }
}
