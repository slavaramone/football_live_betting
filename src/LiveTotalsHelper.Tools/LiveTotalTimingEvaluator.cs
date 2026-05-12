using LiveTotalsHelper.Modeling;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalTimingEvaluation
{
    public string ScoreState { get; set; } = string.Empty;
    public string SelectedTimingGroup { get; set; } = string.Empty;
    public string TimingFallback { get; set; } = string.Empty;
    public double EmpiricalWeight { get; set; }
    public double WeibullRemainingShare { get; set; }
    public double EmpiricalRemainingShare { get; set; }
    public double TimingRemainingShare { get; set; }
}

public static class LiveTotalTimingEvaluator
{
    public static LiveTotalTimingEvaluation Evaluate(WeibullModelFile model, int minute, int homeGoals, int awayGoals, double empiricalWeight)
    {
        string scoreState = ScoreStateResolver.FromScore(homeGoals, awayGoals);
        TimingModelSource source = ResolveTimingModel(model, scoreState);
        TimingBlendResult timing = TimingShareCalculator.Calculate(new TimingBlendInput
        {
            Minute = Math.Clamp(minute, 0, model.MaxMinute > 0 ? model.MaxMinute : 90),
            ShapeK = source.ShapeK,
            ScaleLambda = source.ScaleLambda,
            CdfAtMaxMinute = source.CdfAtMaxMinute,
            EmpiricalBuckets = MapBuckets(source.EmpiricalBuckets),
            EmpiricalWeight = empiricalWeight
        });

        return new LiveTotalTimingEvaluation
        {
            ScoreState = scoreState,
            SelectedTimingGroup = source.GroupName,
            TimingFallback = source.FallbackReason,
            EmpiricalWeight = timing.EmpiricalWeight,
            WeibullRemainingShare = timing.WeibullRemainingShare,
            EmpiricalRemainingShare = timing.EmpiricalRemainingShare,
            TimingRemainingShare = timing.BlendedRemainingShare
        };
    }

    private static TimingModelSource ResolveTimingModel(WeibullModelFile model, string scoreState)
    {
        TimingModelGroupResult? group = model.Groups.FirstOrDefault(g => g.GroupName.Equals(scoreState, StringComparison.OrdinalIgnoreCase));
        if (group is not null)
        {
            return new TimingModelSource
            {
                GroupName = group.GroupName,
                ShapeK = group.ShapeK,
                ScaleLambda = group.ScaleLambda,
                CdfAtMaxMinute = group.CdfAtMaxMinute,
                EmpiricalBuckets = group.EmpiricalBuckets
            };
        }

        string fallback = model.Groups.Count > 0
            ? $"Timing group '{scoreState}' was not found; falling back to All/root model."
            : string.Empty;

        return new TimingModelSource
        {
            GroupName = "All",
            FallbackReason = fallback,
            ShapeK = model.Weibull.ShapeK,
            ScaleLambda = model.Weibull.ScaleLambda,
            CdfAtMaxMinute = model.Weibull.CdfAtMaxMinute,
            EmpiricalBuckets = model.Empirical.Buckets
        };
    }

    private static List<EmpiricalTimingBucketModel> MapBuckets(IEnumerable<EmpiricalTimingBucket> buckets)
    {
        return buckets.Select(x => new EmpiricalTimingBucketModel
        {
            FromMinuteExclusive = x.FromMinuteExclusive,
            ToMinuteInclusive = x.ToMinuteInclusive,
            Label = x.Label,
            GoalCount = x.GoalCount,
            GoalShare = x.GoalShare,
            CumulativeShareBefore = x.CumulativeShareBefore,
            CumulativeShareAfter = x.CumulativeShareAfter
        }).ToList();
    }
}

internal sealed class TimingModelSource
{
    public string GroupName { get; set; } = string.Empty;
    public string FallbackReason { get; set; } = string.Empty;
    public double ShapeK { get; set; }
    public double ScaleLambda { get; set; }
    public double CdfAtMaxMinute { get; set; }
    public List<EmpiricalTimingBucket> EmpiricalBuckets { get; set; } = [];
}
