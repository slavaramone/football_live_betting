namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class NextGoalSideModelSet
{
    public string Version { get; init; } = "next-goal-side-model-v1";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string SourceExposureFile { get; init; } = string.Empty;
    public string League { get; init; } = string.Empty;
    public List<string> DirectionalScoreBuckets { get; init; } = [];
    public List<StateWeibullCurveBucketInfo> TimeBuckets { get; init; } = [];
    public NextGoalSideModelSettings Settings { get; init; } = new();
    public NextGoalSideAggregate LeagueOverall { get; init; } = new();
    public List<NextGoalSideAggregate> DirectionalOverall { get; init; } = [];
    public List<NextGoalSideAggregate> PressureTime { get; init; } = [];
    public List<NextGoalSideAggregate> NeutralScoreTime { get; init; } = [];
    public List<NextGoalSideAggregate> TimeFallbacks { get; init; } = [];
    public List<NextGoalSideEstimate> Estimates { get; init; } = [];
}

public sealed class NextGoalSideModelSettings
{
    public int MinExactGoals { get; init; } = 25;
    public int MinDirectionalOverallGoals { get; init; } = 50;
    public int MinPressureTimeGoals { get; init; } = 40;
    public int MinNeutralScoreTimeGoals { get; init; } = 25;
    public int MinTimeGoals { get; init; } = 50;
    public int MinLeagueGoals { get; init; } = 100;
    public double PriorWeightGoals { get; init; } = 6.0;
    public string FallbackPolicy { get; init; } = "directional_time -> directional_overall -> pressure_time -> neutral_score_time -> time_bucket -> league_overall -> rule_based";
}

public sealed class NextGoalSideAggregate
{
    public string Key { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string DirectionalScoreBucket { get; init; } = string.Empty;
    public string NeutralScoreBucket { get; init; } = string.Empty;
    public string PressureBucket { get; init; } = string.Empty;
    public string TimeBucket { get; init; } = string.Empty;
    public int HomeGoalCount { get; init; }
    public int AwayGoalCount { get; init; }
    public int GoalCount => HomeGoalCount + AwayGoalCount;
    public double ProbabilityHomeNextGoal { get; init; } = 0.5;
}

public sealed class NextGoalSideEstimate
{
    public string League { get; init; } = string.Empty;
    public string DirectionalScoreBucket { get; init; } = string.Empty;
    public string NeutralScoreBucket { get; init; } = string.Empty;
    public string PressureBucket { get; init; } = string.Empty;
    public string TimeBucket { get; init; } = string.Empty;
    public double BucketStartMinute { get; init; }
    public double BucketEndMinute { get; init; }

    public string Status { get; init; } = string.Empty;
    public string ProbabilitySource { get; init; } = string.Empty;
    public double ProbabilityHomeNextGoal { get; init; } = 0.5;
    public double ProbabilityAwayNextGoal => 1.0 - ProbabilityHomeNextGoal;

    public int ExactHomeGoalCount { get; init; }
    public int ExactAwayGoalCount { get; init; }
    public int ExactGoalCount => ExactHomeGoalCount + ExactAwayGoalCount;
    public double? ExactRawProbabilityHomeNextGoal { get; init; }

    public string FallbackSource { get; init; } = string.Empty;
    public int FallbackHomeGoalCount { get; init; }
    public int FallbackAwayGoalCount { get; init; }
    public int FallbackGoalCount => FallbackHomeGoalCount + FallbackAwayGoalCount;
    public double FallbackProbabilityHomeNextGoal { get; init; } = 0.5;
    public double RuleBasedProbabilityHomeNextGoal { get; init; } = 0.5;
    public string Warning { get; init; } = string.Empty;
}
