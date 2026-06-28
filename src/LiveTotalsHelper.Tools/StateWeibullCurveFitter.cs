using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Core.MonteCarlo;

namespace LiveTotalsHelper.Tools;

public sealed class StateWeibullCurveFitterOptions
{
    public string InputPath { get; init; } = "outputs/calibration/state-weibull-exposures.csv";
    public string OutputPath { get; init; } = "outputs/calibration/state-weibull-curves.json";
    public string SummaryPath { get; init; } = "outputs/calibration/state-weibull-curves-summary.csv";
    public string League { get; init; } = string.Empty;
    public double MinMuFullBucketExposures { get; init; } = 75.0;
    public int MinMuGoals { get; init; } = 30;
    public double MinKFullBucketExposures { get; init; } = 150.0;
    public int MinKGoals { get; init; } = 50;
    public double MinK { get; init; } = 0.65;
    public double MaxK { get; init; } = 1.85;
    public double KStep { get; init; } = 0.05;
    public double DefaultK { get; init; } = 1.0;
}

public sealed class StateWeibullCurveFitResult
{
    public int ExposureRowsRead { get; init; }
    public int CurvesWritten { get; init; }
    public int ExactSupported { get; init; }
    public int PartialSupported { get; init; }
    public int UnsupportedSparse { get; init; }
    public int TimeFallbacksWritten { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string SummaryPath { get; init; } = string.Empty;
}

public sealed class StateWeibullCurveFitter
{
    private const double Epsilon = 0.000001;

    public async Task<StateWeibullCurveFitResult> FitAsync(
        StateWeibullCurveFitterOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.InputPath))
            throw new ArgumentException("Input exposure CSV path is required.", nameof(options));
        if (!File.Exists(options.InputPath))
            throw new FileNotFoundException($"Exposure CSV was not found: {options.InputPath}", options.InputPath);
        if (options.MinK <= 0 || options.MaxK <= options.MinK || options.KStep <= 0)
            throw new ArgumentException("Invalid k grid settings. Require 0 < min-k < max-k and k-step > 0.", nameof(options));
        if (options.DefaultK <= 0)
            throw new ArgumentException("Default k must be positive.", nameof(options));

        List<ExposureRow> rows = await ReadExposureRowsAsync(options.InputPath, cancellationToken);
        if (rows.Count == 0)
            throw new ArgumentException($"Exposure CSV contains no data rows: {options.InputPath}");

        string league = !string.IsNullOrWhiteSpace(options.League)
            ? options.League.Trim()
            : ResolveLeague(rows);

        List<StateWeibullCurveBucketInfo> timeBuckets = rows
            .GroupBy(x => x.TimeBucket)
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

        if (timeBuckets.Count == 0)
            throw new ArgumentException("Exposure CSV contains no time buckets.");

