using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

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

        await SaveChangesWithDiagnosticsAsync($"calendar round={requestedRound} file={calendarFilePath}", cancellationToken);
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
            await SaveChangesWithDiagnosticsAsync($"incidents empty event={sofaScoreEventId} file={incidentsFilePath}", cancellationToken);
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

        await SaveChangesWithDiagnosticsAsync($"incidents event={sofaScoreEventId} file={incidentsFilePath}", cancellationToken);
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
            await SaveChangesWithDiagnosticsAsync($"statistics empty event={sofaScoreEventId} file={statisticsFilePath}", cancellationToken);
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

        await SaveChangesWithDiagnosticsAsync($"statistics event={sofaScoreEventId} file={statisticsFilePath}", cancellationToken);
    }


    private async Task SaveChangesWithDiagnosticsAsync(string operation, CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            string message = BuildDbUpdateDiagnosticMessage(operation, ex);
            _db.ChangeTracker.Clear();
            throw new InvalidOperationException(message, ex);
        }
        catch
        {
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private string BuildDbUpdateDiagnosticMessage(string operation, DbUpdateException ex)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"DB save failed during {operation}.");
        builder.AppendLine($"Exception: {ex.GetType().Name}: {ex.Message}");

        PostgresException? postgresException = FindPostgresException(ex);
        if (postgresException is not null)
        {
            builder.AppendLine("PostgreSQL details:");
            builder.AppendLine($"  SqlState: {postgresException.SqlState}");
            builder.AppendLine($"  Severity: {postgresException.Severity}");
            builder.AppendLine($"  MessageText: {postgresException.MessageText}");
            builder.AppendLine($"  Detail: {postgresException.Detail}");
            builder.AppendLine($"  Hint: {postgresException.Hint}");
            builder.AppendLine($"  SchemaName: {postgresException.SchemaName}");
            builder.AppendLine($"  TableName: {postgresException.TableName}");
            builder.AppendLine($"  ColumnName: {postgresException.ColumnName}");
            builder.AppendLine($"  ConstraintName: {postgresException.ConstraintName}");
        }

        if (ex.Entries.Count > 0)
        {
            builder.AppendLine("EF entries involved:");
            foreach (EntityEntry entry in ex.Entries)
            {
                builder.AppendLine($"  {entry.Entity.GetType().Name} State={entry.State}");
                AppendEntityPreview(builder, entry.Entity);
            }
        }

        builder.AppendLine("Inner exception chain:");
        Exception? current = ex;
        int depth = 0;
        while (current is not null && depth < 8)
        {
            builder.AppendLine($"  [{depth}] {current.GetType().FullName}: {current.Message}");
            current = current.InnerException;
            depth++;
        }

        return builder.ToString().TrimEnd();
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        Exception? current = exception;
        while (current is not null)
        {
            if (current is PostgresException postgresException)
                return postgresException;

            current = current.InnerException;
        }

        return null;
    }

    private static void AppendEntityPreview(StringBuilder builder, object entity)
    {
        switch (entity)
        {
            case MatchEntity match:
                builder.AppendLine($"    Match: EventId={match.SofaScoreEventId}, Tournament={match.SofaScoreUniqueTournamentId}, Season={match.SofaScoreSeasonId}, Round={match.RoundNumber}, {match.HomeTeamName} vs {match.AwayTeamName}, Status={match.StatusType}, Start={match.StartTimeUtc:O}");
                break;
            case MatchEventEntity matchEvent:
                builder.AppendLine($"    MatchEvent: MatchId={matchEvent.MatchId}, EventId={matchEvent.SofaScoreEventId}, IncidentId={matchEvent.SofaScoreIncidentId}, Type={matchEvent.IncidentType}, Class={matchEvent.IncidentClass}, Minute={matchEvent.Minute}, Player={matchEvent.PlayerName}");
                break;
            case MatchTeamStatEntity stat:
                builder.AppendLine($"    MatchTeamStat: MatchId={stat.MatchId}, EventId={stat.SofaScoreEventId}, Period={stat.Period}, Group={stat.GroupName}, Key={stat.Key}, Name={stat.Name}, HomeRaw={stat.HomeRaw}, AwayRaw={stat.AwayRaw}");
                break;
            default:
                builder.AppendLine($"    Entity: {entity}");
                break;
        }
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
