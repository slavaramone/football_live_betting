namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class CompetingHazardCurveSet
{
    public string Version { get; init; } = "competing-hazard-curves-v3-after-goal";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Strategy { get; init; } = "total_state_weibull_x_directional_scorer_share_x_after_goal_hazard_factors";
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
    public List<CompetingHazardCurve> Curves { get; init; } = [];
}

public sealed class CompetingHazardFitSettings
{
    public StateWeibullCurveFitSettings TotalHazardFit { get; init; } = new();
    public NextGoalSideModelSettings ScorerShareFit { get; init; } = new();
    public string Strategy { get; init; } = "Fit total goal hazard by neutral score/time bucket, split it by directional next-goal scorer share, then apply fitted after-goal hazard factors during simulation.";
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
