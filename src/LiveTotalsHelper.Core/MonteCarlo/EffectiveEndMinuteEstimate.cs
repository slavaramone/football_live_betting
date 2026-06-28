namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class EffectiveEndMinuteEstimate
{
    public double CurrentMinute { get; init; }
    public double EffectiveEndMinute { get; init; }
    public double RemainingEffectiveMinutes { get; init; }
    public string Period { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