        List<string> scoreBuckets = StateWeibullScoreBucketer.StandardBuckets.ToList();
        foreach (string observed in rows.Select(x => x.ScoreBucket).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        {
            if (!scoreBuckets.Contains(observed, StringComparer.OrdinalIgnoreCase))
                scoreBuckets.Add(observed);
        }

        var settings = new StateWeibullCurveFitSettings
        {
            MinMuFullBucketExposures = options.MinMuFullBucketExposures,
            MinMuGoals = options.MinMuGoals,
            MinKFullBucketExposures = options.MinKFullBucketExposures,
            MinKGoals = options.MinKGoals,
            MinK = options.MinK,
            MaxK = options.MaxK,
            KStep = options.KStep,
            DefaultK = options.DefaultK,
            SparseFallbackPolicy = "league_time_bucket"
        };

        Dictionary<string, StateWeibullTimeFallbackCurve> timeFallbacks = BuildTimeFallbacks(rows, timeBuckets, options);
        var curves = new List<StateWeibullCurve>();

        foreach (string scoreBucket in scoreBuckets)
        {
            foreach (StateWeibullCurveBucketInfo timeBucket in timeBuckets)
            {
                List<ExposureRow> bucketRows = rows
                    .Where(x => x.ScoreBucket.Equals(scoreBucket, StringComparison.OrdinalIgnoreCase)
                                && x.TimeBucket.Equals(timeBucket.TimeBucket, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                ExposureAggregate aggregate = Aggregate(bucketRows, timeBucket.StartMinute, timeBucket.EndMinute);
                StateWeibullTimeFallbackCurve fallback = timeFallbacks.TryGetValue(timeBucket.TimeBucket, out StateWeibullTimeFallbackCurve? foundFallback)
                    ? foundFallback
                    : BuildDefaultFallback(timeBucket, options);

                bool muReady = aggregate.FullBucketExposures >= options.MinMuFullBucketExposures && aggregate.GoalCount >= options.MinMuGoals;
                bool kReady = aggregate.FullBucketExposures >= options.MinKFullBucketExposures && aggregate.GoalCount >= options.MinKGoals;

                string status;
                string curveSource;
                string expectedGoalsSource;
                string shapeKSource;
                double expectedGoalsInBucket;
                double shapeK;
                double? rawK = null;
                string warning = string.Empty;

                if (!muReady)
                {
                    status = "UnsupportedSparse";
                    curveSource = "fallback_league_time_bucket";
                    expectedGoalsSource = "fallback_league_time_bucket";
                    shapeKSource = "fallback_league_time_bucket";
                    expectedGoalsInBucket = fallback.ExpectedGoalsInBucket;
                    shapeK = fallback.ShapeK;
                    warning = $"Exact bucket too sparse; fallback curve used. Exact bucket sample: {aggregate.FullBucketExposures.ToString("0.##", CultureInfo.InvariantCulture)} full-bucket exposures, {aggregate.GoalCount} goals.";
                }
                else if (!kReady)
                {
                    status = "PartialSupported";
                    curveSource = "exact_mu_fallback_k";
                    expectedGoalsSource = "exact_score_time_bucket";
                    shapeKSource = "fallback_league_time_bucket";
                    expectedGoalsInBucket = aggregate.RawExpectedGoalsInBucket ?? fallback.ExpectedGoalsInBucket;
                    shapeK = fallback.ShapeK;
                    warning = $"Exact bucket has enough data for μ but not for k; fallback k used. Exact bucket sample: {aggregate.FullBucketExposures.ToString("0.##", CultureInfo.InvariantCulture)} full-bucket exposures, {aggregate.GoalCount} goals.";
                }
                else
                {
                    status = "ExactSupported";
                    curveSource = "exact_score_time_bucket";
                    expectedGoalsSource = "exact_score_time_bucket";
                    shapeKSource = "exact_score_time_bucket";
                    expectedGoalsInBucket = aggregate.RawExpectedGoalsInBucket ?? fallback.ExpectedGoalsInBucket;
                    rawK = FitShapeK(bucketRows, options.DefaultK, options.MinK, options.MaxK, options.KStep);
                    shapeK = rawK ?? fallback.ShapeK;
                }

                curves.Add(new StateWeibullCurve
                {
                    League = league,
                    ScoreBucket = scoreBucket,
                    TimeBucket = timeBucket.TimeBucket,
                    BucketStartMinute = timeBucket.StartMinute,
                    BucketEndMinute = timeBucket.EndMinute,
                    BucketLengthMinutes = timeBucket.LengthMinutes,
                    Status = status,
                    CurveSource = curveSource,
                    ExpectedGoalsSource = expectedGoalsSource,
                    ShapeKSource = shapeKSource,
                    FullBucketExposures = aggregate.FullBucketExposures,
                    ExposureMinutes = aggregate.ExposureMinutes,
                    GoalCount = aggregate.GoalCount,
                    RawExpectedGoalsInBucket = aggregate.RawExpectedGoalsInBucket,
                    ExpectedGoalsInBucket = expectedGoalsInBucket,
                    RawShapeK = rawK,
                    ShapeK = shapeK,
                    FallbackFullBucketExposures = fallback.FullBucketExposures,
                    FallbackExposureMinutes = fallback.ExposureMinutes,
                    FallbackGoalCount = fallback.GoalCount,
                    FallbackExpectedGoalsInBucket = fallback.ExpectedGoalsInBucket,
                    FallbackShapeK = fallback.ShapeK,
                    Warning = warning
                });
            }
        }

        var curveSet = new StateWeibullCurveSet
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SourceExposureFile = Path.GetFullPath(options.InputPath),
            League = league,
            ScoreBuckets = scoreBuckets,
            TimeBuckets = timeBuckets,
            Settings = settings,
            TimeFallbacks = timeFallbacks.Values
                .OrderBy(x => x.BucketStartMinute)
                .ThenBy(x => x.BucketEndMinute)
                .ToList(),
            Curves = curves
                .OrderBy(x => x.BucketStartMinute)
                .ThenBy(x => x.ScoreBucket)
                .ToList()
        };

        await WriteJsonAsync(curveSet, options.OutputPath, cancellationToken);
        await WriteSummaryCsvAsync(curveSet.Curves, options.SummaryPath, cancellationToken);

        return new StateWeibullCurveFitResult
        {
            ExposureRowsRead = rows.Count,
            CurvesWritten = curves.Count,
            ExactSupported = curves.Count(x => x.Status == "ExactSupported"),
            PartialSupported = curves.Count(x => x.Status == "PartialSupported"),
            UnsupportedSparse = curves.Count(x => x.Status == "UnsupportedSparse"),
            TimeFallbacksWritten = curveSet.TimeFallbacks.Count,
            OutputPath = Path.GetFullPath(options.OutputPath),
            SummaryPath = Path.GetFullPath(options.SummaryPath)
        };
    }

    private static Dictionary<string, StateWeibullTimeFallbackCurve> BuildTimeFallbacks(
        IReadOnlyList<ExposureRow> rows,
        IReadOnlyList<StateWeibullCurveBucketInfo> timeBuckets,
        StateWeibullCurveFitterOptions options)
    {
        var result = new Dictionary<string, StateWeibullTimeFallbackCurve>(StringComparer.OrdinalIgnoreCase);

        foreach (StateWeibullCurveBucketInfo timeBucket in timeBuckets)
        {
            List<ExposureRow> timeRows = rows
                .Where(x => x.TimeBucket.Equals(timeBucket.TimeBucket, StringComparison.OrdinalIgnoreCase))
                .ToList();

            ExposureAggregate aggregate = Aggregate(timeRows, timeBucket.StartMinute, timeBucket.EndMinute);
            double expectedGoals = aggregate.RawExpectedGoalsInBucket ?? 0.0;
            double? fittedK = aggregate.GoalCount > 0
                ? FitShapeK(timeRows, options.DefaultK, options.MinK, options.MaxK, options.KStep)
                : null;

            result[timeBucket.TimeBucket] = new StateWeibullTimeFallbackCurve
            {
                TimeBucket = timeBucket.TimeBucket,
                BucketStartMinute = timeBucket.StartMinute,
                BucketEndMinute = timeBucket.EndMinute,
                BucketLengthMinutes = timeBucket.LengthMinutes,
                FullBucketExposures = aggregate.FullBucketExposures,
                ExposureMinutes = aggregate.ExposureMinutes,
                GoalCount = aggregate.GoalCount,
                ExpectedGoalsInBucket = expectedGoals,
                ShapeK = fittedK ?? options.DefaultK,
                ShapeKSource = fittedK.HasValue ? "league_time_bucket" : "default_k"
            };
        }

        return result;
    }

    private static StateWeibullTimeFallbackCurve BuildDefaultFallback(
        StateWeibullCurveBucketInfo timeBucket,
        StateWeibullCurveFitterOptions options)
        => new()
        {
            TimeBucket = timeBucket.TimeBucket,
            BucketStartMinute = timeBucket.StartMinute,
            BucketEndMinute = timeBucket.EndMinute,
            BucketLengthMinutes = timeBucket.LengthMinutes,
            FullBucketExposures = 0.0,
            ExposureMinutes = 0.0,
            GoalCount = 0,
            ExpectedGoalsInBucket = 0.0,
            ShapeK = options.DefaultK,
            ShapeKSource = "default_k"
        };

    private static ExposureAggregate Aggregate(
        IReadOnlyList<ExposureRow> rows,
        double bucketStartMinute,
        double bucketEndMinute)
    {
        double exposureMinutes = rows.Sum(x => x.ExposureMinutes);
        double length = Math.Max(Epsilon, bucketEndMinute - bucketStartMinute);
        double fullBucketExposures = exposureMinutes / length;
        int goals = rows.Count(x => x.GoalHappened);

        return new ExposureAggregate(
            exposureMinutes,
            fullBucketExposures,
            goals,
            fullBucketExposures > Epsilon ? goals / fullBucketExposures : null);
    }

    private static double? FitShapeK(
        IReadOnlyList<ExposureRow> rows,
        double defaultK,
        double minK,
        double maxK,
        double kStep)
    {
        int goalCount = rows.Count(x => x.GoalHappened);
        if (goalCount <= 0)
            return null;

        double bestK = defaultK;
        double bestLogLikelihood = double.NegativeInfinity;

        int steps = (int)Math.Floor((maxK - minK) / kStep + 0.5);
        for (int i = 0; i <= steps; i++)
        {
            double k = minK + i * kStep;
            if (k <= 0)
                continue;

            double transformedExposure = 0.0;
            double eventShapeLog = 0.0;
            bool valid = true;

            foreach (ExposureRow row in rows)
            {
                double length = Math.Max(Epsilon, row.BucketEndMinute - row.BucketStartMinute);
                double startX = Math.Clamp((row.StartMinute - row.BucketStartMinute) / length, 0.0, 1.0);
                double endX = Math.Clamp((row.EndMinute - row.BucketStartMinute) / length, 0.0, 1.0);
                if (endX <= startX + Epsilon)
                    continue;

                transformedExposure += Math.Pow(endX, k) - Math.Pow(startX, k);

                if (row.GoalHappened)
                {
                    double goalMinute = row.GoalMinute ?? row.EndMinute;
                    double goalX = Math.Clamp((goalMinute - row.BucketStartMinute) / length, 0.001, 1.0);
                    double shapeRate = k / length * Math.Pow(goalX, k - 1.0);
                    if (shapeRate <= 0 || double.IsNaN(shapeRate) || double.IsInfinity(shapeRate))
                    {
                        valid = false;
                        break;
                    }

                    eventShapeLog += Math.Log(shapeRate);
                }
            }

            if (!valid || transformedExposure <= Epsilon)
                continue;

            double mu = goalCount / transformedExposure;
            if (mu <= 0 || double.IsNaN(mu) || double.IsInfinity(mu))
                continue;

            double logLikelihood = goalCount * Math.Log(mu) + eventShapeLog - mu * transformedExposure;
            if (logLikelihood > bestLogLikelihood)
            {
                bestLogLikelihood = logLikelihood;
                bestK = k;
            }
        }

        if (double.IsNegativeInfinity(bestLogLikelihood))
            return null;

        return Math.Round(bestK, 6);
    }

    private static async Task<List<ExposureRow>> ReadExposureRowsAsync(string path, CancellationToken cancellationToken)
    {
        var rows = new List<ExposureRow>();
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            return rows;

        List<string> headers = ParseCsvLine(headerLine);
        var indexes = headers
            .Select((name, index) => (name: name.Trim(), index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);

        int lineNumber = 1;
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync(cancellationToken);
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            List<string> values = ParseCsvLine(line);
            try
            {
                rows.Add(new ExposureRow
                {
                    League = Get(values, indexes, "league"),
                    LeagueSlug = Get(values, indexes, "league_slug"),
                    TimeBucket = Get(values, indexes, "time_bucket"),
                    BucketStartMinute = GetDouble(values, indexes, "bucket_start_minute"),
                    BucketEndMinute = GetDouble(values, indexes, "bucket_end_minute"),
                    ScoreBucket = Get(values, indexes, "score_bucket"),
                    StartMinute = GetDouble(values, indexes, "start_minute"),
                    EndMinute = GetDouble(values, indexes, "end_minute"),
                    ExposureMinutes = GetDouble(values, indexes, "exposure_minutes"),
                    GoalHappened = GetBool(values, indexes, "goal_happened"),
                    GoalMinute = GetNullableDouble(values, indexes, "goal_minute")
                });
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is IndexOutOfRangeException)
            {
                throw new ArgumentException($"Invalid exposure CSV row at line {lineNumber}: {ex.Message}", ex);
            }
        }

        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var value = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    value.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                values.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(ch);
            }
        }

