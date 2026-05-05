namespace LiveTotalsHelper.Core.Models;

public sealed class ModelSummary
{
    public double PreMatchTotalXg { get; init; }
    public double LeagueRemainingShare { get; init; }
    public double OpponentRemainingShare { get; init; }
    public double MixedRemainingShare { get; init; }
    public double RemainingXg { get; init; }

    public string PreMatchTotalXgText => PreMatchTotalXg.ToString("0.00");
    public string LeagueRemainingText => LeagueRemainingShare.ToString("P0");
    public string OpponentRemainingText => OpponentRemainingShare.ToString("P0");
    public string MixedRemainingText => MixedRemainingShare.ToString("P0");
    public string RemainingXgText => RemainingXg.ToString("0.00");
}
