namespace LiveTotalsHelper.Modeling;

public static class PoissonMath
{
    public static double Pmf(int k, double lambda)
    {
        if (k < 0) return 0.0;
        if (lambda < 0) throw new ArgumentOutOfRangeException(nameof(lambda));

        double result = Math.Exp(-lambda);
        for (int i = 1; i <= k; i++)
            result *= lambda / i;

        return result;
    }

    public static double Cdf(int k, double lambda)
    {
        if (k < 0) return 0.0;

        double sum = 0.0;
        for (int i = 0; i <= k; i++)
            sum += Pmf(i, lambda);

        return Math.Clamp(sum, 0.0, 1.0);
    }
}
