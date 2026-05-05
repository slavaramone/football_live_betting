using System.Globalization;
using System.Text.Json;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Infrastructure.Persistence.SofaScore;

public sealed class SofaScoreDbImporter
{
    private readonly LiveTotalsDbContext _db;

    public SofaScoreDbImporter(LiveTotalsDbContext db)
    {
        _db = db;
    }

    public async Task ImportCalendarAsync(
        string calendarJson,
        int tournamentId,
        int seasonId,
        int requestedRound,
        string calendarFilePath,
        CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(calendarJson);
        if (!document.RootElement.TryGetProperty("events", out JsonElement eventsElement) || eventsElement.ValueKind != JsonValueKind.Array)
            return;

        foreach (JsonElement eventElement in eventsElement.EnumerateArray())
            await UpsertCalendarEventAsync(eventElement, tournamentId, seasonId, requestedRound, calendarFilePath, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ImportIncidentsAsync(long sofaScoreEventId, string incidentsJson, string incidentsFilePath, CancellationToken cancellationToken)
    {
        MatchEntity? match = await _db.Matches.FirstOrDefaultAsync(x => x.SofaScoreEventId == sofaScoreEventId, cancellationToken);
        if (match is null)
            return;

        List<MatchEventEntity> existingEvents = await _db.MatchEvents.Where(x => x.SofaScoreEventId == sofaScoreEventId).ToListAsync(cancellationToken);
        _db.MatchEvents.RemoveRange(existingEvents);

        using JsonDocument document = JsonDocument.Parse(incidentsJson);
        if (!document.RootElement.TryGetProperty("incidents", out JsonElement incidentsElement) || incidentsElement.ValueKind != JsonValueKind.Array)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        foreach (JsonElement incident in incidentsElement.EnumerateArray())
        {
            string incidentType = GetString(incident, "incidentType");
            if (!ShouldStoreIncident(incidentType))
                continue;

            var matchEvent = new MatchEventEntity
            {
                MatchId = match.Id,
                SofaScoreEventId = sofaScoreEventId,
                SofaScoreIncidentId = GetNullableInt64(incident, "id"),
                IncidentType = incidentType,
                IncidentClass = GetString(incident, "incidentClass"),
                Minute = GetNullableInt32(incident, "time") ?? 0,
                AddedTime = GetNullableInt32(incident, "addedTime"),
                TimeSeconds = GetNullableInt32(incident, "timeSeconds"),
                IsHome = GetNullableBool(incident, "isHome") ?? false,
                HomeScore = GetNullableInt32(incident, "homeScore"),
                AwayScore = GetNullableInt32(incident, "awayScore"),
                PlayerName = GetNestedString(incident, "player", "name"),
                SofaScorePlayerId = GetNullableInt64(incident, "player", "id"),
                AssistPlayerName = GetNestedString(incident, "assist1", "name"),
                SofaScoreAssistPlayerId = GetNullableInt64(incident, "assist1", "id"),
                Reason = GetString(incident, "reason")
            };

            _db.MatchEvents.Add(matchEvent);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ImportStatisticsAsync(long sofaScoreEventId, string statisticsJson, string statisticsFilePath, CancellationToken cancellationToken)
    {
        MatchEntity? match = await _db.Matches.FirstOrDefaultAsync(x => x.SofaScoreEventId == sofaScoreEventId, cancellationToken);
        if (match is null)
            return;

        List<MatchTeamStatEntity> existingStats = await _db.MatchTeamStats.Where(x => x.SofaScoreEventId == sofaScoreEventId).ToListAsync(cancellationToken);
        _db.MatchTeamStats.RemoveRange(existingStats);

        using JsonDocument document = JsonDocument.Parse(statisticsJson);
        if (!document.RootElement.TryGetProperty("statistics", out JsonElement statisticsElement) || statisticsElement.ValueKind != JsonValueKind.Array)
        {
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        foreach (JsonElement periodElement in statisticsElement.EnumerateArray())
        {
            string period = GetString(periodElement, "period");
            if (!periodElement.TryGetProperty("groups", out JsonElement groupsElement) || groupsElement.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement groupElement in groupsElement.EnumerateArray())
            {
                string groupName = GetString(groupElement, "groupName");
                if (!groupElement.TryGetProperty("statisticsItems", out JsonElement itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (JsonElement itemElement in itemsElement.EnumerateArray())
                {
                    string key = GetString(itemElement, "key");
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    _db.MatchTeamStats.Add(new MatchTeamStatEntity
                    {
                        MatchId = match.Id,
                        SofaScoreEventId = sofaScoreEventId,
                        Period = period,
                        GroupName = groupName,
                        Key = key,
                        Name = GetString(itemElement, "name"),
                        ValueType = GetString(itemElement, "valueType"),
                        StatisticsType = GetString(itemElement, "statisticsType"),
                        HomeRaw = GetStringOrRaw(itemElement, "home"),
                        AwayRaw = GetStringOrRaw(itemElement, "away"),
                        HomeValue = GetNullableDouble(itemElement, "homeValue"),
                        AwayValue = GetNullableDouble(itemElement, "awayValue"),
                        HomeTotal = GetNullableDouble(itemElement, "homeTotal"),
                        AwayTotal = GetNullableDouble(itemElement, "awayTotal"),
                        StatisticsJsonPath = statisticsFilePath
                    });
                }
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertCalendarEventAsync(JsonElement eventElement, int requestedTournamentId, int requestedSeasonId, int requestedRound, string calendarFilePath, CancellationToken cancellationToken)
    {
        long eventId = GetNullableInt64(eventElement, "id") ?? 0;
        if (eventId <= 0)
            return;

        MatchEntity? match = await _db.Matches.FirstOrDefaultAsync(x => x.SofaScoreEventId == eventId, cancellationToken);
        if (match is null)
        {
            match = new MatchEntity { SofaScoreEventId = eventId };
            _db.Matches.Add(match);
        }

        JsonElement homeTeam = GetProperty(eventElement, "homeTeam");
        JsonElement awayTeam = GetProperty(eventElement, "awayTeam");
        long? startTimestamp = GetNullableInt64(eventElement, "startTimestamp");

        match.SofaScoreUniqueTournamentId = GetNullableInt32(eventElement, "tournament", "uniqueTournament", "id") ?? requestedTournamentId;
        match.LeagueName = Coalesce(GetNestedString(eventElement, "tournament", "uniqueTournament", "name"), GetNestedString(eventElement, "tournament", "name"));
        match.LeagueSlug = Coalesce(GetNestedString(eventElement, "tournament", "uniqueTournament", "slug"), GetNestedString(eventElement, "tournament", "slug"));
        match.CountryName = GetNestedString(eventElement, "tournament", "category", "country", "name");
        match.CountryCode = Coalesce(GetNestedString(eventElement, "tournament", "category", "country", "alpha3"), GetNestedString(eventElement, "tournament", "category", "country", "alpha2"));

        match.SofaScoreSeasonId = GetNullableInt32(eventElement, "season", "id") ?? requestedSeasonId;
        match.SeasonName = GetNestedString(eventElement, "season", "name");
        match.SeasonYear = GetNestedString(eventElement, "season", "year");
        match.RoundNumber = GetNullableInt32(eventElement, "roundInfo", "round") ?? requestedRound;

        match.HomeTeamSofaScoreId = GetNullableInt64(homeTeam, "id") ?? 0;
        match.HomeTeamName = GetString(homeTeam, "name");
        match.HomeTeamSlug = GetString(homeTeam, "slug");
        match.HomeTeamShortName = GetString(homeTeam, "shortName");

        match.AwayTeamSofaScoreId = GetNullableInt64(awayTeam, "id") ?? 0;
        match.AwayTeamName = GetString(awayTeam, "name");
        match.AwayTeamSlug = GetString(awayTeam, "slug");
        match.AwayTeamShortName = GetString(awayTeam, "shortName");

        match.Slug = GetString(eventElement, "slug");
        match.StartTimeUtc = startTimestamp.HasValue ? DateTimeOffset.FromUnixTimeSeconds(startTimestamp.Value) : null;
        match.StatusType = GetNestedString(eventElement, "status", "type");
        match.StatusDescription = GetNestedString(eventElement, "status", "description");
        match.HomeScoreCurrent = GetNullableInt32(eventElement, "homeScore", "current");
        match.AwayScoreCurrent = GetNullableInt32(eventElement, "awayScore", "current");
        match.HomeScorePeriod1 = GetNullableInt32(eventElement, "homeScore", "period1");
        match.AwayScorePeriod1 = GetNullableInt32(eventElement, "awayScore", "period1");
        match.HomeScorePeriod2 = GetNullableInt32(eventElement, "homeScore", "period2");
        match.AwayScorePeriod2 = GetNullableInt32(eventElement, "awayScore", "period2");
        match.CalendarJsonPath = calendarFilePath;
        match.CalendarUpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static bool ShouldStoreIncident(string incidentType)
        => incidentType.Equals("goal", StringComparison.OrdinalIgnoreCase)
           || incidentType.Equals("card", StringComparison.OrdinalIgnoreCase)
           || incidentType.Equals("period", StringComparison.OrdinalIgnoreCase);

    private static string Coalesce(params string[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

    private static JsonElement GetProperty(JsonElement element, string propertyName)
        => element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty(propertyName, out JsonElement value) ? value : default;

    private static string GetString(JsonElement element, string propertyName)
        => element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string GetStringOrRaw(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return string.Empty;

        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
    }

    private static string GetNestedString(JsonElement element, params string[] path)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
            return string.Empty;

        JsonElement current = element;
        foreach (string part in path)
        {
            if (!current.TryGetProperty(part, out current))
                return string.Empty;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : string.Empty;
    }

    private static int? GetNullableInt32(JsonElement element, params string[] path)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
            return null;

        JsonElement current = element;
        foreach (string part in path)
        {
            if (!current.TryGetProperty(part, out current))
                return null;
        }

        return current.TryGetInt32(out int parsed) ? parsed : null;
    }

    private static long? GetNullableInt64(JsonElement element, params string[] path)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
            return null;

        JsonElement current = element;
        foreach (string part in path)
        {
            if (!current.TryGetProperty(part, out current))
                return null;
        }

        return current.TryGetInt64(out long parsed) ? parsed : null;
    }

    private static double? GetNullableDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out double parsed) => parsed,
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
            _ => null
        };
    }

    private static bool? GetNullableBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return null;

        return value.ValueKind == JsonValueKind.True ? true : value.ValueKind == JsonValueKind.False ? false : null;
    }
}
