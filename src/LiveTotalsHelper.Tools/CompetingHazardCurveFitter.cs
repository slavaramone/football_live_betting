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

    public int MinExactGoals { get; init; } = 25;
    public int MinDirectionalOverallGoals { get; init; } = 50;
    public int MinPressureTimeGoals { get; init; } = 40;
    public int MinNeutralScoreTimeGoals { get; init; } = 25;
    public int MinTimeGoals { get; init; } = 50;
    public int MinLeagueGoals { get; init; } = 100;
    public double PriorWeightGoals { get; init; } = 6.0;
}

public sealed class CompetingHazardCurveFitResult
{
    public int ExposureRowsRead { get; init; }
    public int GoalRowsRead { get; init; }
    public int TotalCurvesWritten { get; init; }
    public int CurvesWritten { get; init; }
    public int TotalExactSupported { get; init; }
    public int TotalPartialSupported { get; init; }
    public int TotalUnsupportedSparse { get; init; }
    public int ScorerShareExactSupported { get; init; }
    public int ScorerShareDirectionalFallback { get; init; }
    public int ScorerSharePressureTimeFallback { get; init; }
    public int ScorerShareNeutralTimeFallback { get; init; }
    public int ScorerShareTimeFallback { get; init; }
    public int ScorerShareLeagueFallback { get; init; }
    public int ScorerShareRuleBasedFallback { get; init; }
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
        if (options.PriorWeightGoals < 0)
            throw new ArgumentException("Prior weight must be non-negative.", nameof(options));

        List<ExposureRow> rows = await ReadExposureRowsAsync(options.InputPath, cancellationToken);
        if (rows.Count == 0)
            throw new ArgumentException($"Exposure CSV contains no data rows: {options.InputPath}");

        List<ExposureRow> goalRows = rows
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

        if (timeBuckets.Count == 0)
            throw new ArgumentException("Exposure CSV contains no time buckets.");

