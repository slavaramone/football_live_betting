namespace LiveTotalsHelper.Modeling;

public sealed class EmpiricalTimingBucketModel
{
    public int FromMinuteExclusive { get; set; }
    public int ToMinuteInclusive { get; set; }
    public string Label { get; set; } = string.Empty;
    public int GoalCount { get; set; }
    public double GoalShare { get; set; }
    public double CumulativeShareBefore { get; set; }
    public double CumulativeShareAfter { get; set; }
}

public sealed class TimingBlendInput
{
    public double Minute { get; set; }
    public double ShapeK { get; set; }
    public double ScaleLambda { get; set; }
    public double CdfAtMaxMinute { get; set; }
    public IReadOnlyList<EmpiricalTimingBucketModel> EmpiricalBuckets { get; set; } = Array.Empty<EmpiricalTimingBucketModel>();
    public double EmpiricalWeight { get; set; } = 0.8;
}

public sealed class TimingBlendResult
{
    public double WeibullElapsedShare { get; set; }
    public double WeibullRemainingShare { get; set; }
    public double EmpiricalElapsedShare { get; set; }
    public double EmpiricalRemainingShare { get; set; }
    public double EmpiricalWeight { get; set; }
    public double WeibullWeight => 1.0 - EmpiricalWeight;
    public double BlendedElapsedShare { get; set; }
    public double BlendedRemainingShare { get; set; }
}

public static class TimingShareCalculator
{
    public static TimingBlendResult Calculate(TimingBlendInput input)
    {
        double empiricalWeight = Math.Clamp(input.EmpiricalWeight, 0.0, 1.0);
        double weibullElapsed = NormalizedWeibullCdf(input.Minute, input.ShapeK, input.ScaleLambda, input.CdfAtMaxMinute);
        double empiricalElapsed = EmpiricalCdf(input.Minute, input.EmpiricalBuckets);
        double blendedElapsed = empiricalWeight * empiricalElapsed + (1.0 - empiricalWeight) * weibullElapsed;

        return new TimingBlendResult
        {
            WeibullElapsedShare = weibullElapsed,
            WeibullRemainingShare = Math.Clamp(1.0 - weibullElapsed, 0.0, 1.0),
            EmpiricalElapsedShare = empiricalElapsed,
            EmpiricalRemainingShare = Math.Clamp(1.0 - empiricalElapsed, 0.0, 1.0),
            EmpiricalWeight = empiricalWeight,
            BlendedElapsedShare = Math.Clamp(blendedElapsed, 0.0, 1.0),
            BlendedRemainingShare = Math.Clamp(1.0 - blendedElapsed, 0.0, 1.0)
        };
    }

    public static double WeibullCdf(double minute, double shapeK, double scaleLambda)
    {
        if (minute <= 0 || shapeK <= 0 || scaleLambda <= 0)
            return 0.0;
        return Math.Clamp(1.0 - Math.Exp(-Math.Pow(minute / scaleLambda, shapeK)), 0.0, 1.0);
    }

    public static double NormalizedWeibullCdf(double minute, double shapeK, double scaleLambda, double cdfAtMaxMinute)
    {
        if (minute <= 0 || shapeK <= 0 || scaleLambda <= 0 || cdfAtMaxMinute <= 0)
            return 0.0;
        double raw = WeibullCdf(minute, shapeK, scaleLambda);
        return Math.Clamp(raw / cdfAtMaxMinute, 0.0, 1.0);
    }

    public static double NormalizedWeibullSurvival(double minute, double shapeK, double scaleLambda, double cdfAtMaxMinute)
    {
        return Math.Clamp(1.0 - NormalizedWeibullCdf(minute, shapeK, scaleLambda, cdfAtMaxMinute), 0.0, 1.0);
    }

    public static double EmpiricalCdf(double minute, IReadOnlyList<EmpiricalTimingBucketModel> buckets)
    {
        if (minute <= 0 || buckets.Count == 0)
            return 0.0;

        foreach (EmpiricalTimingBucketModel bucket in buckets)
        {
            if (minute <= bucket.FromMinuteExclusive)
                return Math.Clamp(bucket.CumulativeShareBefore, 0.0, 1.0);

            if (minute <= bucket.ToMinuteInclusive)
            {
                double width = bucket.ToMinuteInclusive - bucket.FromMinuteExclusive;
                if (width <= 0)
                    return Math.Clamp(bucket.CumulativeShareAfter, 0.0, 1.0);

                double progress = (minute - bucket.FromMinuteExclusive) / width;
                return Math.Clamp(bucket.CumulativeShareBefore + bucket.GoalShare * progress, 0.0, 1.0);
            }
        }

        return 1.0;
    }
}
