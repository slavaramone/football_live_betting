namespace LiveTotalsHelper.Infrastructure.Persistence.Entities;

public sealed class FlashscoreOddsEntity
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public string EventId { get; set; } = string.Empty;

    public string Market { get; set; } = string.Empty;
    public string Bookmaker { get; set; } = string.Empty;
    public string Selection { get; set; } = string.Empty;
    public double? Line { get; set; }
    public double Odds { get; set; }

    public string SourceUrl { get; set; } = string.Empty;
    public string OddsJsonPath { get; set; } = string.Empty;
    public DateTime? DownloadedAtUtc { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }

    public MatchEntity? Match { get; set; }
}
