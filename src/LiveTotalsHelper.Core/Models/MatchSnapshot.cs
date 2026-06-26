namespace LiveTotalsHelper.Core.Models;

public sealed class MatchSnapshot
{
    public string MatchId { get; init; } = Guid.NewGuid().ToString("N");
    public string League { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public int Minute { get; set; }
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    public int HomeRedCards { get; set; }
    public int AwayRedCards { get; set; }
    public bool IsFixture { get; init; }
    public string BestSignal { get; set; } = "No bet";
    public double BestEdgePercent { get; set; }

    public string MatchName => $"{HomeTeam} vs {AwayTeam}";
    public string Score => IsFixture ? "vs" : $"{HomeGoals}-{AwayGoals}";
    public string MinuteText => IsFixture ? string.Empty : $"{Minute}'";
    public string EdgeText => BestEdgePercent > 0 ? $"+{BestEdgePercent:0.0}%" : "No bet";
}
