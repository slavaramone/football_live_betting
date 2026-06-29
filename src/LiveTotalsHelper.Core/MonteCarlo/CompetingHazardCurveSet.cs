namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class CompetingHazardCurveSet
{
    public string Version { get; init; } = "competing-hazard-curves-v3-after-goal-goal-draw-market-baseline";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Strategy { get; init; } = "total_state_weibull_x_directional_scorer_share_x_after_goal_hazard_factors_x_goal_draw_suppression_x_market_baseline";
    public string SourceExposureFile { get; init; } = string.Empty;
    public string League { get; init; } = string.Empty;
    public List<string> ScoreBuckets { get; init; } = [];
    public List<string> DirectionalScoreBuckets { get; init; } = [];
    public List<StateWeibullCurveBucketInfo> TimeBuckets { get; init; } = [];
    public CompetingHazardFitSettings Settings { get; init; } = new();
    public List<StateWeibullTimeFallbackCurve> TotalTimeFallbacks { get; init; } = [];
    public List<StateWeibullCurve> TotalCurves { get; init; } = [];
    public NextGoalSideAggregate LeagueScorerShare { get; init; } = new();
    public List<NextGoalSideAggregate> DirectionalScorerShares { get; init; } = [];
    public List<NextGoalSideAggregate> PressureTimeScorerShares { get; init; } = [];
    public List<NextGoalSideAggregate> NeutralScoreTimeScorerShares { get; init; } = [];
    public List<NextGoalSideAggregate> TimeScorerShares { get; init; } = [];
    public CompetingHazardAfterGoalSettings AfterGoalSettings { get; init; } = new();
    public List<CompetingHazardAfterGoalFactor> AfterGoalFactors { get; init; } = [];
    public CompetingHazardGoalDrawSuppressionSettings GoalDrawSuppressionSettings { get; init; } = new();
    public List<CompetingHazardGoalDrawSuppressionFactor> GoalDrawSuppressionFactors { get; init; } = [];
    public CompetingHazardMarketBaselineSettings MarketBaselineSettings { get; init; } = new();
    public List<CompetingHazardCurve> Curves { get; init; } = [];
}

public sealed class CompetingHazardFitSettings
{
    public StateWeibullCurveFitSettings TotalHazardFit { get; init; } = new();
    public NextGoalSideModelSettings ScorerShareFit { get; init; } = new();
    public string Strategy { get; init; } = "Fit total goal hazard by neutral score/time bucket, split it by directional next-goal scorer share, then apply fitted after-goal, goal-draw suppression, and optional pregame market baseline factors during simulation.";
}

public sealed class CompetingHazardAfterGoalSettings
{
    public bool Enabled { get; init; } = true;
    public double PriorExpectedGoals { get; init; } = 40.0;
    public double MinMultiplier { get; init; } = 0.55;
    public double MaxMultiplier { get; init; } = 1.65;
    public double MinExpectedGoalsForStableFactor { get; init; } = 8.0;
    public string Strategy { get; init; } = "Fit multiplicative residual factors against base competing hazards by minutes since previous goal; side-aware same-team and opponent-response factors are used when last-goal side is known.";
    public List<CompetingHazardAfterGoalBucket> Buckets { get; init; } = [];
}

public sealed class CompetingHazardAfterGoalBucket
{
    public string Key { get; init; } = string.Empty;
    public double StartMinutesSinceGoal { get; init; }
    public double EndMinutesSinceGoal { get; init; }
}

public sealed class CompetingHazardAfterGoalFactor
{
    public string Key { get; init; } = string.Empty;
    public double StartMinutesSinceGoal { get; init; }
    public double EndMinutesSinceGoal { get; init; }
    public string Status { get; init; } = string.Empty;
    public int ExposureRows { get; init; }
    public double ExposureMinutes { get; init; }

    public int TotalObservedGoals { get; init; }
    public double TotalExpectedGoals { get; init; }
    public double TotalRawMultiplier { get; init; } = 1.0;
    public double TotalMultiplier { get; init; } = 1.0;

    public int SameTeamObservedGoals { get; init; }
    public double SameTeamExpectedGoals { get; init; }
    public double SameTeamRawMultiplier { get; init; } = 1.0;
    public double SameTeamMultiplier { get; init; } = 1.0;

    public int OpponentObservedGoals { get; init; }
    public double OpponentExpectedGoals { get; init; }
    public double OpponentRawMultiplier { get; init; } = 1.0;
    public double OpponentMultiplier { get; init; } = 1.0;

    public string Warning { get; init; } = string.Empty;
}

public sealed class CompetingHazardGoalDrawSuppressionSettings
{
    public bool Enabled { get; init; } = true;
    public string NeutralScoreBucket { get; init; } = "draw_1_1_plus";
    public double PriorExpectedGoals { get; init; } = 35.0;
    public double MinMultiplier { get; init; } = 0.55;
    public double MaxMultiplier { get; init; } = 1.0;
    public double MinExpectedGoalsForStableFactor { get; init; } = 8.0;
    public string Strategy { get; init; } = "Fit time-bucket residual multipliers for goal-draw states such as 1-1 and 2-2 after applying base competing hazards and after-goal factors; multiply both home and away hazards during simulation.";
}

