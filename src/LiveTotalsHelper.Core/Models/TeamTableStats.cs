namespace LiveTotalsHelper.Core.Models;

public sealed class TeamTableStats
{
    public string Team { get; init; } = string.Empty;
    public int Position { get; init; }
    public int Played { get; init; }
    public int GoalsFor { get; init; }
    public int GoalsAgainst { get; init; }

    public double GoalsForPerMatch => Played <= 0 ? 0 : (double)GoalsFor / Played;
    public double GoalsAgainstPerMatch => Played <= 0 ? 0 : (double)GoalsAgainst / Played;
}
