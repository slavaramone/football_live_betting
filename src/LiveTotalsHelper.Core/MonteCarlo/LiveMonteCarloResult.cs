namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class LiveMonteCarloResult
{
    public double ExpectedRemainingGoals { get; init; }

    public double P0 { get; init; }
    public double P1 { get; init; }
    public double P2 { get; init; }
    public double P3Plus { get; init; }

    public double POver { get; init; }
    public double PUnder { get; init; }

    public double FairOverOdds { get; init; }
    public double FairUnderOdds { get; init; }

    public int NeededGoalsForOver { get; init; }
    public double EffectiveEndMinute { get; init; }

    public string Explanation { get; init; } = string.Empty;
}
