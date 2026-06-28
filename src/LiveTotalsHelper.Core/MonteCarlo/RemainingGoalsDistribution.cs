namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class RemainingGoalsDistribution
{
    public double P0 { get; init; }
    public double P1 { get; init; }
    public double P2 { get; init; }
    public double P3Plus { get; init; }

    public double Sum => P0 + P1 + P2 + P3Plus;
}