        values.Add(value.ToString());
        return values;
    }

    private static string Get(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> indexes, string name)
    {
        if (!indexes.TryGetValue(name, out int index))
            throw new ArgumentException($"Missing required column '{name}'.");
        if (index < 0 || index >= values.Count)
            return string.Empty;
        return values[index];
    }

    private static double GetDouble(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> indexes, string name)
    {
        string value = Get(values, indexes, name);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : throw new FormatException($"Column '{name}' must be a number, value was '{value}'.");
    }

    private static double? GetNullableDouble(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> indexes, string name)
    {
        string value = Get(values, indexes, name);
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : throw new FormatException($"Column '{name}' must be a number or empty, value was '{value}'.");
    }

    private static bool GetBool(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> indexes, string name)
    {
        string value = Get(values, indexes, name);
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveLeague(IReadOnlyList<ExposureRow> rows)
    {
        string? league = rows.Select(x => x.LeagueSlug).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (!string.IsNullOrWhiteSpace(league))
            return league;

        league = rows.Select(x => x.League).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return league ?? string.Empty;
    }

    private static async Task WriteJsonAsync(StateWeibullCurveSet curveSet, string outputPath, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(curveSet, options), Encoding.UTF8, cancellationToken);
    }

    private static async Task WriteSummaryCsvAsync(IReadOnlyList<StateWeibullCurve> curves, string outputPath, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        builder.AppendLine("league,score_bucket,time_bucket,bucket_start_minute,bucket_end_minute,status,curve_source,mu_source,k_source,full_bucket_exposures,exposure_minutes,goal_count,raw_mu,final_mu,raw_k,final_k,fallback_full_bucket_exposures,fallback_goals,fallback_mu,fallback_k,warning");

        foreach (StateWeibullCurve curve in curves.OrderBy(x => x.BucketStartMinute).ThenBy(x => x.ScoreBucket))
        {
            builder.Append(Csv(curve.League)); builder.Append(',');
            builder.Append(Csv(curve.ScoreBucket)); builder.Append(',');
            builder.Append(Csv(curve.TimeBucket)); builder.Append(',');
            builder.Append(FormatDouble(curve.BucketStartMinute)); builder.Append(',');
            builder.Append(FormatDouble(curve.BucketEndMinute)); builder.Append(',');
            builder.Append(Csv(curve.Status)); builder.Append(',');
            builder.Append(Csv(curve.CurveSource)); builder.Append(',');
            builder.Append(Csv(curve.ExpectedGoalsSource)); builder.Append(',');
            builder.Append(Csv(curve.ShapeKSource)); builder.Append(',');
            builder.Append(FormatDouble(curve.FullBucketExposures)); builder.Append(',');
            builder.Append(FormatDouble(curve.ExposureMinutes)); builder.Append(',');
            builder.Append(curve.GoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(curve.RawExpectedGoalsInBucket.HasValue ? FormatDouble(curve.RawExpectedGoalsInBucket.Value) : string.Empty); builder.Append(',');
            builder.Append(FormatDouble(curve.ExpectedGoalsInBucket)); builder.Append(',');
            builder.Append(curve.RawShapeK.HasValue ? FormatDouble(curve.RawShapeK.Value) : string.Empty); builder.Append(',');
            builder.Append(FormatDouble(curve.ShapeK)); builder.Append(',');
            builder.Append(FormatDouble(curve.FallbackFullBucketExposures)); builder.Append(',');
            builder.Append(curve.FallbackGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(FormatDouble(curve.FallbackExpectedGoalsInBucket)); builder.Append(',');
            builder.Append(FormatDouble(curve.FallbackShapeK)); builder.Append(',');
            builder.Append(Csv(curve.Warning));
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(fullPath, builder.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static string FormatDouble(double value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Csv(string value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private sealed record ExposureAggregate(
        double ExposureMinutes,
        double FullBucketExposures,
        int GoalCount,
        double? RawExpectedGoalsInBucket);

    private sealed class ExposureRow
    {
        public string League { get; init; } = string.Empty;
        public string LeagueSlug { get; init; } = string.Empty;
        public string TimeBucket { get; init; } = string.Empty;
        public double BucketStartMinute { get; init; }
        public double BucketEndMinute { get; init; }
        public string ScoreBucket { get; init; } = string.Empty;
        public double StartMinute { get; init; }
        public double EndMinute { get; init; }
        public double ExposureMinutes { get; init; }
        public bool GoalHappened { get; init; }
        public double? GoalMinute { get; init; }
    }
}
