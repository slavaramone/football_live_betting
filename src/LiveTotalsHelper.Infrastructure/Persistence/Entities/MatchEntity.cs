namespace LiveTotalsHelper.Infrastructure.Persistence.Entities;

public sealed class MatchEntity
{
    public int Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string FlashscoreId { get; set; } = string.Empty;

    public int TournamentId { get; set; }
    public string LeagueName { get; set; } = string.Empty;
    public string LeagueSlug { get; set; } = string.Empty;
    public string CountryName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;

    public int SeasonId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public string SeasonYear { get; set; } = string.Empty;
    public int RoundNumber { get; set; }

    public string HomeTeamId { get; set; } = string.Empty;
    public string HomeTeamName { get; set; } = string.Empty;
    public string HomeTeamSlug { get; set; } = string.Empty;
    public string HomeTeamShortName { get; set; } = string.Empty;

    public string AwayTeamId { get; set; } = string.Empty;
    public string AwayTeamName { get; set; } = string.Empty;
    public string AwayTeamSlug { get; set; } = string.Empty;
    public string AwayTeamShortName { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;
    public DateTimeOffset? StartTimeUtc { get; set; }
    public string StatusType { get; set; } = string.Empty;
    public string StatusDescription { get; set; } = string.Empty;

    public int? HomeScoreCurrent { get; set; }
    public int? AwayScoreCurrent { get; set; }
    public int? HomeScorePeriod1 { get; set; }
    public int? AwayScorePeriod1 { get; set; }
    public int? HomeScorePeriod2 { get; set; }
    public int? AwayScorePeriod2 { get; set; }

    public string CalendarJsonPath { get; set; } = string.Empty;
    public string EventMetaJsonPath { get; set; } = string.Empty;
    public DateTimeOffset CalendarUpdatedAtUtc { get; set; }

    public List<MatchEventEntity> Events { get; set; } = [];
    public List<MatchStatEntity> Stats { get; set; } = [];
    public List<FlashscoreOddsEntity> FlashscoreOdds { get; set; } = [];
}
