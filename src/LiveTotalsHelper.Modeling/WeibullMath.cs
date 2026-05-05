namespace LiveTotalsHelper.Modeling;

public static class WeibullMath
{
    public static double Cdf(double minute, double shapeK, double scaleLambda)
    {
        if (minute <= 0) return 0.0;
        if (scaleLambda <= 0) throw new ArgumentOutOfRangeException(nameof(scaleLambda));
        if (shapeK <= 0) throw new ArgumentOutOfRangeException(nameof(shapeK));

        return 1.0 - Math.Exp(-Math.Pow(minute / scaleLambda, shapeK));
    }

    public static double RemainingShare(double minute, double shapeK, double scaleLambda)
        => Math.Clamp(1.0 - Cdf(minute, shapeK, scaleLambda), 0.0, 1.0);
}
