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

    public bool AfterGoalFactorsEnabled { get; init; } = true;
    public double AfterGoalPriorExpectedGoals { get; init; } = 40.0;
    public double AfterGoalMinMultiplier { get; init; } = 0.55;
    public double AfterGoalMaxMultiplier { get; init; } = 1.65;
    public double AfterGoalMinExpectedGoalsForStableFactor { get; init; } = 8.0;

    public bool GoalDrawSuppressionEnabled { get; init; } = true;
    public string GoalDrawNeutralScoreBucket { get; init; } = "draw_1_1_plus";
    public double GoalDrawPriorExpectedGoals { get; init; } = 35.0;
    public double GoalDrawMinMultiplier { get; init; } = 0.55;
    public double GoalDrawMaxMultiplier { get; init; } = 1.0;
    public double GoalDrawMinExpectedGoalsForStableFactor { get; init; } = 8.0;

    public bool MarketBaselineEnabled { get; init; } = true;
    public double MarketBaselineOddsSensitivityGoals { get; init; } = 1.25;
    public double MarketBaselineMultiplierShrink { get; init; } = 0.65;
    public double? MarketBaselineLowTotalMultiplierShrink { get; init; }
    public double? MarketBaselineHighTotalMultiplierShrink { get; init; }
    public double MarketBaselineMinMultiplier { get; init; } = 0.75;
    public double MarketBaselineMaxMultiplier { get; init; } = 1.25;
    public double MarketBaselineMinMarketExpectedTotalGoals { get; init; } = 1.0;
    public double MarketBaselineMaxMarketExpectedTotalGoals { get; init; } = 6.0;
    public double MarketBaselineModelBaselineExpectedTotalGoals { get; init; }
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
    public int AfterGoalFactorsWritten { get; init; }
    public int GoalDrawSuppressionFactorsWritten { get; init; }
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

        CompetingHazardAfterGoalSettings afterGoalSettings = CreateAfterGoalSettings(options);
        List<CompetingHazardAfterGoalFactor> afterGoalFactors = options.AfterGoalFactorsEnabled
            ? BuildAfterGoalFactors(rows, curves, afterGoalSettings)
            : [];

        CompetingHazardGoalDrawSuppressionSettings goalDrawSettings = CreateGoalDrawSuppressionSettings(options);
        List<CompetingHazardGoalDrawSuppressionFactor> goalDrawSuppressionFactors = options.GoalDrawSuppressionEnabled
            ? BuildGoalDrawSuppressionFactors(rows, curves, afterGoalSettings, afterGoalFactors, goalDrawSettings)
            : [];

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
            AfterGoalSettings = afterGoalSettings,
            AfterGoalFactors = afterGoalFactors,
            GoalDrawSuppressionSettings = goalDrawSettings,
            GoalDrawSuppressionFactors = goalDrawSuppressionFactors,
            MarketBaselineSettings = new CompetingHazardMarketBaselineSettings
            {
                Enabled = options.MarketBaselineEnabled,
                OddsSensitivityGoals = options.MarketBaselineOddsSensitivityGoals,
                MultiplierShrink = options.MarketBaselineMultiplierShrink,
                LowTotalMultiplierShrink = options.MarketBaselineLowTotalMultiplierShrink,
                HighTotalMultiplierShrink = options.MarketBaselineHighTotalMultiplierShrink,
                MinMultiplier = options.MarketBaselineMinMultiplier,
                MaxMultiplier = options.MarketBaselineMaxMultiplier,
                MinMarketExpectedTotalGoals = options.MarketBaselineMinMarketExpectedTotalGoals,
                MaxMarketExpectedTotalGoals = options.MarketBaselineMaxMarketExpectedTotalGoals,
                ModelBaselineExpectedTotalGoals = options.MarketBaselineModelBaselineExpectedTotalGoals
            },
            Curves = curves
                .OrderBy(x => x.BucketStartMinute)
                .ThenBy(x => x.DirectionalScoreBucket)
                .ToList()
        };

        await WriteJsonAsync(model, options.OutputPath, cancellationToken);
        await WriteSummaryCsvAsync(model, options.SummaryPath, cancellationToken);

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
            AfterGoalFactorsWritten = model.AfterGoalFactors.Count,
            GoalDrawSuppressionFactorsWritten = model.GoalDrawSuppressionFactors.Count,
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


    private static CompetingHazardAfterGoalSettings CreateAfterGoalSettings(CompetingHazardCurveFitterOptions options)
    {
        double minMultiplier = Math.Max(0.05, options.AfterGoalMinMultiplier);
        double maxMultiplier = Math.Max(minMultiplier, options.AfterGoalMaxMultiplier);
        return new CompetingHazardAfterGoalSettings
        {
            Enabled = options.AfterGoalFactorsEnabled,
            PriorExpectedGoals = Math.Max(0.0, options.AfterGoalPriorExpectedGoals),
            MinMultiplier = minMultiplier,
            MaxMultiplier = maxMultiplier,
            MinExpectedGoalsForStableFactor = Math.Max(0.0, options.AfterGoalMinExpectedGoalsForStableFactor),
            Buckets =
            [
                new CompetingHazardAfterGoalBucket { Key = "after_goal_0_3", StartMinutesSinceGoal = 0.0, EndMinutesSinceGoal = 3.0 },
                new CompetingHazardAfterGoalBucket { Key = "after_goal_3_7", StartMinutesSinceGoal = 3.0, EndMinutesSinceGoal = 7.0 },
                new CompetingHazardAfterGoalBucket { Key = "after_goal_7_12", StartMinutesSinceGoal = 7.0, EndMinutesSinceGoal = 12.0 }
            ]
        };
    }

    private static List<CompetingHazardAfterGoalFactor> BuildAfterGoalFactors(
        IReadOnlyList<ExposureRow> rows,
        IReadOnlyList<CompetingHazardCurve> curves,
        CompetingHazardAfterGoalSettings settings)
    {
        var accumulators = settings.Buckets.ToDictionary(
            x => x.Key,
            x => new AfterGoalFactorAccumulator(x),
            StringComparer.OrdinalIgnoreCase);

        var curveLookup = curves.ToDictionary(
            x => Key(x.DirectionalScoreBucket, x.TimeBucket),
            StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<int, ExposureRow> matchRows in rows
                     .GroupBy(x => x.MatchId)
                     .OrderBy(g => g.Key))
        {
            double? lastGoalMinute = null;
            string lastGoalSide = string.Empty;

            foreach (ExposureRow row in matchRows
                         .OrderBy(x => x.Sequence)
                         .ThenBy(x => x.StartMinute))
            {
                if (lastGoalMinute.HasValue
                    && curveLookup.TryGetValue(Key(row.DirectionalScoreBucket, row.TimeBucket), out CompetingHazardCurve? curve))
                {
                    string normalizedLastGoalSide = NormalizeGoalSide(lastGoalSide);
                    string normalizedGoalSide = NormalizeGoalSide(row.GoalSide);
                    double goalMinute = row.GoalMinute ?? row.EndMinute;

                    foreach (CompetingHazardAfterGoalBucket bucket in settings.Buckets)
                    {
                        double segmentStart = Math.Max(row.StartMinute, lastGoalMinute.Value + bucket.StartMinutesSinceGoal);
                        double segmentEnd = Math.Min(row.EndMinute, lastGoalMinute.Value + bucket.EndMinutesSinceGoal);
                        if (segmentEnd <= segmentStart + Epsilon)
                            continue;

                        if (!accumulators.TryGetValue(bucket.Key, out AfterGoalFactorAccumulator? accumulator))
                            continue;

                        double homeExpected = ExpectedGoalsBetween(curve, curve.Home, segmentStart, segmentEnd);
                        double awayExpected = ExpectedGoalsBetween(curve, curve.Away, segmentStart, segmentEnd);
                        double totalExpected = homeExpected + awayExpected;
                        bool goalInSegment = row.GoalHappened
                                             && goalMinute >= segmentStart - Epsilon
                                             && goalMinute <= segmentEnd + Epsilon;

                        accumulator.ExposureRows++;
                        accumulator.ExposureMinutes += segmentEnd - segmentStart;
                        accumulator.TotalExpectedGoals += totalExpected;
                        if (goalInSegment)
                            accumulator.TotalObservedGoals++;

                        if (normalizedLastGoalSide.Equals("home", StringComparison.OrdinalIgnoreCase))
                        {
                            accumulator.SameTeamExpectedGoals += homeExpected;
                            accumulator.OpponentExpectedGoals += awayExpected;
                            if (goalInSegment && normalizedGoalSide.Equals("home", StringComparison.OrdinalIgnoreCase))
                                accumulator.SameTeamObservedGoals++;
                            else if (goalInSegment && normalizedGoalSide.Equals("away", StringComparison.OrdinalIgnoreCase))
                                accumulator.OpponentObservedGoals++;
                        }
                        else if (normalizedLastGoalSide.Equals("away", StringComparison.OrdinalIgnoreCase))
                        {
                            accumulator.SameTeamExpectedGoals += awayExpected;
                            accumulator.OpponentExpectedGoals += homeExpected;
                            if (goalInSegment && normalizedGoalSide.Equals("away", StringComparison.OrdinalIgnoreCase))
                                accumulator.SameTeamObservedGoals++;
                            else if (goalInSegment && normalizedGoalSide.Equals("home", StringComparison.OrdinalIgnoreCase))
                                accumulator.OpponentObservedGoals++;
                        }
                    }
                }

                if (row.GoalHappened)
                {
                    lastGoalMinute = row.GoalMinute ?? row.EndMinute;
                    lastGoalSide = NormalizeGoalSide(row.GoalSide);
                }
            }
        }

        return accumulators.Values
            .OrderBy(x => x.Bucket.StartMinutesSinceGoal)
            .Select(x => x.ToFactor(settings))
            .ToList();
    }

    private static double ExpectedGoalsBetween(
        CompetingHazardCurve curve,
        CompetingHazardSideSplit side,
        double startMinute,
        double endMinute)
    {
        double start = Math.Clamp(startMinute - curve.BucketStartMinute, 0.0, curve.BucketLengthMinutes);
        double end = Math.Clamp(endMinute - curve.BucketStartMinute, 0.0, curve.BucketLengthMinutes);
        if (end <= start + Epsilon)
            return 0.0;

        double startCum = side.ExpectedGoalsInBucket * Math.Pow(start / curve.BucketLengthMinutes, side.ShapeK);
        double endCum = side.ExpectedGoalsInBucket * Math.Pow(end / curve.BucketLengthMinutes, side.ShapeK);
        return Math.Max(0.0, endCum - startCum);
    }

    private static CompetingHazardGoalDrawSuppressionSettings CreateGoalDrawSuppressionSettings(CompetingHazardCurveFitterOptions options)
    {
        double minMultiplier = Math.Max(0.05, options.GoalDrawMinMultiplier);
        double maxMultiplier = Math.Max(minMultiplier, options.GoalDrawMaxMultiplier);
        return new CompetingHazardGoalDrawSuppressionSettings
        {
            Enabled = options.GoalDrawSuppressionEnabled,
            NeutralScoreBucket = string.IsNullOrWhiteSpace(options.GoalDrawNeutralScoreBucket) ? "draw_1_1_plus" : options.GoalDrawNeutralScoreBucket.Trim(),
            PriorExpectedGoals = Math.Max(0.0, options.GoalDrawPriorExpectedGoals),
            MinMultiplier = minMultiplier,
            MaxMultiplier = maxMultiplier,
            MinExpectedGoalsForStableFactor = Math.Max(0.0, options.GoalDrawMinExpectedGoalsForStableFactor)
        };
    }

    private static List<CompetingHazardGoalDrawSuppressionFactor> BuildGoalDrawSuppressionFactors(
        IReadOnlyList<ExposureRow> rows,
        IReadOnlyList<CompetingHazardCurve> curves,
        CompetingHazardAfterGoalSettings afterGoalSettings,
        IReadOnlyList<CompetingHazardAfterGoalFactor> afterGoalFactors,
        CompetingHazardGoalDrawSuppressionSettings settings)
    {
        if (!settings.Enabled)
            return [];

        var curveLookup = curves.ToDictionary(
            x => Key(x.DirectionalScoreBucket, x.TimeBucket),
            StringComparer.OrdinalIgnoreCase);

        var afterGoalLookup = afterGoalFactors.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        var accumulators = new Dictionary<string, GoalDrawSuppressionAccumulator>(StringComparer.OrdinalIgnoreCase)
        {
            ["goal_draw_overall"] = new GoalDrawSuppressionAccumulator("goal_draw_overall", settings.NeutralScoreBucket, "overall", 0.0, 0.0)
        };

        foreach (CompetingHazardCurve curve in curves
                     .Where(x => x.NeutralScoreBucket.Equals(settings.NeutralScoreBucket, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x.BucketStartMinute))
        {
            string key = GoalDrawTimeKey(curve.TimeBucket);
            if (!accumulators.ContainsKey(key))
            {
                accumulators[key] = new GoalDrawSuppressionAccumulator(
                    key,
                    settings.NeutralScoreBucket,
                    curve.TimeBucket,
                    curve.BucketStartMinute,
                    curve.BucketEndMinute);
            }
        }

        foreach (IGrouping<int, ExposureRow> matchRows in rows
                     .GroupBy(x => x.MatchId)
                     .OrderBy(g => g.Key))
        {
            double? lastGoalMinute = null;
            string lastGoalSide = string.Empty;

            foreach (ExposureRow row in matchRows
                         .OrderBy(x => x.Sequence)
                         .ThenBy(x => x.StartMinute))
            {
                if (row.NeutralScoreBucket.Equals(settings.NeutralScoreBucket, StringComparison.OrdinalIgnoreCase)
                    && curveLookup.TryGetValue(Key(row.DirectionalScoreBucket, row.TimeBucket), out CompetingHazardCurve? curve))
                {
                    AccumulateGoalDrawRow(
                        row,
                        curve,
                        accumulators,
                        afterGoalSettings,
                        afterGoalLookup,
                        lastGoalMinute,
                        lastGoalSide);
                }

                if (row.GoalHappened)
                {
                    lastGoalMinute = row.GoalMinute ?? row.EndMinute;
                    lastGoalSide = NormalizeGoalSide(row.GoalSide);
                }
            }
        }

        return accumulators.Values
            .OrderBy(x => x.TimeBucket.Equals("overall", StringComparison.OrdinalIgnoreCase) ? -1.0 : x.BucketStartMinute)
            .ThenBy(x => x.TimeBucket)
            .Select(x => x.ToFactor(settings))
            .ToList();
    }

    private static void AccumulateGoalDrawRow(
        ExposureRow row,
        CompetingHazardCurve curve,
        IDictionary<string, GoalDrawSuppressionAccumulator> accumulators,
        CompetingHazardAfterGoalSettings afterGoalSettings,
        IReadOnlyDictionary<string, CompetingHazardAfterGoalFactor> afterGoalFactors,
        double? lastGoalMinute,
        string lastGoalSide)
    {
        string timeKey = GoalDrawTimeKey(row.TimeBucket);
        if (!accumulators.TryGetValue(timeKey, out GoalDrawSuppressionAccumulator? timeAccumulator))
            return;

        GoalDrawSuppressionAccumulator overallAccumulator = accumulators["goal_draw_overall"];
        double goalMinute = row.GoalMinute ?? row.EndMinute;

        foreach (var segment in SplitForAfterGoalBoundaries(row.StartMinute, row.EndMinute, lastGoalMinute, afterGoalSettings))
        {
            if (segment.SegmentEnd <= segment.SegmentStart + Epsilon)
                continue;

            double homeExpected = ExpectedGoalsBetween(curve, curve.Home, segment.SegmentStart, segment.SegmentEnd);
            double awayExpected = ExpectedGoalsBetween(curve, curve.Away, segment.SegmentStart, segment.SegmentEnd);
            (double HomeMultiplier, double AwayMultiplier) afterGoal = ResolveAfterGoalMultipliersForFit(
                afterGoalSettings,
                afterGoalFactors,
                lastGoalMinute,
                lastGoalSide,
                segment.SegmentStart);

            double expected = homeExpected * afterGoal.HomeMultiplier + awayExpected * afterGoal.AwayMultiplier;
            bool goalInSegment = row.GoalHappened
                                 && goalMinute > segment.SegmentStart + Epsilon
                                 && goalMinute <= segment.SegmentEnd + Epsilon;

            timeAccumulator.Add(segment.SegmentEnd - segment.SegmentStart, expected, goalInSegment);
            overallAccumulator.Add(segment.SegmentEnd - segment.SegmentStart, expected, goalInSegment);
        }
    }

    private static IEnumerable<(double SegmentStart, double SegmentEnd)> SplitForAfterGoalBoundaries(
        double startMinute,
        double endMinute,
        double? lastGoalMinute,
        CompetingHazardAfterGoalSettings afterGoalSettings)
    {
        var points = new SortedSet<double> { startMinute, endMinute };
        if (lastGoalMinute.HasValue && afterGoalSettings.Enabled)
        {
            foreach (CompetingHazardAfterGoalBucket bucket in afterGoalSettings.Buckets)
            {
                AddCutPoint(points, startMinute, endMinute, lastGoalMinute.Value + bucket.StartMinutesSinceGoal);
                AddCutPoint(points, startMinute, endMinute, lastGoalMinute.Value + bucket.EndMinutesSinceGoal);
            }
        }

        double? previous = null;
        foreach (double point in points)
        {
            if (previous.HasValue && point > previous.Value + Epsilon)
                yield return (previous.Value, point);
            previous = point;
        }
    }

    private static void AddCutPoint(SortedSet<double> points, double startMinute, double endMinute, double point)
    {
        if (point > startMinute + Epsilon && point < endMinute - Epsilon)
            points.Add(point);
    }

    private static (double HomeMultiplier, double AwayMultiplier) ResolveAfterGoalMultipliersForFit(
        CompetingHazardAfterGoalSettings settings,
        IReadOnlyDictionary<string, CompetingHazardAfterGoalFactor> factors,
        double? lastGoalMinute,
        string lastGoalSide,
        double currentMinute)
    {
        if (!settings.Enabled || !lastGoalMinute.HasValue || factors.Count == 0)
            return (1.0, 1.0);

        double minutesSinceGoal = Math.Max(0.0, currentMinute - lastGoalMinute.Value);
        CompetingHazardAfterGoalFactor? factor = factors.Values
            .Where(x => minutesSinceGoal >= x.StartMinutesSinceGoal - Epsilon && minutesSinceGoal < x.EndMinutesSinceGoal - Epsilon)
            .OrderBy(x => x.StartMinutesSinceGoal)
            .FirstOrDefault();

        if (factor is null)
            return (1.0, 1.0);

        string normalizedLastGoalSide = NormalizeGoalSide(lastGoalSide);
        if (normalizedLastGoalSide.Equals("home", StringComparison.OrdinalIgnoreCase))
            return (ClampMultiplier(factor.SameTeamMultiplier), ClampMultiplier(factor.OpponentMultiplier));
        if (normalizedLastGoalSide.Equals("away", StringComparison.OrdinalIgnoreCase))
            return (ClampMultiplier(factor.OpponentMultiplier), ClampMultiplier(factor.SameTeamMultiplier));

        double multiplier = ClampMultiplier(factor.TotalMultiplier);
        return (multiplier, multiplier);
    }

    private static string GoalDrawTimeKey(string timeBucket)
        => $"goal_draw_{timeBucket}";

    private static double ClampMultiplier(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return 1.0;
        return Math.Clamp(value, 0.05, 5.0);
    }

    private sealed class GoalDrawSuppressionAccumulator
    {
        public GoalDrawSuppressionAccumulator(string key, string neutralScoreBucket, string timeBucket, double bucketStartMinute, double bucketEndMinute)
        {
            Key = key;
            NeutralScoreBucket = neutralScoreBucket;
            TimeBucket = timeBucket;
            BucketStartMinute = bucketStartMinute;
            BucketEndMinute = bucketEndMinute;
        }

        public string Key { get; }
        public string NeutralScoreBucket { get; }
        public string TimeBucket { get; }
        public double BucketStartMinute { get; }
        public double BucketEndMinute { get; }
        public int ExposureRows { get; private set; }
        public double ExposureMinutes { get; private set; }
        public int ObservedGoals { get; private set; }
        public double ExpectedGoals { get; private set; }

        public void Add(double exposureMinutes, double expectedGoals, bool observedGoal)
        {
            ExposureRows++;
            ExposureMinutes += Math.Max(0.0, exposureMinutes);
            ExpectedGoals += Math.Max(0.0, expectedGoals);
            if (observedGoal)
                ObservedGoals++;
        }

        public CompetingHazardGoalDrawSuppressionFactor ToFactor(CompetingHazardGoalDrawSuppressionSettings settings)
        {
            double raw = ExpectedGoals > Epsilon ? ObservedGoals / ExpectedGoals : 1.0;
            double multiplier = ShrinkAndClamp(raw, ExpectedGoals, settings);
            string status = ExpectedGoals >= settings.MinExpectedGoalsForStableFactor ? "Supported" : "SparseShrunk";
            string warning = string.Empty;
            if (status == "SparseShrunk")
                warning = $"Goal-draw suppression factor has low expected-goal sample ({ExpectedGoals.ToString("0.##", CultureInfo.InvariantCulture)} xG); multiplier is strongly shrunk toward 1.0.";

            return new CompetingHazardGoalDrawSuppressionFactor
            {
                Key = Key,
                NeutralScoreBucket = NeutralScoreBucket,
                TimeBucket = TimeBucket,
                BucketStartMinute = BucketStartMinute,
                BucketEndMinute = BucketEndMinute,
                Status = status,
                ExposureRows = ExposureRows,
                ExposureMinutes = ExposureMinutes,
                ObservedGoals = ObservedGoals,
                ExpectedGoals = ExpectedGoals,
                RawMultiplier = raw,
                Multiplier = multiplier,
                Warning = warning
            };
        }

        private static double ShrinkAndClamp(double raw, double expected, CompetingHazardGoalDrawSuppressionSettings settings)
        {
            double weight = expected / (expected + Math.Max(0.0, settings.PriorExpectedGoals));
            double shrunk = 1.0 + (raw - 1.0) * weight;
            if (double.IsNaN(shrunk) || double.IsInfinity(shrunk) || shrunk <= 0)
                shrunk = 1.0;
            return Math.Clamp(shrunk, settings.MinMultiplier, settings.MaxMultiplier);
        }
    }

    private static string NormalizeGoalSide(string value)
    {
        if (value.Equals("h", StringComparison.OrdinalIgnoreCase) || value.Equals("home", StringComparison.OrdinalIgnoreCase))
            return "home";
        if (value.Equals("a", StringComparison.OrdinalIgnoreCase) || value.Equals("away", StringComparison.OrdinalIgnoreCase))
            return "away";
        return string.Empty;
    }

    private sealed class AfterGoalFactorAccumulator
    {
        public AfterGoalFactorAccumulator(CompetingHazardAfterGoalBucket bucket)
        {
            Bucket = bucket;
        }

        public CompetingHazardAfterGoalBucket Bucket { get; }
        public int ExposureRows { get; set; }
        public double ExposureMinutes { get; set; }
        public int TotalObservedGoals { get; set; }
        public double TotalExpectedGoals { get; set; }
        public int SameTeamObservedGoals { get; set; }
        public double SameTeamExpectedGoals { get; set; }
        public int OpponentObservedGoals { get; set; }
        public double OpponentExpectedGoals { get; set; }

        public CompetingHazardAfterGoalFactor ToFactor(CompetingHazardAfterGoalSettings settings)
        {
            double totalRaw = RawMultiplier(TotalObservedGoals, TotalExpectedGoals);
            double sameRaw = RawMultiplier(SameTeamObservedGoals, SameTeamExpectedGoals);
            double opponentRaw = RawMultiplier(OpponentObservedGoals, OpponentExpectedGoals);
            string status = TotalExpectedGoals >= settings.MinExpectedGoalsForStableFactor
                ? "Supported"
                : "SparseShrunk";

            string warning = string.Empty;
            if (status == "SparseShrunk")
                warning = $"After-goal factor has low expected-goal sample ({TotalExpectedGoals.ToString("0.##", CultureInfo.InvariantCulture)} xG); multiplier is strongly shrunk toward 1.0.";

            return new CompetingHazardAfterGoalFactor
            {
                Key = Bucket.Key,
                StartMinutesSinceGoal = Bucket.StartMinutesSinceGoal,
                EndMinutesSinceGoal = Bucket.EndMinutesSinceGoal,
                Status = status,
                ExposureRows = ExposureRows,
                ExposureMinutes = ExposureMinutes,
                TotalObservedGoals = TotalObservedGoals,
                TotalExpectedGoals = TotalExpectedGoals,
                TotalRawMultiplier = totalRaw,
                TotalMultiplier = ShrinkAndClamp(totalRaw, TotalExpectedGoals, settings),
                SameTeamObservedGoals = SameTeamObservedGoals,
                SameTeamExpectedGoals = SameTeamExpectedGoals,
                SameTeamRawMultiplier = sameRaw,
                SameTeamMultiplier = ShrinkAndClamp(sameRaw, SameTeamExpectedGoals, settings),
                OpponentObservedGoals = OpponentObservedGoals,
                OpponentExpectedGoals = OpponentExpectedGoals,
                OpponentRawMultiplier = opponentRaw,
                OpponentMultiplier = ShrinkAndClamp(opponentRaw, OpponentExpectedGoals, settings),
                Warning = warning
            };
        }

        private static double RawMultiplier(int observed, double expected)
            => expected > Epsilon ? observed / expected : 1.0;

        private static double ShrinkAndClamp(double raw, double expected, CompetingHazardAfterGoalSettings settings)
        {
            double weight = expected / (expected + Math.Max(0.0, settings.PriorExpectedGoals));
            double shrunk = 1.0 + (raw - 1.0) * weight;
            if (double.IsNaN(shrunk) || double.IsInfinity(shrunk) || shrunk <= 0)
                shrunk = 1.0;
            return Math.Clamp(shrunk, settings.MinMultiplier, settings.MaxMultiplier);
        }
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
            "match_id", "sequence", "league", "league_slug", "time_bucket", "bucket_start_minute", "bucket_end_minute",
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
                    MatchId = GetInt(values, indexes, "match_id"),
                    Sequence = GetInt(values, indexes, "sequence"),
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

    private static async Task WriteSummaryCsvAsync(CompetingHazardCurveSet model, string outputPath, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        IReadOnlyList<CompetingHazardCurve> curves = model.Curves;
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

        if (model.AfterGoalFactors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("after_goal_factor_key,start_minutes_since_goal,end_minutes_since_goal,status,exposure_rows,exposure_minutes,total_observed_goals,total_expected_goals,total_raw_multiplier,total_multiplier,same_team_observed_goals,same_team_expected_goals,same_team_raw_multiplier,same_team_multiplier,opponent_observed_goals,opponent_expected_goals,opponent_raw_multiplier,opponent_multiplier,warning");
            foreach (CompetingHazardAfterGoalFactor factor in model.AfterGoalFactors.OrderBy(x => x.StartMinutesSinceGoal))
            {
                builder.Append(Csv(factor.Key)); builder.Append(',');
                builder.Append(Format(factor.StartMinutesSinceGoal)); builder.Append(',');
                builder.Append(Format(factor.EndMinutesSinceGoal)); builder.Append(',');
                builder.Append(Csv(factor.Status)); builder.Append(',');
                builder.Append(factor.ExposureRows.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
                builder.Append(Format(factor.ExposureMinutes)); builder.Append(',');
                builder.Append(factor.TotalObservedGoals.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
                builder.Append(Format(factor.TotalExpectedGoals)); builder.Append(',');
                builder.Append(Format(factor.TotalRawMultiplier)); builder.Append(',');
                builder.Append(Format(factor.TotalMultiplier)); builder.Append(',');
                builder.Append(factor.SameTeamObservedGoals.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
                builder.Append(Format(factor.SameTeamExpectedGoals)); builder.Append(',');
                builder.Append(Format(factor.SameTeamRawMultiplier)); builder.Append(',');
                builder.Append(Format(factor.SameTeamMultiplier)); builder.Append(',');
                builder.Append(factor.OpponentObservedGoals.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
                builder.Append(Format(factor.OpponentExpectedGoals)); builder.Append(',');
                builder.Append(Format(factor.OpponentRawMultiplier)); builder.Append(',');
                builder.Append(Format(factor.OpponentMultiplier)); builder.Append(',');
                builder.Append(Csv(factor.Warning));
                builder.AppendLine();
            }
        }

        if (model.GoalDrawSuppressionFactors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("goal_draw_factor_key,neutral_score_bucket,time_bucket,bucket_start_minute,bucket_end_minute,status,exposure_rows,exposure_minutes,observed_goals,expected_goals,raw_multiplier,multiplier,warning");
            foreach (CompetingHazardGoalDrawSuppressionFactor factor in model.GoalDrawSuppressionFactors.OrderBy(x => x.TimeBucket.Equals("overall", StringComparison.OrdinalIgnoreCase) ? -1.0 : x.BucketStartMinute).ThenBy(x => x.TimeBucket))
            {
                builder.Append(Csv(factor.Key)); builder.Append(',');
                builder.Append(Csv(factor.NeutralScoreBucket)); builder.Append(',');
                builder.Append(Csv(factor.TimeBucket)); builder.Append(',');
                builder.Append(Format(factor.BucketStartMinute)); builder.Append(',');
                builder.Append(Format(factor.BucketEndMinute)); builder.Append(',');
                builder.Append(Csv(factor.Status)); builder.Append(',');
                builder.Append(factor.ExposureRows.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
                builder.Append(Format(factor.ExposureMinutes)); builder.Append(',');
                builder.Append(factor.ObservedGoals.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
                builder.Append(Format(factor.ExpectedGoals)); builder.Append(',');
                builder.Append(Format(factor.RawMultiplier)); builder.Append(',');
                builder.Append(Format(factor.Multiplier)); builder.Append(',');
                builder.Append(Csv(factor.Warning));
                builder.AppendLine();
            }
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
        public int MatchId { get; init; }
        public int Sequence { get; init; }
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
