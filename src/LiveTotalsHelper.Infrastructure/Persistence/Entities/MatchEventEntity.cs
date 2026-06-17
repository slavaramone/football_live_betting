namespace LiveTotalsHelper.Infrastructure.Persistence.Entities;

public sealed class MatchEventEntity
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string IncidentId { get; set; } = string.Empty;

    public string IncidentType { get; set; } = string.Empty;
    public string IncidentClass { get; set; } = string.Empty;
    public int Minute { get; set; }
    public int? AddedTime { get; set; }
    public int? TimeSeconds { get; set; }
    public bool IsHome { get; set; }

    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    public string PlayerName { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string AssistPlayerName { get; set; } = string.Empty;
    public string AssistPlayerId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    public MatchEntity? Match { get; set; }
}
