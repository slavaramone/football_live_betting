namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class CompetingHazardCurveSet
{
    public string Version { get; init; } = "competing-hazard-curves-v1";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string SourceExposureFile { get; init; } = string.Empty;
    public string League { get; init; } = string.Empty;
    public List<string> DirectionalScoreBuckets { get; init; } = [];
    public List<StateWeibullCurveBucketInfo> TimeBuckets { get; init; } = [];
    public CompetingHazardCurveFitSettings Settings { get; init; } = new();
    public List<CompetingHazardFallbackCurve> TimeFallbacks { get; init; } = [];
    public List<CompetingHazardFallbackCurve> NeutralScoreTimeFallbacks { get; init; } = [];
    public List<CompetingHazardCurve> Curves { get; init; } = [];
}

public sealed class CompetingHazardCurveFitSettings
{
    public double MinMuFullBucketExposures { get; init; } = 75.0;
    public int MinMuGoals { get; init; } = 30;
    public double MinKFullBucketExposures { get; init; } = 150.0;
    public int MinKGoals { get; init; } = 50;
    public double MinK { get; init; } = 0.65;
    public double MaxK { get; init; } = 1.85;
    public double KStep { get; init; } = 0.05;
    public double DefaultK { get; init; } = 1.0;
    public string SparseFallbackPolicy { get; init; } = "neutral_score_time -> league_time_bucket -> default_k";
}

public sealed class CompetingHazardFallbackCurve
{
    public string Key { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string NeutralScoreBucket { get; init; } = string.Empty;
    public string TimeBucket { get; init; } = string.Empty;
    public double BucketStartMinute { get; init; }
    public double BucketEndMinute { get; init; }
    public double BucketLengthMinutes { get; init; }
    public CompetingHazardSideCurve Home { get; init; } = new() { Side = "home" };
    public CompetingHazardSideCurve Away { get; init; } = new() { Side = "away" };
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

    public CompetingHazardSideCurve Home { get; init; } = new() { Side = "home" };
    public CompetingHazardSideCurve Away { get; init; } = new() { Side = "away" };

    public double ExpectedGoalsInBucket => Home.ExpectedGoalsInBucket + Away.ExpectedGoalsInBucket;
    public double RawExpectedGoalsInBucket => (Home.RawExpectedGoalsInBucket ?? 0.0) + (Away.RawExpectedGoalsInBucket ?? 0.0);
    public double ProbabilityHomeGoalInBucket => ExpectedGoalsInBucket > 0.0
        ? Home.ExpectedGoalsInBucket / ExpectedGoalsInBucket
        : 0.5;
    public double ProbabilityAwayGoalInBucket => 1.0 - ProbabilityHomeGoalInBucket;
    public string Warning => string.Join(" | ", new[] { Home.Warning, Away.Warning }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

public sealed class CompetingHazardSideCurve
{
    public string Side { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string CurveSource { get; init; } = string.Empty;
    public string ExpectedGoalsSource { get; init; } = string.Empty;
    public string ShapeKSource { get; init; } = string.Empty;

    public double FullBucketExposures { get; init; }
    public double ExposureMinutes { get; init; }
    public int GoalCount { get; init; }
    public double? RawExpectedGoalsInBucket { get; init; }
    public double ExpectedGoalsInBucket { get; init; }
    public double? RawShapeK { get; init; }
    public double ShapeK { get; init; }

    public double FallbackFullBucketExposures { get; init; }
    public double FallbackExposureMinutes { get; init; }
    public int FallbackGoalCount { get; init; }
    public double FallbackExpectedGoalsInBucket { get; init; }
    public double FallbackShapeK { get; init; }
    public string FallbackSource { get; init; } = string.Empty;
    public string Warning { get; init; } = string.Empty;
}
