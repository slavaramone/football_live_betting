namespace LiveTotalsHelper.Modeling;

public readonly record struct OverSettlementProbabilities(double WinProbability, double PushProbability, double LossProbability);

public static class TotalGoalsPricingCalculator
{
    public static double RemoveTwoWayMargin(double overOdds, double underOdds)
    {
        if (overOdds <= 1.0) throw new ArgumentException("Over odds must be greater than 1.0.", nameof(overOdds));
        if (underOdds <= 1.0) throw new ArgumentException("Under odds must be greater than 1.0.", nameof(underOdds));
        double overRaw = 1.0 / overOdds;
        double underRaw = 1.0 / underOdds;
        return overRaw / (overRaw + underRaw);
    }

    public static double SolveTotalXg(double line, double fairOverProbability)
    {
        if (line <= 0) throw new ArgumentException("Line must be greater than 0.", nameof(line));
        if (fairOverProbability <= 0 || fairOverProbability >= 1) throw new ArgumentException("Fair probability must be between 0 and 1.", nameof(fairOverProbability));

        double low = 0.01;
        double high = 8.0;

        while (OverNoPushProbability(high, line) < fairOverProbability && high < 20.0)
            high *= 1.5;

        for (int i = 0; i < 100; i++)
        {
            double mid = (low + high) / 2.0;
            double p = OverNoPushProbability(mid, line);
            if (p < fairOverProbability)
                low = mid;
            else
                high = mid;
        }

        return (low + high) / 2.0;
    }

    public static double OverNoPushProbability(double lambda, double line)
    {
        OverSettlementProbabilities p = CalculateOverSettlementProbabilities(line, 0, lambda);
        double decisive = p.WinProbability + p.LossProbability;
        if (decisive <= 0)
            return 0.5;
        return p.WinProbability / decisive;
    }

    public static OverSettlementProbabilities CalculateOverSettlementProbabilities(double line, int currentGoals, double remainingLambda)
    {
        double frac = Math.Round(line - Math.Floor(line), 6);
        int floor = (int)Math.Floor(line);
        remainingLambda = Math.Max(0.0, remainingLambda);

        if (Math.Abs(frac - 0.5) < 1e-6)
        {
            int winFromTotal = floor + 1;
            int needed = winFromTotal - currentGoals;
            double win = ProbabilityAtLeast(needed, remainingLambda);
            return new OverSettlementProbabilities(win, 0.0, 1.0 - win);
        }

        if (Math.Abs(frac) < 1e-6)
        {
            int neededWin = floor + 1 - currentGoals;
            int neededPush = floor - currentGoals;
            double win = ProbabilityAtLeast(neededWin, remainingLambda);
            double push = ProbabilityExactly(neededPush, remainingLambda);
            double loss = Math.Max(0.0, 1.0 - win - push);
            return new OverSettlementProbabilities(win, push, loss);
        }

        if (Math.Abs(frac - 0.25) < 1e-6)
        {
            OverSettlementProbabilities lower = CalculateOverSettlementProbabilities(floor, currentGoals, remainingLambda);
            OverSettlementProbabilities upper = CalculateOverSettlementProbabilities(floor + 0.5, currentGoals, remainingLambda);
            return Average(lower, upper);
        }

        if (Math.Abs(frac - 0.75) < 1e-6)
        {
            OverSettlementProbabilities lower = CalculateOverSettlementProbabilities(floor + 0.5, currentGoals, remainingLambda);
            OverSettlementProbabilities upper = CalculateOverSettlementProbabilities(floor + 1.0, currentGoals, remainingLambda);
            return Average(lower, upper);
        }

        throw new ArgumentException($"Unsupported total line {line}. Supported: .0, .25, .5, .75 lines.");
    }

    public static OverSettlementProbabilities CalculateOverSettlementProbabilities(
        double line,
        int currentGoals,
        IReadOnlyDictionary<int, double> remainingGoalProbabilities,
        double? targetMean = null)
    {
        Dictionary<int, double> distribution = NormalizeDistribution(remainingGoalProbabilities);
        if (distribution.Count == 0)
            throw new ArgumentException("Remaining-goals distribution must contain at least one positive probability.", nameof(remainingGoalProbabilities));

        if (targetMean.HasValue)
            distribution = TiltDistributionToMean(distribution, Math.Max(0.0, targetMean.Value));

        double frac = Math.Round(line - Math.Floor(line), 6);
        int floor = (int)Math.Floor(line);

        if (Math.Abs(frac - 0.5) < 1e-6)
        {
            int needed = floor + 1 - currentGoals;
            double win = ProbabilityAtLeast(needed, distribution);
            return new OverSettlementProbabilities(win, 0.0, 1.0 - win);
        }

        if (Math.Abs(frac) < 1e-6)
        {
            int neededWin = floor + 1 - currentGoals;
            int neededPush = floor - currentGoals;
            double win = ProbabilityAtLeast(neededWin, distribution);
            double push = ProbabilityExactly(neededPush, distribution);
            double loss = Math.Max(0.0, 1.0 - win - push);
            return new OverSettlementProbabilities(win, push, loss);
        }

        if (Math.Abs(frac - 0.25) < 1e-6)
        {
            OverSettlementProbabilities lower = CalculateOverSettlementProbabilities(floor, currentGoals, distribution, targetMean: null);
            OverSettlementProbabilities upper = CalculateOverSettlementProbabilities(floor + 0.5, currentGoals, distribution, targetMean: null);
            return Average(lower, upper);
        }

        if (Math.Abs(frac - 0.75) < 1e-6)
        {
            OverSettlementProbabilities lower = CalculateOverSettlementProbabilities(floor + 0.5, currentGoals, distribution, targetMean: null);
            OverSettlementProbabilities upper = CalculateOverSettlementProbabilities(floor + 1.0, currentGoals, distribution, targetMean: null);
            return Average(lower, upper);
        }

        throw new ArgumentException($"Unsupported total line {line}. Supported: .0, .25, .5, .75 lines.");
    }

