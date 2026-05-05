namespace LiveTotalsHelper.Core.Models;

public sealed class BetDecision
{
    public double Line { get; init; }
    public double BookOverOdds { get; init; }
    public double ModelOverProbability { get; init; }
    public double FairOverOdds { get; init; }
    public double EdgePercent { get; init; }
    public string Decision { get; init; } = "NO";

    public string LineText => $"Over {Line:0.0}";
    public string BookOverOddsText => BookOverOdds.ToString("0.00");
    public string ModelOverProbabilityText => ModelOverProbability.ToString("P1");
    public string FairOverOddsText => FairOverOdds.ToString("0.00");
    public string EdgeText => EdgePercent >= 0 ? $"+{EdgePercent:0.0}%" : $"{EdgePercent:0.0}%";
}
