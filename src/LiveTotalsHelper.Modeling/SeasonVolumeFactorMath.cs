namespace LiveTotalsHelper.Modeling;

public sealed class SeasonVolumeFactorMathInput
{
    public int BaseGoals { get; set; }
    public int BaseMatches { get; set; }
    public int CurrentGoals { get; set; }
    public int CurrentMatches { get; set; }
    public int PriorStrengthMatches { get; set; } = 100;
}

public sealed class SeasonVolumeFactorMathResult
{
    public double Factor { get; set; } = 1.0;
    public double RawFactor { get; set; } = 1.0;
    public double Weight { get; set; }
    public double BaseGoalsPerMatch { get; set; }
    public double CurrentGoalsPerMatch { get; set; }
    public string Warning { get; set; } = string.Empty;
}

public static class SeasonVolumeFactorMath
{
    public static SeasonVolumeFactorMathResult Calculate(SeasonVolumeFactorMathInput input)
    {
        if (input.PriorStrengthMatches < 0)
            throw new ArgumentException("Prior strength must be zero or greater.", nameof(input));

        var result = new SeasonVolumeFactorMathResult();

        if (input.BaseMatches <= 0)
        {
            result.Warning = "Could not apply current-season volume factor: no finished base-season matches found.";
            return result;
        }

        result.BaseGoalsPerMatch = input.BaseGoals / (double)input.BaseMatches;

        if (input.CurrentMatches <= 0)
        {
            result.Warning = "Current-season volume factor is 1.0 because no prior current-season matches were found before the requested round.";
            result.CurrentGoalsPerMatch = 0.0;
            result.RawFactor = 1.0;
            result.Factor = 1.0;
            return result;
        }

        result.CurrentGoalsPerMatch = input.CurrentGoals / (double)input.CurrentMatches;
        result.RawFactor = result.BaseGoalsPerMatch > 0.0 ? result.CurrentGoalsPerMatch / result.BaseGoalsPerMatch : 1.0;
        result.Weight = input.PriorStrengthMatches == 0
            ? 1.0
            : input.CurrentMatches / (input.CurrentMatches + (double)input.PriorStrengthMatches);
        result.Factor = 1.0 + ((result.RawFactor - 1.0) * result.Weight);
        return result;
    }
}