        List<string> scoreBuckets = StateWeibullScoreBucketer.StandardBuckets.ToList();
        foreach (string observed in rows.Select(x => x.NeutralScoreBucket).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        {
            if (!scoreBuckets.Contains(observed, StringComparer.OrdinalIgnoreCase))
                scoreBuckets.Add(observed);
        }

        List<string> directionalBuckets = StateWeibullScoreBucketer.StandardDirectionalBuckets.ToList();
        foreach (string observed in rows.Select(x => x.DirectionalScoreBucket).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
        {
            if (!directionalBuckets.Contains(observed, StringComparer.OrdinalIgnoreCase))
                directionalBuckets.Add(observed);
        }

        StateWeibullCurveFitSettings totalSettings = new()
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

        NextGoalSideModelSettings scorerSettings = new()
        {
            MinExactGoals = options.MinExactGoals,
            MinDirectionalOverallGoals = options.MinDirectionalOverallGoals,
            MinPressureTimeGoals = options.MinPressureTimeGoals,
            MinNeutralScoreTimeGoals = options.MinNeutralScoreTimeGoals,
            MinTimeGoals = options.MinTimeGoals,
            MinLeagueGoals = options.MinLeagueGoals,
            PriorWeightGoals = options.PriorWeightGoals
        };

        Dictionary<string, StateWeibullTimeFallbackCurve> totalTimeFallbacks = BuildTotalTimeFallbacks(rows, timeBuckets, options);
        List<StateWeibullCurve> totalCurves = BuildTotalCurves(rows, scoreBuckets, timeBuckets, totalTimeFallbacks, league, options);
        Dictionary<string, StateWeibullCurve> totalByNeutralTime = totalCurves.ToDictionary(x => Key(x.ScoreBucket, x.TimeBucket), StringComparer.OrdinalIgnoreCase);

        NextGoalSideAggregate leagueShare = ToShareAggregate("league_overall", "league_overall", goalRows, 0.5, options.PriorWeightGoals);
        if (leagueShare.GoalCount == 0)
        {
            leagueShare = new NextGoalSideAggregate
            {
                Key = "league_overall",
                Source = "rule_based_default",
                ProbabilityHomeNextGoal = 0.5
            };
        }

        Dictionary<string, NextGoalSideAggregate> directionalOverall = BuildShareAggregateMap(
            goalRows,
            x => x.DirectionalScoreBucket,
            "directional_overall",
            leagueShare.ProbabilityHomeNextGoal,
            options.PriorWeightGoals);

        Dictionary<string, NextGoalSideAggregate> pressureTime = BuildShareAggregateMap(
            goalRows,
            x => Key(x.PressureBucket, x.TimeBucket),
            "pressure_time",
            leagueShare.ProbabilityHomeNextGoal,
            options.PriorWeightGoals);

        Dictionary<string, NextGoalSideAggregate> neutralScoreTime = BuildShareAggregateMap(
            goalRows,
            x => Key(x.NeutralScoreBucket, x.TimeBucket),
            "neutral_score_time",
            leagueShare.ProbabilityHomeNextGoal,
            options.PriorWeightGoals);

        Dictionary<string, NextGoalSideAggregate> timeShares = BuildShareAggregateMap(
            goalRows,
            x => x.TimeBucket,
            "time_bucket",
            leagueShare.ProbabilityHomeNextGoal,
            options.PriorWeightGoals);

        var curves = new List<CompetingHazardCurve>();

        foreach (string directionalBucket in directionalBuckets)
        {
            foreach (StateWeibullCurveBucketInfo timeBucket in timeBuckets)
            {
                List<ExposureRow> matchingRows = rows
                    .Where(x => x.DirectionalScoreBucket.Equals(directionalBucket, StringComparison.OrdinalIgnoreCase)
                                && x.TimeBucket.Equals(timeBucket.TimeBucket, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                ExposureRow? sample = matchingRows.FirstOrDefault()
                    ?? rows.FirstOrDefault(x => x.DirectionalScoreBucket.Equals(directionalBucket, StringComparison.OrdinalIgnoreCase));

                string neutralBucket = sample?.NeutralScoreBucket ?? NeutralFromDirectional(directionalBucket);
                string pressureBucket = sample?.PressureBucket ?? PressureFromDirectional(directionalBucket);
                StateWeibullCurve totalCurve = totalByNeutralTime.TryGetValue(Key(neutralBucket, timeBucket.TimeBucket), out StateWeibullCurve? foundTotal)
                    ? foundTotal
                    : BuildDefaultTotalCurve(league, neutralBucket, timeBucket, options);

                SideCount exact = CountSideGoals(matchingRows);
                double ruleBased = RuleBasedFromDirectional(directionalBucket);
                NextGoalSideAggregate fallback = ResolveShareFallback(
                    directionalBucket,
                    neutralBucket,
                    pressureBucket,
                    timeBucket.TimeBucket,
                    exact,
                    ruleBased,
                    directionalOverall,
                    pressureTime,
                    neutralScoreTime,
                    timeShares,
                    leagueShare,
                    options,
                    out string shareStatus,
                    out string shareSource,
                    out string shareWarning);

                double? exactRawProbabilityHome = exact.GoalCount > 0 ? exact.HomeGoalCount / (double)exact.GoalCount : null;
                double probabilityHome;
                if (exact.GoalCount >= options.MinExactGoals)
                {
                    shareStatus = "ExactSupported";
                    shareSource = "exact_directional_time";
                    probabilityHome = Smooth(exact.HomeGoalCount, exact.AwayGoalCount, fallback.ProbabilityHomeNextGoal, options.PriorWeightGoals);
                    shareWarning = string.Empty;
                }
                else
                {
                    probabilityHome = fallback.ProbabilityHomeNextGoal;
                }

                probabilityHome = ClampProbability(probabilityHome);
                double homeExpected = totalCurve.ExpectedGoalsInBucket * probabilityHome;
                double awayExpected = totalCurve.ExpectedGoalsInBucket * (1.0 - probabilityHome);

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
                    TotalStatus = totalCurve.Status,
                    TotalCurveSource = totalCurve.CurveSource,
                    TotalExpectedGoalsSource = totalCurve.ExpectedGoalsSource,
                    TotalShapeKSource = totalCurve.ShapeKSource,
                    TotalFullBucketExposures = totalCurve.FullBucketExposures,
                    TotalExposureMinutes = totalCurve.ExposureMinutes,
                    TotalGoalCount = totalCurve.GoalCount,
                    TotalRawExpectedGoalsInBucket = totalCurve.RawExpectedGoalsInBucket,
                    TotalExpectedGoalsInBucket = totalCurve.ExpectedGoalsInBucket,
                    TotalRawShapeK = totalCurve.RawShapeK,
                    TotalShapeK = totalCurve.ShapeK,
                    ScorerShareStatus = shareStatus,
                    ScorerShareSource = shareSource,
                    ProbabilityHomeGoalInBucket = probabilityHome,
                    ExactHomeGoalCount = exact.HomeGoalCount,
                    ExactAwayGoalCount = exact.AwayGoalCount,
                    ExactRawProbabilityHomeGoal = exactRawProbabilityHome,
                    FallbackScorerShareSource = fallback.Source,
                    FallbackHomeGoalCount = fallback.HomeGoalCount,
                    FallbackAwayGoalCount = fallback.AwayGoalCount,
                    FallbackProbabilityHomeGoal = fallback.ProbabilityHomeNextGoal,
                    RuleBasedProbabilityHomeGoal = ruleBased,
                    Home = new CompetingHazardSideSplit
                    {
                        Side = "home",
                        ProbabilityGoalInBucket = probabilityHome,
                        ExpectedGoalsInBucket = homeExpected,
                        ShapeK = totalCurve.ShapeK,
                        ExpectedGoalsSource = $"{totalCurve.ExpectedGoalsSource} * {shareSource}",
                        ShapeKSource = totalCurve.ShapeKSource
                    },
                    Away = new CompetingHazardSideSplit
                    {
                        Side = "away",
                        ProbabilityGoalInBucket = 1.0 - probabilityHome,
                        ExpectedGoalsInBucket = awayExpected,
                        ShapeK = totalCurve.ShapeK,
                        ExpectedGoalsSource = $"{totalCurve.ExpectedGoalsSource} * {shareSource}",
                        ShapeKSource = totalCurve.ShapeKSource
                    },
                    TotalWarning = totalCurve.Warning,
                    ScorerShareWarning = shareWarning
                });
            }
        }

        var model = new CompetingHazardCurveSet
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SourceExposureFile = Path.GetFullPath(options.InputPath),
            League = league,
            ScoreBuckets = scoreBuckets,
            DirectionalScoreBuckets = directionalBuckets,
            TimeBuckets = timeBuckets,
            Settings = new CompetingHazardFitSettings
            {
                TotalHazardFit = totalSettings,
                ScorerShareFit = scorerSettings
            },
            TotalTimeFallbacks = totalTimeFallbacks.Values
                .OrderBy(x => x.BucketStartMinute)
                .ThenBy(x => x.BucketEndMinute)
                .ToList(),
            TotalCurves = totalCurves
                .OrderBy(x => x.BucketStartMinute)
                .ThenBy(x => x.ScoreBucket)
                .ToList(),
            LeagueScorerShare = leagueShare,
            DirectionalScorerShares = directionalOverall.Values.OrderBy(x => x.Key).ToList(),
            PressureTimeScorerShares = pressureTime.Values.OrderBy(x => x.Key).ToList(),
            NeutralScoreTimeScorerShares = neutralScoreTime.Values.OrderBy(x => x.Key).ToList(),
            TimeScorerShares = timeShares.Values.OrderBy(x => x.Key).ToList(),
            Curves = curves
                .OrderBy(x => x.BucketStartMinute)
                .ThenBy(x => x.DirectionalScoreBucket)
                .ToList()
        };

        await WriteJsonAsync(model, options.OutputPath, cancellationToken);
        await WriteSummaryCsvAsync(model.Curves, options.SummaryPath, cancellationToken);

        return new CompetingHazardCurveFitResult
        {
            ExposureRowsRead = rows.Count,
            GoalRowsRead = goalRows.Count,
            TotalCurvesWritten = totalCurves.Count,
            CurvesWritten = model.Curves.Count,
            TotalExactSupported = totalCurves.Count(x => x.Status == "ExactSupported"),
            TotalPartialSupported = totalCurves.Count(x => x.Status == "PartialSupported"),
            TotalUnsupportedSparse = totalCurves.Count(x => x.Status == "UnsupportedSparse"),
            ScorerShareExactSupported = model.Curves.Count(x => x.ScorerShareStatus == "ExactSupported"),
            ScorerShareDirectionalFallback = model.Curves.Count(x => x.ScorerShareStatus == "DirectionalOverallFallback"),
            ScorerSharePressureTimeFallback = model.Curves.Count(x => x.ScorerShareStatus == "PressureTimeFallback"),
            ScorerShareNeutralTimeFallback = model.Curves.Count(x => x.ScorerShareStatus == "NeutralScoreTimeFallback"),
            ScorerShareTimeFallback = model.Curves.Count(x => x.ScorerShareStatus == "TimeBucketFallback"),
            ScorerShareLeagueFallback = model.Curves.Count(x => x.ScorerShareStatus == "LeagueOverallFallback"),
            ScorerShareRuleBasedFallback = model.Curves.Count(x => x.ScorerShareStatus == "RuleBasedFallback"),
            OutputPath = Path.GetFullPath(options.OutputPath),
            SummaryPath = Path.GetFullPath(options.SummaryPath)
        };
    }

