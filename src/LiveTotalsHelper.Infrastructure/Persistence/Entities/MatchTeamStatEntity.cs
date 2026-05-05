namespace LiveTotalsHelper.Infrastructure.Persistence.Entities;

public sealed class MatchTeamStatEntity
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public long SofaScoreEventId { get; set; }

    public string Period { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ValueType { get; set; } = string.Empty;
    public string StatisticsType { get; set; } = string.Empty;

    public string HomeRaw { get; set; } = string.Empty;
    public string AwayRaw { get; set; } = string.Empty;
    public double? HomeValue { get; set; }
    public double? AwayValue { get; set; }
    public double? HomeTotal { get; set; }
    public double? AwayTotal { get; set; }

    public string StatisticsJsonPath { get; set; } = string.Empty;

    public MatchEntity? Match { get; set; }
}
