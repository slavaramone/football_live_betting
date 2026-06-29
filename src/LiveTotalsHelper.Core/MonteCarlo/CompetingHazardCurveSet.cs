namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class CompetingHazardCurveSet
{
    public string Version { get; init; } = "competing-hazard-curves-v2";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Strategy { get; init; } = "total_state_weibull_x_directional_scorer_share";
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
    public List<CompetingHazardCurve> Curves { get; init; } = [];
}

public sealed class CompetingHazardFitSettings
{
    public StateWeibullCurveFitSettings TotalHazardFit { get; init; } = new();
    public NextGoalSideModelSettings ScorerShareFit { get; init; } = new();
    public string Strategy { get; init; } = "Fit total goal hazard by neutral score/time bucket, then split it by directional next-goal scorer share.";
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
