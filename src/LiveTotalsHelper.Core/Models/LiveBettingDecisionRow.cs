namespace LiveTotalsHelper.Core.Models;

public sealed class LiveBettingDecisionRow
{
    public double Line { get; init; }
    public string Side { get; init; } = string.Empty;
    public double? BookOdds { get; init; }
    public double? ModelProbability { get; init; }
    public double? FairOdds { get; init; }
    public double? Edge { get; init; }
    public string Decision { get; init; } = "NO ODDS";
    public string Reason { get; init; } = string.Empty;

    public string SelectionKey => $"{Side} {Line:0.##}";
    public string LineText => Line.ToString("0.##");
    public string BookOddsText => BookOdds.HasValue ? BookOdds.Value.ToString("0.###") : "-";
    public string ModelProbabilityText => ModelProbability.HasValue ? ModelProbability.Value.ToString("P1") : "-";
    public string FairOddsText => FairOdds.HasValue ? FairOdds.Value.ToString("0.###") : "-";
    public string EdgeText => Edge.HasValue ? $"{Edge.Value:+0.0%;-0.0%;0.0%}" : "-";
}
