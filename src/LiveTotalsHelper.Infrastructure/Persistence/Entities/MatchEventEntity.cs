namespace LiveTotalsHelper.Infrastructure.Persistence.Entities;

public sealed class MatchEventEntity
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public long SofaScoreEventId { get; set; }
    public long? SofaScoreIncidentId { get; set; }

    public string IncidentType { get; set; } = string.Empty;
    public string IncidentClass { get; set; } = string.Empty;
    public int Minute { get; set; }
    public int? AddedTime { get; set; }
    public int? TimeSeconds { get; set; }
    public bool IsHome { get; set; }

    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    public string PlayerName { get; set; } = string.Empty;
    public long? SofaScorePlayerId { get; set; }
    public string AssistPlayerName { get; set; } = string.Empty;
    public long? SofaScoreAssistPlayerId { get; set; }
    public string Reason { get; set; } = string.Empty;

    public MatchEntity? Match { get; set; }
}