    private static List<StateWeibullCurve> BuildTotalCurves(
        IReadOnlyList<ExposureRow> rows,
        IReadOnlyList<string> scoreBuckets,
        IReadOnlyList<StateWeibullCurveBucketInfo> timeBuckets,
        IReadOnlyDictionary<string, StateWeibullTimeFallbackCurve> timeFallbacks,
        string league,
        CompetingHazardCurveFitterOptions options)
    {
        var curves = new List<StateWeibullCurve>();
        foreach (string scoreBucket in scoreBuckets)
        {
            foreach (StateWeibullCurveBucketInfo timeBucket in timeBuckets)
            {
                List<ExposureRow> bucketRows = rows
                    .Where(x => x.NeutralScoreBucket.Equals(scoreBucket, StringComparison.OrdinalIgnoreCase)
                                && x.TimeBucket.Equals(timeBucket.TimeBucket, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                ExposureAggregate aggregate = Aggregate(bucketRows, timeBucket.StartMinute, timeBucket.EndMinute);
                StateWeibullTimeFallbackCurve fallback = timeFallbacks.TryGetValue(timeBucket.TimeBucket, out StateWeibullTimeFallbackCurve? foundFallback)
                    ? foundFallback
                    : BuildDefaultTotalFallback(timeBucket, options);

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
                    warning = $"Total hazard exact neutral/time bucket too sparse; fallback total curve used. Exact sample: {aggregate.FullBucketExposures.ToString("0.##", CultureInfo.InvariantCulture)} full-bucket exposures, {aggregate.GoalCount} goals.";
                }
                else if (!kReady)
                {
                    status = "PartialSupported";
                    curveSource = "exact_total_mu_fallback_k";
                    expectedGoalsSource = "exact_neutral_time_bucket";
                    shapeKSource = "fallback_league_time_bucket";
                    expectedGoalsInBucket = aggregate.RawExpectedGoalsInBucket ?? fallback.ExpectedGoalsInBucket;
                    shapeK = fallback.ShapeK;
                    warning = $"Total hazard exact neutral/time bucket has enough data for μ but not for k; fallback k used. Exact sample: {aggregate.FullBucketExposures.ToString("0.##", CultureInfo.InvariantCulture)} full-bucket exposures, {aggregate.GoalCount} goals.";
                }
                else
                {
                    status = "ExactSupported";
                    curveSource = "exact_neutral_time_bucket";
                    expectedGoalsSource = "exact_neutral_time_bucket";
                    shapeKSource = "exact_neutral_time_bucket";
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

        return curves;
    }

    private static Dictionary<string, StateWeibullTimeFallbackCurve> BuildTotalTimeFallbacks(
        IReadOnlyList<ExposureRow> rows,
        IReadOnlyList<StateWeibullCurveBucketInfo> timeBuckets,
        CompetingHazardCurveFitterOptions options)
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

    private static StateWeibullTimeFallbackCurve BuildDefaultTotalFallback(
        StateWeibullCurveBucketInfo timeBucket,
        CompetingHazardCurveFitterOptions options)
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

    private static StateWeibullCurve BuildDefaultTotalCurve(
        string league,
        string neutralBucket,
        StateWeibullCurveBucketInfo timeBucket,
        CompetingHazardCurveFitterOptions options)
        => new()
        {
            League = league,
            ScoreBucket = neutralBucket,
            TimeBucket = timeBucket.TimeBucket,
            BucketStartMinute = timeBucket.StartMinute,
            BucketEndMinute = timeBucket.EndMinute,
            BucketLengthMinutes = timeBucket.LengthMinutes,
            Status = "DefaultFallback",
            CurveSource = "default_zero_total_hazard",
            ExpectedGoalsSource = "default_zero_total_hazard",
            ShapeKSource = "default_k",
            ExpectedGoalsInBucket = 0.0,
            ShapeK = options.DefaultK,
            Warning = "Total hazard curve missing; default zero hazard used."
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

    private static NextGoalSideAggregate ResolveShareFallback(
        string directionalBucket,
        string neutralBucket,
        string pressureBucket,
        string timeBucket,
        SideCount exact,
        double ruleBasedProbability,
        IReadOnlyDictionary<string, NextGoalSideAggregate> directionalOverall,
        IReadOnlyDictionary<string, NextGoalSideAggregate> pressureTime,
        IReadOnlyDictionary<string, NextGoalSideAggregate> neutralScoreTime,
        IReadOnlyDictionary<string, NextGoalSideAggregate> timeShares,
        NextGoalSideAggregate leagueOverall,
        CompetingHazardCurveFitterOptions options,
        out string status,
        out string source,
        out string warning)
    {
        if (directionalOverall.TryGetValue(directionalBucket, out NextGoalSideAggregate? directional) && directional.GoalCount >= options.MinDirectionalOverallGoals)
        {
            status = "DirectionalOverallFallback";
            source = "fallback_directional_overall";
            warning = SparseShareWarning(exact, source);
            return directional;
        }

        string pressureTimeKey = Key(pressureBucket, timeBucket);
        if (pressureTime.TryGetValue(pressureTimeKey, out NextGoalSideAggregate? pressure) && pressure.GoalCount >= options.MinPressureTimeGoals)
        {
            status = "PressureTimeFallback";
            source = "fallback_pressure_time";
            warning = SparseShareWarning(exact, source);
            return pressure;
        }

        string neutralTimeKey = Key(neutralBucket, timeBucket);
        if (neutralScoreTime.TryGetValue(neutralTimeKey, out NextGoalSideAggregate? neutral) && neutral.GoalCount >= options.MinNeutralScoreTimeGoals)
        {
            status = "NeutralScoreTimeFallback";
            source = "fallback_neutral_score_time";
            warning = SparseShareWarning(exact, source);
            return neutral;
        }

        if (timeShares.TryGetValue(timeBucket, out NextGoalSideAggregate? time) && time.GoalCount >= options.MinTimeGoals)
        {
            status = "TimeBucketFallback";
            source = "fallback_time_bucket";
            warning = SparseShareWarning(exact, source);
            return time;
        }

        if (leagueOverall.GoalCount >= options.MinLeagueGoals)
        {
            status = "LeagueOverallFallback";
            source = "fallback_league_overall";
            warning = SparseShareWarning(exact, source);
            return leagueOverall;
        }

        status = "RuleBasedFallback";
        source = "fallback_rule_based";
        warning = SparseShareWarning(exact, source);
        return new NextGoalSideAggregate
        {
            Key = "rule_based",
            Source = "rule_based",
            ProbabilityHomeNextGoal = ruleBasedProbability
        };
    }

    private static string SparseShareWarning(SideCount exact, string source)
        => $"Scorer-share exact directional/time bucket too sparse ({exact.GoalCount} goals: home={exact.HomeGoalCount}, away={exact.AwayGoalCount}); {source} used.";

    private static Dictionary<string, NextGoalSideAggregate> BuildShareAggregateMap(
        IReadOnlyList<ExposureRow> goalRows,
        Func<ExposureRow, string> keySelector,
        string source,
        double priorProbability,
        double priorWeight)
    {
        var result = new Dictionary<string, NextGoalSideAggregate>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, ExposureRow> group in goalRows.GroupBy(keySelector, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(group.Key))
                continue;

            result[group.Key] = ToShareAggregate(group.Key, source, group.ToList(), priorProbability, priorWeight);
        }

        return result;
    }

    private static NextGoalSideAggregate ToShareAggregate(
        string key,
        string source,
        IReadOnlyList<ExposureRow> rows,
        double priorProbability,
        double priorWeight)
    {
        SideCount count = CountSideGoals(rows);
        return new NextGoalSideAggregate
        {
            Key = key,
            Source = source,
            DirectionalScoreBucket = rows.FirstOrDefault()?.DirectionalScoreBucket ?? string.Empty,
            NeutralScoreBucket = rows.FirstOrDefault()?.NeutralScoreBucket ?? string.Empty,
            PressureBucket = rows.FirstOrDefault()?.PressureBucket ?? string.Empty,
            TimeBucket = rows.FirstOrDefault()?.TimeBucket ?? string.Empty,
            HomeGoalCount = count.HomeGoalCount,
            AwayGoalCount = count.AwayGoalCount,
            ProbabilityHomeNextGoal = Smooth(count.HomeGoalCount, count.AwayGoalCount, priorProbability, priorWeight)
        };
    }

    private static SideCount CountSideGoals(IEnumerable<ExposureRow> rows)
    {
        int home = 0;
        int away = 0;
        foreach (ExposureRow row in rows)
        {
            if (!row.GoalHappened)
                continue;
            if (row.GoalSide.Equals("home", StringComparison.OrdinalIgnoreCase))
                home++;
            else if (row.GoalSide.Equals("away", StringComparison.OrdinalIgnoreCase))
                away++;
        }

        return new SideCount(home, away);
    }

    private static double Smooth(int homeGoals, int awayGoals, double priorProbability, double priorWeight)
    {
        double total = homeGoals + awayGoals;
        if (total <= 0 && priorWeight <= 0)
            return ClampProbability(priorProbability);

        double numerator = homeGoals + priorProbability * priorWeight;
        double denominator = total + priorWeight;
        return denominator > Epsilon ? ClampProbability(numerator / denominator) : ClampProbability(priorProbability);
    }

    private static double ClampProbability(double probability)
        => Math.Clamp(probability, 0.01, 0.99);

    private static string NeutralFromDirectional(string directionalBucket)
    {
        if (directionalBucket.Equals("draw_0_0", StringComparison.OrdinalIgnoreCase))
            return "draw_0_0";
        if (directionalBucket.StartsWith("draw", StringComparison.OrdinalIgnoreCase))
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
        if (directionalBucket.StartsWith("home", StringComparison.OrdinalIgnoreCase))
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
            "score_bucket", "home_goals_at_start", "away_goals_at_start", "start_minute", "end_minute",
            "exposure_minutes", "goal_happened", "goal_minute", "goal_side"
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
        builder.AppendLine("league,directional_score_bucket,neutral_score_bucket,pressure_bucket,time_bucket,bucket_start_minute,bucket_end_minute,total_status,total_curve_source,total_mu_source,total_k_source,total_full_bucket_exposures,total_goal_count,total_raw_mu,total_final_mu,total_raw_k,total_final_k,scorer_share_status,scorer_share_source,p_home_goal,p_away_goal,exact_home_goals,exact_away_goals,exact_goal_count,exact_raw_p_home,fallback_share_source,fallback_home_goals,fallback_away_goals,fallback_goal_count,fallback_p_home,rule_based_p_home,home_mu,away_mu,warning");

        foreach (CompetingHazardCurve curve in curves.OrderBy(x => x.BucketStartMinute).ThenBy(x => x.DirectionalScoreBucket))
        {
            builder.Append(Csv(curve.League)); builder.Append(',');
            builder.Append(Csv(curve.DirectionalScoreBucket)); builder.Append(',');
            builder.Append(Csv(curve.NeutralScoreBucket)); builder.Append(',');
            builder.Append(Csv(curve.PressureBucket)); builder.Append(',');
            builder.Append(Csv(curve.TimeBucket)); builder.Append(',');
            builder.Append(Format(curve.BucketStartMinute)); builder.Append(',');
            builder.Append(Format(curve.BucketEndMinute)); builder.Append(',');
            builder.Append(Csv(curve.TotalStatus)); builder.Append(',');
            builder.Append(Csv(curve.TotalCurveSource)); builder.Append(',');
            builder.Append(Csv(curve.TotalExpectedGoalsSource)); builder.Append(',');
            builder.Append(Csv(curve.TotalShapeKSource)); builder.Append(',');
            builder.Append(Format(curve.TotalFullBucketExposures)); builder.Append(',');
            builder.Append(curve.TotalGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(curve.TotalRawExpectedGoalsInBucket.HasValue ? Format(curve.TotalRawExpectedGoalsInBucket.Value) : string.Empty); builder.Append(',');
            builder.Append(Format(curve.TotalExpectedGoalsInBucket)); builder.Append(',');
            builder.Append(curve.TotalRawShapeK.HasValue ? Format(curve.TotalRawShapeK.Value) : string.Empty); builder.Append(',');
            builder.Append(Format(curve.TotalShapeK)); builder.Append(',');
            builder.Append(Csv(curve.ScorerShareStatus)); builder.Append(',');
            builder.Append(Csv(curve.ScorerShareSource)); builder.Append(',');
            builder.Append(Format(curve.ProbabilityHomeGoalInBucket)); builder.Append(',');
            builder.Append(Format(curve.ProbabilityAwayGoalInBucket)); builder.Append(',');
            builder.Append(curve.ExactHomeGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(curve.ExactAwayGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(curve.ExactGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(curve.ExactRawProbabilityHomeGoal.HasValue ? Format(curve.ExactRawProbabilityHomeGoal.Value) : string.Empty); builder.Append(',');
            builder.Append(Csv(curve.FallbackScorerShareSource)); builder.Append(',');
            builder.Append(curve.FallbackHomeGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(curve.FallbackAwayGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(curve.FallbackGoalCount.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(Format(curve.FallbackProbabilityHomeGoal)); builder.Append(',');
            builder.Append(Format(curve.RuleBasedProbabilityHomeGoal)); builder.Append(',');
            builder.Append(Format(curve.Home.ExpectedGoalsInBucket)); builder.Append(',');
            builder.Append(Format(curve.Away.ExpectedGoalsInBucket)); builder.Append(',');
            builder.Append(Csv(curve.Warning));
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(fullPath, builder.ToString(), Encoding.UTF8, cancellationToken);
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

    private sealed record SideCount(int HomeGoalCount, int AwayGoalCount)
    {
        public int GoalCount => HomeGoalCount + AwayGoalCount;
    }
}
