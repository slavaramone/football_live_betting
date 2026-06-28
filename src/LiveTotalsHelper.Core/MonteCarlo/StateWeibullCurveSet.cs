namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class StateWeibullCurveSet
{
    public string Version { get; init; } = "state-weibull-curves-v1";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string SourceExposureFile { get; init; } = string.Empty;
    public string League { get; init; } = string.Empty;
    public List<string> ScoreBuckets { get; init; } = [];
    public List<StateWeibullCurveBucketInfo> TimeBuckets { get; init; } = [];
    public StateWeibullCurveFitSettings Settings { get; init; } = new();
    public List<StateWeibullTimeFallbackCurve> TimeFallbacks { get; init; } = [];
    public List<StateWeibullCurve> Curves { get; init; } = [];
}

public sealed class StateWeibullCurveFitSettings
{
    public double MinMuFullBucketExposures { get; init; } = 75.0;
    public int MinMuGoals { get; init; } = 30;
    public double MinKFullBucketExposures { get; init; } = 150.0;
    public int MinKGoals { get; init; } = 50;
    public double MinK { get; init; } = 0.65;
    public double MaxK { get; init; } = 1.85;
    public double KStep { get; init; } = 0.05;
    public double DefaultK { get; init; } = 1.0;
    public string SparseFallbackPolicy { get; init; } = "league_time_bucket";
}

public sealed class StateWeibullCurveBucketInfo
{
    public string TimeBucket { get; init; } = string.Empty;
    public double StartMinute { get; init; }
    public double EndMinute { get; init; }
    public double LengthMinutes { get; init; }
}

public sealed class StateWeibullTimeFallbackCurve
{
    public string TimeBucket { get; init; } = string.Empty;
    public double BucketStartMinute { get; init; }
    public double BucketEndMinute { get; init; }
    public double BucketLengthMinutes { get; init; }
    public double FullBucketExposures { get; init; }
    public double ExposureMinutes { get; init; }
    public int GoalCount { get; init; }
    public double ExpectedGoalsInBucket { get; init; }
    public double ShapeK { get; init; }
    public string ShapeKSource { get; init; } = string.Empty;
}

public sealed class StateWeibullCurve
{
    public string League { get; init; } = string.Empty;
    public string ScoreBucket { get; init; } = string.Empty;
    public string TimeBucket { get; init; } = string.Empty;
    public double BucketStartMinute { get; init; }
    public double BucketEndMinute { get; init; }
    public double BucketLengthMinutes { get; init; }

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
    public string Warning { get; init; } = string.Empty;
}