    public static double CalculateFairOdds(OverSettlementProbabilities p)
    {
        if (p.WinProbability <= 0)
            return double.PositiveInfinity;
        return 1.0 + p.LossProbability / p.WinProbability;
    }

    public static double ProbabilityAtLeast(int needed, double lambda)
    {
        if (needed <= 0)
            return 1.0;
        return Math.Clamp(1.0 - PoissonCdf(needed - 1, lambda), 0.0, 1.0);
    }

    public static double ProbabilityExactly(int needed, double lambda)
    {
        if (needed < 0)
            return 0.0;
        return PoissonPmf(needed, lambda);
    }

    public static double ProbabilityAtLeast(int needed, IReadOnlyDictionary<int, double> distribution)
    {
        if (needed <= 0)
            return 1.0;

        return Math.Clamp(distribution.Where(x => x.Key >= needed).Sum(x => x.Value), 0.0, 1.0);
    }

    public static double ProbabilityExactly(int needed, IReadOnlyDictionary<int, double> distribution)
    {
        if (needed < 0)
            return 0.0;

        return distribution.TryGetValue(needed, out double probability)
            ? Math.Clamp(probability, 0.0, 1.0)
            : 0.0;
    }

    public static double PoissonPmf(int k, double lambda)
    {
        if (k < 0)
            return 0.0;
        if (lambda < 0)
            throw new ArgumentOutOfRangeException(nameof(lambda));

        double result = Math.Exp(-lambda);
        for (int i = 1; i <= k; i++)
            result *= lambda / i;
        return result;
    }

    public static double PoissonCdf(int k, double lambda)
    {
        if (k < 0)
            return 0.0;
        double sum = 0.0;
        for (int i = 0; i <= k; i++)
            sum += PoissonPmf(i, lambda);
        return Math.Clamp(sum, 0.0, 1.0);
    }

    private static OverSettlementProbabilities Average(OverSettlementProbabilities a, OverSettlementProbabilities b)
    {
        return new OverSettlementProbabilities(
            (a.WinProbability + b.WinProbability) / 2.0,
            (a.PushProbability + b.PushProbability) / 2.0,
            (a.LossProbability + b.LossProbability) / 2.0);
    }

    private static Dictionary<int, double> NormalizeDistribution(IReadOnlyDictionary<int, double> probabilities)
    {
        var result = probabilities
            .Where(x => x.Key >= 0 && x.Value > 0 && !double.IsNaN(x.Value) && !double.IsInfinity(x.Value))
            .ToDictionary(x => x.Key, x => x.Value);

        double total = result.Values.Sum();
        if (total <= 0)
            return [];

        foreach (int key in result.Keys.ToList())
            result[key] /= total;

        return result;
    }

    private static Dictionary<int, double> TiltDistributionToMean(IReadOnlyDictionary<int, double> distribution, double targetMean)
    {
        int min = distribution.Keys.Min();
        int max = distribution.Keys.Max();
        targetMean = Math.Clamp(targetMean, min, max);

        double currentMean = distribution.Sum(x => x.Key * x.Value);
        if (Math.Abs(currentMean - targetMean) < 1e-9)
            return distribution.ToDictionary(x => x.Key, x => x.Value);

        double low = -30.0;
        double high = 30.0;

        for (int i = 0; i < 100; i++)
        {
            double mid = (low + high) / 2.0;
            double mean = TiltedMean(distribution, mid);
            if (mean < targetMean)
                low = mid;
            else
                high = mid;
        }

        double theta = (low + high) / 2.0;
        double maxLog = distribution.Max(x => Math.Log(x.Value) + theta * x.Key);
        var weights = distribution.ToDictionary(
            x => x.Key,
            x => Math.Exp(Math.Log(x.Value) + theta * x.Key - maxLog));
        double total = weights.Values.Sum();
        return weights.ToDictionary(x => x.Key, x => x.Value / total);
    }

    private static double TiltedMean(IReadOnlyDictionary<int, double> distribution, double theta)
    {
        double maxLog = distribution.Max(x => Math.Log(x.Value) + theta * x.Key);
        double weighted = 0.0;
        double total = 0.0;

        foreach ((int goals, double probability) in distribution)
        {
            double weight = Math.Exp(Math.Log(probability) + theta * goals - maxLog);
            weighted += goals * weight;
            total += weight;
        }

        return total <= 0 ? 0.0 : weighted / total;
    }
}
