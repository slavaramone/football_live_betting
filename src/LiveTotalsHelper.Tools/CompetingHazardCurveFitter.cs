using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Core.MonteCarlo;

namespace LiveTotalsHelper.Tools;

public sealed class CompetingHazardCurveFitterOptions
{
    public string InputPath { get; init; } = "outputs/calibration/state-weibull-exposures.csv";
    public string OutputPath { get; init; } = "outputs/calibration/competing-hazard-curves.json";
    public string SummaryPath { get; init; } = "outputs/calibration/competing-hazard-curves-summary.csv";
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

public sealed class CompetingHazardCurveFitResult
{
    public int ExposureRowsRead { get; init; }
    public int GoalRowsRead { get; init; }
    public int CurvesWritten { get; init; }
    public int SideCurvesWritten { get; init; }
    public int ExactSupported { get; init; }
    public int PartialSupported { get; init; }
    public int UnsupportedSparse { get; init; }
    public int TimeFallbacksWritten { get; init; }
    public int NeutralTimeFallbacksWritten { get; init; }
    public string OutputPath { get; init; } = string.Empty;
    public string SummaryPath { get; init; } = string.Empty;
}

public sealed class CompetingHazardCurveFitter
{
    private const double Epsilon = 0.000001;

    public async Task<CompetingHazardCurveFitResult> FitAsync(
        CompetingHazardCurveFitterOptions options,
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

        if (timeBuckets.Count == 0)
            throw new ArgumentException("Exposure CSV contains no time buckets.");

        List<string> directionalBuckets = StateWeibullScoreBucketer.StandardDirectionalBuckets.ToList();
        foreach (string observed in rows.Select(x => x.DirectionalScoreBucket).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        {
            if (!directionalBuckets.Contains(observed, StringComparer.OrdinalIgnoreCase))
                directionalBuckets.Add(observed);
        }

        Dictionary<string, CompetingHazardFallbackCurve> timeFallbacks = BuildTimeFallbacks(rows, timeBuckets, options);
        Dictionary<string, CompetingHazardFallbackCurve> neutralTimeFallbacks = BuildNeutralTimeFallbacks(rows, timeBuckets, options);
        var curves = new List<CompetingHazardCurve>();

        foreach (string directionalBucket in directionalBuckets)
        {
            foreach (StateWeibullCurveBucketInfo timeBucket in timeBuckets)
            {
                List<ExposureRow> bucketRows = rows
                    .Where(x => x.DirectionalScoreBucket.Equals(directionalBucket, StringComparison.OrdinalIgnoreCase)
                                && x.TimeBucket.Equals(timeBucket.TimeBucket, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                ExposureRow? sample = bucketRows.FirstOrDefault()
                    ?? rows.FirstOrDefault(x => x.DirectionalScoreBucket.Equals(directionalBucket, StringComparison.OrdinalIgnoreCase));

                string neutralBucket = sample?.NeutralScoreBucket ?? NeutralFromDirectional(directionalBucket);
                string pressureBucket = sample?.PressureBucket ?? PressureFromDirectional(directionalBucket);
                string neutralTimeKey = Key(neutralBucket, timeBucket.TimeBucket);

                CompetingHazardFallbackCurve? neutralFallback = neutralTimeFallbacks.TryGetValue(neutralTimeKey, out CompetingHazardFallbackCurve? foundNeutral)
                    ? foundNeutral
                    : null;
                CompetingHazardFallbackCurve timeFallback = timeFallbacks.TryGetValue(timeBucket.TimeBucket, out CompetingHazardFallbackCurve? foundTime)
                    ? foundTime
                    : BuildDefaultFallback("time_bucket", timeBucket, options);

                CompetingHazardSideCurve home = BuildResolvedSideCurve(
                    "home",
                    bucketRows,
                    timeBucket,
                    neutralFallback?.Home,
                    timeFallback.Home,
                    options);

                CompetingHazardSideCurve away = BuildResolvedSideCurve(
                    "away",
                    bucketRows,
                    timeBucket,
                    neutralFallback?.Away,
                    timeFallback.Away,
                    options);

                curves.Add(new CompetingHazardCurve
                {
                    League = league,
                    DirectionalScoreBucket = directionalBucket,
                    NeutralScoreBucket = neutralBucket,
                    PressureBucket = pressureBucket,
                    TimeBucket = timeBucket.TimeBucket,
                    BucketStartMinute = timeBucket.StartMinute,
                    BucketEndMinute = timeBucket.EndMinute,
                    BucketLengthMinutes = timeBucket.LengthMinutes,
                    Home = home,
                    Away = away
                });
            }
        }

        var model = new CompetingHazardCurveSet
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SourceExposureFile = Path.GetFullPath(options.InputPath),
            League = league,
            DirectionalScoreBuckets = directionalBuckets,
            TimeBuckets = timeBuckets,
            Settings = new CompetingHazardCurveFitSettings
            {
                MinMuFullBucketExposures = options.MinMuFullBucketExposures,
                MinMuGoals = options.MinMuGoals,
                MinKFullBucketExposures = options.MinKFullBucketExposures,
                MinKGoals = options.MinKGoals,
                MinK = options.MinK,
                MaxK = options.MaxK,
                KStep = options.KStep,
                DefaultK = options.DefaultK
            },
            TimeFallbacks = timeFallbacks.Values
                .OrderBy(x => x.BucketStartMinute)
                .ThenBy(x => x.BucketEndMinute)
                .ToList(),
            NeutralScoreTimeFallbacks = neutralTimeFallbacks.Values
                .OrderBy(x => x.BucketStartMinute)
                .ThenBy(x => x.NeutralScoreBucket)
                .ToList(),
            Curves = curves
                .OrderBy(x => x.BucketStartMinute)
                .ThenBy(x => x.DirectionalScoreBucket)
                .ToList()
        };

        await WriteJsonAsync(model, options.OutputPath, cancellationToken);
        await WriteSummaryCsvAsync(model.Curves, options.SummaryPath, cancellationToken);

        List<CompetingHazardSideCurve> sideCurves = model.Curves.SelectMany(x => new[] { x.Home, x.Away }).ToList();
        return new CompetingHazardCurveFitResult
        {
            ExposureRowsRead = rows.Count,
            GoalRowsRead = rows.Count(x => x.GoalHappened),
            CurvesWritten = model.Curves.Count,
            SideCurvesWritten = sideCurves.Count,
            ExactSupported = sideCurves.Count(x => x.Status == "ExactSupported"),
            PartialSupported = sideCurves.Count(x => x.Status == "PartialSupported"),
            UnsupportedSparse = sideCurves.Count(x => x.Status == "UnsupportedSparse"),
            TimeFallbacksWritten = model.TimeFallbacks.Count,
            NeutralTimeFallbacksWritten = model.NeutralScoreTimeFallbacks.Count,
            OutputPath = Path.GetFullPath(options.OutputPath),
            SummaryPath = Path.GetFullPath(options.SummaryPath)
        };
    }

    private static Dictionary<string, CompetingHazardFallbackCurve> BuildTimeFallbacks(
        IReadOnlyList<ExposureRow> rows,
        IReadOnlyList<StateWeibullCurveBucketInfo> timeBuckets,
        CompetingHazardCurveFitterOptions options)
    {
        var result = new Dictionary<string, CompetingHazardFallbackCurve>(StringComparer.OrdinalIgnoreCase);
        foreach (StateWeibullCurveBucketInfo timeBucket in timeBuckets)
        {
            List<ExposureRow> timeRows = rows
                .Where(x => x.TimeBucket.Equals(timeBucket.TimeBucket, StringComparison.OrdinalIgnoreCase))
                .ToList();

            result[timeBucket.TimeBucket] = new CompetingHazardFallbackCurve
            {
                Key = timeBucket.TimeBucket,
                Source = "league_time_bucket",
                TimeBucket = timeBucket.TimeBucket,
                BucketStartMinute = timeBucket.StartMinute,
                BucketEndMinute = timeBucket.EndMinute,
                BucketLengthMinutes = timeBucket.LengthMinutes,
                Home = BuildFallbackSideCurve("home", timeRows, timeBucket, "league_time_bucket", options),
                Away = BuildFallbackSideCurve("away", timeRows, timeBucket, "league_time_bucket", options)
            };
        }

        return result;
    }

    private static Dictionary<string, CompetingHazardFallbackCurve> BuildNeutralTimeFallbacks(
        IReadOnlyList<ExposureRow> rows,
        IReadOnlyList<StateWeibullCurveBucketInfo> timeBuckets,
        CompetingHazardCurveFitterOptions options)
    {
        var result = new Dictionary<string, CompetingHazardFallbackCurve>(StringComparer.OrdinalIgnoreCase);
        List<string> neutralBuckets = StateWeibullScoreBucketer.StandardBuckets.ToList();
        foreach (string observed in rows.Select(x => x.NeutralScoreBucket).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        {
            if (!neutralBuckets.Contains(observed, StringComparer.OrdinalIgnoreCase))
                neutralBuckets.Add(observed);
        }

        foreach (string neutralBucket in neutralBuckets)
        {
            foreach (StateWeibullCurveBucketInfo timeBucket in timeBuckets)
            {
                List<ExposureRow> matchingRows = rows
                    .Where(x => x.NeutralScoreBucket.Equals(neutralBucket, StringComparison.OrdinalIgnoreCase)
                                && x.TimeBucket.Equals(timeBucket.TimeBucket, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                string key = Key(neutralBucket, timeBucket.TimeBucket);
                result[key] = new CompetingHazardFallbackCurve
                {
                    Key = key,
                    Source = "neutral_score_time",
                    NeutralScoreBucket = neutralBucket,
                    TimeBucket = timeBucket.TimeBucket,
                    BucketStartMinute = timeBucket.StartMinute,
                    BucketEndMinute = timeBucket.EndMinute,
                    BucketLengthMinutes = timeBucket.LengthMinutes,
                    Home = BuildFallbackSideCurve("home", matchingRows, timeBucket, "neutral_score_time", options),
                    Away = BuildFallbackSideCurve("away", matchingRows, timeBucket, "neutral_score_time", options)
                };
            }
        }

        return result;
    }

    private static CompetingHazardFallbackCurve BuildDefaultFallback(
        string source,
        StateWeibullCurveBucketInfo timeBucket,
        CompetingHazardCurveFitterOptions options)
        => new()
        {
            Key = source,
            Source = source,
            TimeBucket = timeBucket.TimeBucket,
            BucketStartMinute = timeBucket.StartMinute,
            BucketEndMinute = timeBucket.EndMinute,
            BucketLengthMinutes = timeBucket.LengthMinutes,
            Home = BuildDefaultSideCurve("home", source, options),
            Away = BuildDefaultSideCurve("away", source, options)
        };

    private static CompetingHazardSideCurve BuildDefaultSideCurve(string side, string source, CompetingHazardCurveFitterOptions options)
        => new()
        {
            Side = side,
            Status = "DefaultFallback",
            CurveSource = source,
            ExpectedGoalsSource = source,
            ShapeKSource = "default_k",
            ExpectedGoalsInBucket = 0.0,
            ShapeK = options.DefaultK,
            FallbackSource = source
        };

    private static CompetingHazardSideCurve BuildFallbackSideCurve(
        string side,
        IReadOnlyList<ExposureRow> rows,
        StateWeibullCurveBucketInfo timeBucket,
        string source,
        CompetingHazardCurveFitterOptions options)
    {
        ExposureAggregate aggregate = Aggregate(rows, side, timeBucket.StartMinute, timeBucket.EndMinute);
        double? rawK = aggregate.GoalCount > 0
            ? FitShapeK(rows, side, options.DefaultK, options.MinK, options.MaxK, options.KStep)
            : null;

        return new CompetingHazardSideCurve
        {
            Side = side,
            Status = aggregate.GoalCount > 0 ? "FallbackAvailable" : "FallbackSparse",
            CurveSource = source,
            ExpectedGoalsSource = source,
            ShapeKSource = rawK.HasValue ? source : "default_k",
            FullBucketExposures = aggregate.FullBucketExposures,
            ExposureMinutes = aggregate.ExposureMinutes,
            GoalCount = aggregate.GoalCount,
            RawExpectedGoalsInBucket = aggregate.RawExpectedGoalsInBucket,
            ExpectedGoalsInBucket = aggregate.RawExpectedGoalsInBucket ?? 0.0,
            RawShapeK = rawK,
            ShapeK = rawK ?? options.DefaultK,
            FallbackSource = source
        };
    }

    private static CompetingHazardSideCurve BuildResolvedSideCurve(
        string side,
        IReadOnlyList<ExposureRow> rows,
        StateWeibullCurveBucketInfo timeBucket,
        CompetingHazardSideCurve? neutralFallback,
        CompetingHazardSideCurve timeFallback,
        CompetingHazardCurveFitterOptions options)
    {
        ExposureAggregate aggregate = Aggregate(rows, side, timeBucket.StartMinute, timeBucket.EndMinute);
        bool muReady = aggregate.FullBucketExposures >= options.MinMuFullBucketExposures && aggregate.GoalCount >= options.MinMuGoals;
        bool kReady = aggregate.FullBucketExposures >= options.MinKFullBucketExposures && aggregate.GoalCount >= options.MinKGoals;

        CompetingHazardSideCurve muFallback = SelectMuFallback(neutralFallback, timeFallback, options);
        CompetingHazardSideCurve kFallback = SelectKFallback(neutralFallback, timeFallback, options);

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
            curveSource = $"fallback_{muFallback.FallbackSource}";
            expectedGoalsSource = $"fallback_{muFallback.FallbackSource}";
            shapeKSource = $"fallback_{kFallback.FallbackSource}";
            expectedGoalsInBucket = muFallback.ExpectedGoalsInBucket;
            shapeK = kFallback.ShapeK;
            warning = $"{side}: exact directional/time bucket too sparse; fallback μ from {muFallback.FallbackSource}, fallback k from {kFallback.FallbackSource}. Exact sample: {aggregate.FullBucketExposures.ToString("0.##", CultureInfo.InvariantCulture)} full-bucket exposures, {aggregate.GoalCount} {side} goals.";
        }
        else if (!kReady)
        {
            status = "PartialSupported";
            curveSource = "exact_mu_fallback_k";
            expectedGoalsSource = "exact_directional_time";
            shapeKSource = $"fallback_{kFallback.FallbackSource}";
            expectedGoalsInBucket = aggregate.RawExpectedGoalsInBucket ?? muFallback.ExpectedGoalsInBucket;
            shapeK = kFallback.ShapeK;
            warning = $"{side}: exact directional/time bucket has enough data for μ but not for k; fallback k from {kFallback.FallbackSource}. Exact sample: {aggregate.FullBucketExposures.ToString("0.##", CultureInfo.InvariantCulture)} full-bucket exposures, {aggregate.GoalCount} {side} goals.";
        }
        else
        {
            status = "ExactSupported";
            curveSource = "exact_directional_time";
            expectedGoalsSource = "exact_directional_time";
            shapeKSource = "exact_directional_time";
            expectedGoalsInBucket = aggregate.RawExpectedGoalsInBucket ?? muFallback.ExpectedGoalsInBucket;
            rawK = FitShapeK(rows, side, options.DefaultK, options.MinK, options.MaxK, options.KStep);
            shapeK = rawK ?? kFallback.ShapeK;
        }

        return new CompetingHazardSideCurve
        {
            Side = side,
            Status = status,
            CurveSource = curveSource,
            ExpectedGoalsSource = expectedGoalsSource,
            ShapeKSource = shapeKSource,
            FullBucketExposures = aggregate.FullBucketExposures,
            ExposureMinutes = aggregate.ExposureMinutes,
            GoalCount = aggregate.GoalCount,
            RawExpectedGoalsInBucket = aggregate.RawExpectedGoalsInBucket,
            ExpectedGoalsInBucket = Math.Max(0.0, expectedGoalsInBucket),
            RawShapeK = rawK,
            ShapeK = shapeK,
            FallbackFullBucketExposures = muFallback.FullBucketExposures,
            FallbackExposureMinutes = muFallback.ExposureMinutes,
            FallbackGoalCount = muFallback.GoalCount,
            FallbackExpectedGoalsInBucket = muFallback.ExpectedGoalsInBucket,
            FallbackShapeK = kFallback.ShapeK,
            FallbackSource = muFallback.FallbackSource,
            Warning = warning
        };
    }

    private static CompetingHazardSideCurve SelectMuFallback(
        CompetingHazardSideCurve? neutralFallback,
        CompetingHazardSideCurve timeFallback,
        CompetingHazardCurveFitterOptions options)
    {
        if (neutralFallback is not null
            && neutralFallback.FullBucketExposures >= options.MinMuFullBucketExposures
            && neutralFallback.GoalCount >= options.MinMuGoals)
            return neutralFallback;

        return timeFallback;
    }

    private static CompetingHazardSideCurve SelectKFallback(
        CompetingHazardSideCurve? neutralFallback,
        CompetingHazardSideCurve timeFallback,
        CompetingHazardCurveFitterOptions options)
    {
        if (neutralFallback is not null
            && neutralFallback.FullBucketExposures >= options.MinKFullBucketExposures
            && neutralFallback.GoalCount >= options.MinKGoals)
            return neutralFallback;

        if (timeFallback.FullBucketExposures >= options.MinKFullBucketExposures
            && timeFallback.GoalCount >= options.MinKGoals)
            return timeFallback;

        return timeFallback;
    }

    private static ExposureAggregate Aggregate(
        IReadOnlyList<ExposureRow> rows,
        string side,
        double bucketStartMinute,
        double bucketEndMinute)
    {
        double exposureMinutes = rows.Sum(x => x.ExposureMinutes);
        double length = Math.Max(Epsilon, bucketEndMinute - bucketStartMinute);
        double fullBucketExposures = exposureMinutes / length;
        int goals = rows.Count(x => x.GoalHappened && x.GoalSide.Equals(side, StringComparison.OrdinalIgnoreCase));

        return new ExposureAggregate(
            exposureMinutes,
            fullBucketExposures,
            goals,
            fullBucketExposures > Epsilon ? goals / fullBucketExposures : null);
    }

    private static double? FitShapeK(
        IReadOnlyList<ExposureRow> rows,
        string side,
        double defaultK,
        double minK,
        double maxK,
        double kStep)
    {
        int goalCount = rows.Count(x => x.GoalHappened && x.GoalSide.Equals(side, StringComparison.OrdinalIgnoreCase));
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

                if (row.GoalHappened && row.GoalSide.Equals(side, StringComparison.OrdinalIgnoreCase))
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

        string[] required =
        [
            "league", "league_slug", "time_bucket", "bucket_start_minute", "bucket_end_minute",
            "score_bucket", "home_goals_at_start", "away_goals_at_start", "start_minute",
            "end_minute", "exposure_minutes", "goal_happened", "goal_minute", "goal_side"
        ];

        foreach (string column in required)
        {
            if (!indexes.ContainsKey(column))
                throw new ArgumentException($"Exposure CSV missing required column '{column}'.");
        }

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
                int homeGoals = GetInt(values, indexes, "home_goals_at_start");
                int awayGoals = GetInt(values, indexes, "away_goals_at_start");
                string neutral = Get(values, indexes, "score_bucket");
                if (string.IsNullOrWhiteSpace(neutral))
                    neutral = StateWeibullScoreBucketer.ResolveScoreBucket(homeGoals, awayGoals);

                rows.Add(new ExposureRow
                {
                    League = Get(values, indexes, "league"),
                    LeagueSlug = Get(values, indexes, "league_slug"),
                    TimeBucket = Get(values, indexes, "time_bucket"),
                    BucketStartMinute = GetDouble(values, indexes, "bucket_start_minute"),
                    BucketEndMinute = GetDouble(values, indexes, "bucket_end_minute"),
                    NeutralScoreBucket = neutral,
                    DirectionalScoreBucket = StateWeibullScoreBucketer.ResolveDirectionalScoreBucket(homeGoals, awayGoals),
                    PressureBucket = StateWeibullScoreBucketer.ResolvePressureBucket(homeGoals, awayGoals),
                    HomeGoalsAtStart = homeGoals,
                    AwayGoalsAtStart = awayGoals,
                    StartMinute = GetDouble(values, indexes, "start_minute"),
                    EndMinute = GetDouble(values, indexes, "end_minute"),
                    ExposureMinutes = GetDouble(values, indexes, "exposure_minutes"),
                    GoalHappened = GetBool(values, indexes, "goal_happened"),
                    GoalMinute = GetNullableDouble(values, indexes, "goal_minute"),
                    GoalSide = Get(values, indexes, "goal_side")
                });
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is IndexOutOfRangeException)
            {
                throw new ArgumentException($"Invalid exposure CSV row at line {lineNumber}: {ex.Message}", ex);
            }
        }

        return rows;
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

    private static string ResolveLeague(IReadOnlyList<ExposureRow> rows)
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

    private static async Task WriteJsonAsync(CompetingHazardCurveSet model, string outputPath, CancellationToken cancellationToken)
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

        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(model, options), Encoding.UTF8, cancellationToken);
    }

    private static async Task WriteSummaryCsvAsync(IReadOnlyList<CompetingHazardCurve> curves, string outputPath, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        builder.AppendLine("league,directional_score_bucket,neutral_score_bucket,pressure_bucket,time_bucket,bucket_start_minute,bucket_end_minute,home_status,home_mu_source,home_k_source,home_full_bucket_exposures,home_exposure_minutes,home_goals,home_raw_mu,home_final_mu,home_raw_k,home_final_k,away_status,away_mu_source,away_k_source,away_full_bucket_exposures,away_exposure_minutes,away_goals,away_raw_mu,away_final_mu,away_raw_k,away_final_k,total_final_mu,p_home_goal_in_bucket,p_away_goal_in_bucket,warning");

        foreach (CompetingHazardCurve curve in curves.OrderBy(x => x.BucketStartMinute).ThenBy(x => x.DirectionalScoreBucket))
        {
            builder.Append(Csv(curve.League)); builder.Append(',');
            builder.Append(Csv(curve.DirectionalScoreBucket)); builder.Append(',');
            builder.Append(Csv(curve.NeutralScoreBucket)); builder.Append(',');
            builder.Append(Csv(curve.PressureBucket)); builder.Append(',');
            builder.Append(Csv(curve.TimeBucket)); builder.Append(',');
            builder.Append(Format(curve.BucketStartMinute)); builder.Append(',');
            builder.Append(Format(curve.BucketEndMinute)); builder.Append(',');
            AppendSide(builder, curve.Home);
            AppendSide(builder, curve.Away);
            builder.Append(Format(curve.ExpectedGoalsInBucket)); builder.Append(',');
            builder.Append(Format(curve.ProbabilityHomeGoalInBucket)); builder.Append(',');
            builder.Append(Format(curve.ProbabilityAwayGoalInBucket)); builder.Append(',');
            builder.Append(Csv(curve.Warning));
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(fullPath, builder.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static void AppendSide(StringBuilder builder, CompetingHazardSideCurve side)
    {
        builder.Append(Csv(side.Status)); builder.Append(',');
        builder.Append(Csv(side.ExpectedGoalsSource)); builder.Append(',');
        builder.Append(Csv(side.ShapeKSource)); builder.Append(',');
        builder.Append(Format(side.FullBucketExposures)); builder.Append(',');
        builder.Append(Format(side.ExposureMinutes)); builder.Append(',');
        builder.Append(side.GoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
        builder.Append(side.RawExpectedGoalsInBucket.HasValue ? Format(side.RawExpectedGoalsInBucket.Value) : string.Empty); builder.Append(',');
        builder.Append(Format(side.ExpectedGoalsInBucket)); builder.Append(',');
        builder.Append(side.RawShapeK.HasValue ? Format(side.RawShapeK.Value) : string.Empty); builder.Append(',');
        builder.Append(Format(side.ShapeK)); builder.Append(',');
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

    private static int GetInt(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> indexes, string name)
    {
        string value = Get(values, indexes, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new FormatException($"Column '{name}' must be an integer, value was '{value}'.");
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

    private static string Key(string first, string second) => $"{first}|{second}";

    private static string Format(double value)
        => value.ToString("0.######", CultureInfo.InvariantCulture);

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
        public string NeutralScoreBucket { get; init; } = string.Empty;
        public string DirectionalScoreBucket { get; init; } = string.Empty;
        public string PressureBucket { get; init; } = string.Empty;
        public int HomeGoalsAtStart { get; init; }
        public int AwayGoalsAtStart { get; init; }
        public double StartMinute { get; init; }
        public double EndMinute { get; init; }
        public double ExposureMinutes { get; init; }
        public bool GoalHappened { get; init; }
        public double? GoalMinute { get; init; }
        public string GoalSide { get; init; } = string.Empty;
    }
}