public sealed class CompetingHazardGoalDrawSuppressionFactor
{
    public string Key { get; init; } = string.Empty;
    public string NeutralScoreBucket { get; init; } = "draw_1_1_plus";
    public string TimeBucket { get; init; } = string.Empty;
    public double BucketStartMinute { get; init; }
    public double BucketEndMinute { get; init; }
    public string Status { get; init; } = string.Empty;
    public int ExposureRows { get; init; }
    public double ExposureMinutes { get; init; }
    public int ObservedGoals { get; init; }
    public double ExpectedGoals { get; init; }
    public double RawMultiplier { get; init; } = 1.0;
    public double Multiplier { get; init; } = 1.0;
    public string Warning { get; init; } = string.Empty;
}


public sealed class CompetingHazardMarketBaselineSettings
{
    public bool Enabled { get; init; } = true;
    public double OddsSensitivityGoals { get; init; } = 1.25;
    public double MultiplierShrink { get; init; } = 0.65;
    public double? LowTotalMultiplierShrink { get; init; }
    public double? HighTotalMultiplierShrink { get; init; }
    public double MinMultiplier { get; init; } = 0.75;
    public double MaxMultiplier { get; init; } = 1.25;
    public double MinMarketExpectedTotalGoals { get; init; } = 1.0;
    public double MaxMarketExpectedTotalGoals { get; init; } = 6.0;
    public double ModelBaselineExpectedTotalGoals { get; init; }
    public string Strategy { get; init; } = "Use pregame total line and over/under odds to infer a match-specific expected total, compare it with the fitted league baseline, and apply asymmetric shrunk/clamped multiplicative factors to both competing hazards.";
}

public sealed class LiveMarketBaselineAdjustment
{
    public bool Enabled { get; init; }
    public bool Applied { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public double? PregameTotalLine { get; init; }
    public double? PregameOverOdds { get; init; }
    public double? PregameUnderOdds { get; init; }
    public double? NoVigPOver { get; init; }
    public double? MarketExpectedTotalGoals { get; init; }
    public double ModelBaselineExpectedTotalGoals { get; init; }
    public double RawMultiplier { get; init; } = 1.0;
    public double Multiplier { get; init; } = 1.0;
    public string Warning { get; init; } = string.Empty;

    public static LiveMarketBaselineAdjustment Disabled => new()
    {
        Enabled = false,
        Applied = false,
        Status = "Disabled",
        Source = "disabled",
        Multiplier = 1.0,
        RawMultiplier = 1.0
    };

    public static LiveMarketBaselineAdjustment Neutral(string status, string source = "none", string warning = "") => new()
    {
        Enabled = true,
        Applied = false,
        Status = status,
        Source = source,
        Multiplier = 1.0,
        RawMultiplier = 1.0,
        Warning = warning
    };
}


public sealed class CompetingHazardCurve
{
    public string League { get; init; } = string.Empty;
    public string DirectionalScoreBucket { get; init; } = string.Empty;
    public string NeutralScoreBucket { get; init; } = string.Empty;
    public string PressureBucket { get; init; } = string.Empty;
    public string TimeBucket { get; init; } = string.Empty;
    public double BucketStartMinute { get; init; }
    public double BucketEndMinute { get; init; }
    public double BucketLengthMinutes { get; init; }

    public string TotalStatus { get; init; } = string.Empty;
    public string TotalCurveSource { get; init; } = string.Empty;
    public string TotalExpectedGoalsSource { get; init; } = string.Empty;
    public string TotalShapeKSource { get; init; } = string.Empty;
    public double TotalFullBucketExposures { get; init; }
    public double TotalExposureMinutes { get; init; }
    public int TotalGoalCount { get; init; }
    public double? TotalRawExpectedGoalsInBucket { get; init; }
    public double TotalExpectedGoalsInBucket { get; init; }
    public double? TotalRawShapeK { get; init; }
    public double TotalShapeK { get; init; }

    public string ScorerShareStatus { get; init; } = string.Empty;
    public string ScorerShareSource { get; init; } = string.Empty;
    public double ProbabilityHomeGoalInBucket { get; init; } = 0.5;
    public double ProbabilityAwayGoalInBucket => 1.0 - ProbabilityHomeGoalInBucket;
    public int ExactHomeGoalCount { get; init; }
    public int ExactAwayGoalCount { get; init; }
    public int ExactGoalCount => ExactHomeGoalCount + ExactAwayGoalCount;
    public double? ExactRawProbabilityHomeGoal { get; init; }
    public string FallbackScorerShareSource { get; init; } = string.Empty;
    public int FallbackHomeGoalCount { get; init; }
    public int FallbackAwayGoalCount { get; init; }
    public int FallbackGoalCount => FallbackHomeGoalCount + FallbackAwayGoalCount;
    public double FallbackProbabilityHomeGoal { get; init; } = 0.5;
    public double RuleBasedProbabilityHomeGoal { get; init; } = 0.5;

    public CompetingHazardSideSplit Home { get; init; } = new() { Side = "home" };
    public CompetingHazardSideSplit Away { get; init; } = new() { Side = "away" };

    public string TotalWarning { get; init; } = string.Empty;
    public string ScorerShareWarning { get; init; } = string.Empty;
    public string Warning => string.Join(" | ", new[] { TotalWarning, ScorerShareWarning }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class CompetingHazardSideSplit
{
    public string Side { get; init; } = string.Empty;
    public double ProbabilityGoalInBucket { get; init; }
    public double ExpectedGoalsInBucket { get; init; }
    public double ShapeK { get; init; }
    public string ExpectedGoalsSource { get; init; } = string.Empty;
    public string ShapeKSource { get; init; } = string.Empty;
}
