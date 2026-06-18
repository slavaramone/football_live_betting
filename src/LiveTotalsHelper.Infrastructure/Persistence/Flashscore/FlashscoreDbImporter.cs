using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;

namespace LiveTotalsHelper.Infrastructure.Persistence.Flashscore;

public sealed class FlashscoreDbImporter
{
    private readonly LiveTotalsDbContext _db;

    public FlashscoreDbImporter(LiveTotalsDbContext db)
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

    public async Task ImportIncidentsAsync(string eventId, string incidentsJson, string incidentsFilePath, CancellationToken cancellationToken)
    {
        MatchEntity? match = await _db.Matches.FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);
        if (match is null)
            return;

        using JsonDocument document = JsonDocument.Parse(incidentsJson);
        ApplyStartTimestamp(match, document.RootElement);
        if (!document.RootElement.TryGetProperty("incidents", out JsonElement incidentsElement) || incidentsElement.ValueKind != JsonValueKind.Array)
        {
            await SaveChangesWithDiagnosticsAsync($"incidents empty event={eventId} file={incidentsFilePath}; existing rows preserved", cancellationToken);
            return;
        }

        List<MatchEventEntity> parsedEvents = [];
        foreach (JsonElement incident in incidentsElement.EnumerateArray())
        {
            string incidentType = GetString(incident, "incidentType");
            if (!ShouldStoreIncident(incidentType))
                continue;

            int? homeScore = GetNullableInt32(incident, "homeScore");
            int? awayScore = GetNullableInt32(incident, "awayScore");

            // Flashscore sometimes exposes duplicate/noisy goal-like rows without a score snapshot.
            // They are not reliable scoring events for timing/state reconstruction, so do not store
            // them as goals. The validator/reconstructor also ignores such legacy rows.
            if (IsGoalIncidentType(incidentType) && (!homeScore.HasValue || !awayScore.HasValue))
                continue;

            parsedEvents.Add(new MatchEventEntity
            {
                MatchId = match.Id,
                EventId = eventId,
                IncidentId = GetScalarString(incident, "id"),
                IncidentType = incidentType,
                IncidentClass = GetString(incident, "incidentClass"),
                Minute = GetNullableInt32(incident, "time") ?? 0,
                AddedTime = GetNullableInt32(incident, "addedTime"),
                TimeSeconds = GetNullableInt32(incident, "timeSeconds"),
                IsHome = GetNullableBool(incident, "isHome") ?? false,
                HomeScore = homeScore,
                AwayScore = awayScore,
                PlayerName = GetNestedString(incident, "player", "name"),
                PlayerId = GetNestedScalarString(incident, "player", "id"),
                AssistPlayerName = GetNestedString(incident, "assist1", "name"),
                AssistPlayerId = GetNestedScalarString(incident, "assist1", "id"),
                Reason = GetString(incident, "reason")
            });
        }

        if (parsedEvents.Count == 0)
        {
            await SaveChangesWithDiagnosticsAsync($"incidents parsed-empty event={eventId} file={incidentsFilePath}; existing rows preserved", cancellationToken);
            return;
        }

        List<MatchEventEntity> existingEvents = await _db.MatchEvents.Where(x => x.EventId == eventId).ToListAsync(cancellationToken);
        _db.MatchEvents.RemoveRange(existingEvents);
        _db.MatchEvents.AddRange(parsedEvents);

        await SaveChangesWithDiagnosticsAsync($"incidents event={eventId} file={incidentsFilePath}", cancellationToken);
    }

    public async Task ImportStatisticsAsync(string eventId, string statisticsJson, string statisticsFilePath, CancellationToken cancellationToken)
    {
        MatchEntity? match = await _db.Matches.FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);
        if (match is null)
            return;

        using JsonDocument document = JsonDocument.Parse(statisticsJson);
        ApplyStartTimestamp(match, document.RootElement);
        if (!document.RootElement.TryGetProperty("statistics", out JsonElement statisticsElement) || statisticsElement.ValueKind != JsonValueKind.Array)
        {
            await SaveChangesWithDiagnosticsAsync($"statistics empty event={eventId} file={statisticsFilePath}; existing rows preserved", cancellationToken);
            return;
        }

        List<MatchStatEntity> parsedStats = [];
        foreach (JsonElement periodElement in statisticsElement.EnumerateArray())
        {
            string period = GetString(periodElement, "period");
            if (!periodElement.TryGetProperty("groups", out JsonElement groupsElement) || groupsElement.ValueKind != JsonValueKind.Array)
                continue;

            var stat = new MatchStatEntity
            {
                MatchId = match.Id,
                EventId = eventId,
                Period = period,
                StatisticsJsonPath = statisticsFilePath,
                ImportedAtUtc = DateTimeOffset.UtcNow
            };
            bool hasAnyValue = false;

            foreach (JsonElement groupElement in groupsElement.EnumerateArray())
            {
                if (!groupElement.TryGetProperty("statisticsItems", out JsonElement itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (JsonElement itemElement in itemsElement.EnumerateArray())
                    hasAnyValue |= ApplyMatchStatItem(stat, itemElement);
            }

            if (hasAnyValue)
                parsedStats.Add(stat);
        }

        List<MatchStatEntity> existingStats = await _db.MatchStats.Where(x => x.EventId == eventId).ToListAsync(cancellationToken);
        if (!ShouldReplaceStatistics(existingStats, parsedStats))
        {
            await SaveChangesWithDiagnosticsAsync($"statistics parsed-incomplete event={eventId} file={statisticsFilePath}; existing rows preserved", cancellationToken);
            return;
        }

        _db.MatchStats.RemoveRange(existingStats);
        _db.MatchStats.AddRange(parsedStats);

        await SaveChangesWithDiagnosticsAsync($"statistics event={eventId} file={statisticsFilePath}", cancellationToken);
    }

    public async Task ImportOddsAsync(string eventId, string oddsJson, string oddsFilePath, CancellationToken cancellationToken)
    {
        MatchEntity? match = await _db.Matches.FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);
        if (match is null)
            return;

        List<FlashscoreOddsEntity> existingOdds = await _db.FlashscoreOdds.Where(x => x.EventId == eventId).ToListAsync(cancellationToken);
        _db.FlashscoreOdds.RemoveRange(existingOdds);

        using JsonDocument document = JsonDocument.Parse(oddsJson);
        if (!document.RootElement.TryGetProperty("markets", out JsonElement marketsElement) || marketsElement.ValueKind != JsonValueKind.Array)
        {
            await SaveChangesWithDiagnosticsAsync($"odds empty event={eventId} file={oddsFilePath}", cancellationToken);
            return;
        }

        DateTime? downloadedAtUtc = GetNullableDateTime(document.RootElement, "downloadedAtUtc");
        string rootSourceUrl = GetString(document.RootElement, "sourceUrl");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonElement marketElement in marketsElement.EnumerateArray())
        {
            string marketSourceUrl = Coalesce(GetString(marketElement, "sourceUrl"), rootSourceUrl);
            if (!marketElement.TryGetProperty("rows", out JsonElement rowsElement) || rowsElement.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement rowElement in rowsElement.EnumerateArray())
            {
                string market = GetString(rowElement, "market");
                string selection = GetString(rowElement, "selection");
                double? odds = GetNullableDouble(rowElement, "odds");
                if (string.IsNullOrWhiteSpace(market) || string.IsNullOrWhiteSpace(selection) || odds is null)
                    continue;

                string bookmaker = GetString(rowElement, "bookmaker");
                double? line = GetNullableDouble(rowElement, "line");
                if (!IsWantedTotalOdds(market, line))
                    continue;

                string key = $"{market}|{bookmaker}|{selection}|{line:0.####}|{odds.Value:0.####}";
                if (!seen.Add(key))
                    continue;

                _db.FlashscoreOdds.Add(new FlashscoreOddsEntity
                {
                    MatchId = match.Id,
                    EventId = eventId,
                    Market = market,
                    Bookmaker = bookmaker,
                    Selection = selection,
                    Line = line,
                    Odds = odds.Value,
                    SourceUrl = marketSourceUrl,
                    OddsJsonPath = oddsFilePath,
                    DownloadedAtUtc = downloadedAtUtc,
                    ImportedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

        await SaveChangesWithDiagnosticsAsync($"odds event={eventId} file={oddsFilePath}", cancellationToken);
    }

    private static bool IsWantedTotalOdds(string market, double? line)
        => market.Contains("OVER/UNDER", StringComparison.OrdinalIgnoreCase)
           && line.HasValue
           && (Math.Abs(line.Value - 2.5) < 0.0001
               || Math.Abs(line.Value - 3.5) < 0.0001);

    private static void ApplyStartTimestamp(MatchEntity match, JsonElement root)
    {
        long? startTimestamp = GetNullableInt64(root, "startTimestamp");
        if (startTimestamp.HasValue)
            match.StartTimeUtc = DateTimeOffset.FromUnixTimeSeconds(startTimestamp.Value);
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
                builder.AppendLine($"    Match: EventId={match.EventId}, Tournament={match.TournamentId}, Season={match.SeasonId}, Round={match.RoundNumber}, {match.HomeTeamName} vs {match.AwayTeamName}, Status={match.StatusType}, Start={match.StartTimeUtc:O}");
                break;
            case MatchEventEntity matchEvent:
                builder.AppendLine($"    MatchEvent: MatchId={matchEvent.MatchId}, EventId={matchEvent.EventId}, IncidentId={matchEvent.IncidentId}, Type={matchEvent.IncidentType}, Class={matchEvent.IncidentClass}, Minute={matchEvent.Minute}, Player={matchEvent.PlayerName}");
                break;
            case MatchStatEntity stat:
                builder.AppendLine($"    MatchStat: MatchId={stat.MatchId}, EventId={stat.EventId}, Period={stat.Period}, xG={stat.HomeExpectedGoals}-{stat.AwayExpectedGoals}, Shots={stat.HomeTotalShots}-{stat.AwayTotalShots}");
                break;
            case FlashscoreOddsEntity odds:
                builder.AppendLine($"    FlashscoreOdds: MatchId={odds.MatchId}, EventId={odds.EventId}, Market={odds.Market}, Selection={odds.Selection}, Line={odds.Line}, Odds={odds.Odds}");
                break;
            default:
                builder.AppendLine($"    Entity: {entity}");
                break;
        }
    }

    private static bool ApplyMatchStatItem(MatchStatEntity stat, JsonElement itemElement)
    {
        string key = NormalizeStatKey(Coalesce(GetString(itemElement, "key"), GetString(itemElement, "name")));
        double? home = GetNullableDouble(itemElement, "homeValue");
        double? away = GetNullableDouble(itemElement, "awayValue");
        if (home is null && away is null)
            return false;

        switch (key)
        {
            case "expectedgoals":
            case "expectedgoalsxg":
                stat.HomeExpectedGoals = home;
                stat.AwayExpectedGoals = away;
                return true;
            case "ballpossession":
                stat.HomeBallPossession = home;
                stat.AwayBallPossession = away;
                return true;
            case "totalshots":
            case "totalshotsongoal":
                stat.HomeTotalShots = home;
                stat.AwayTotalShots = away;
                return true;
            case "shotsontarget":
            case "shotsongoal":
                stat.HomeShotsOnTarget = home;
                stat.AwayShotsOnTarget = away;
                return true;
            case "shotsofftarget":
                stat.HomeShotsOffTarget = home;
                stat.AwayShotsOffTarget = away;
                return true;
            case "blockedshots":
                stat.HomeBlockedShots = home;
                stat.AwayBlockedShots = away;
                return true;
            case "bigchances":
                stat.HomeBigChances = home;
                stat.AwayBigChances = away;
                return true;
            case "bigchancesmissed":
                stat.HomeBigChancesMissed = home;
                stat.AwayBigChancesMissed = away;
                return true;
            case "cornerkicks":
                stat.HomeCornerKicks = home;
                stat.AwayCornerKicks = away;
                return true;
            case "fouls":
                stat.HomeFouls = home;
                stat.AwayFouls = away;
                return true;
            case "yellowcards":
                stat.HomeYellowCards = home;
                stat.AwayYellowCards = away;
                return true;
            case "redcards":
                stat.HomeRedCards = home;
                stat.AwayRedCards = away;
                return true;
            case "goalkeepersaves":
                stat.HomeGoalkeeperSaves = home;
                stat.AwayGoalkeeperSaves = away;
                return true;
            case "offsides":
                stat.HomeOffsides = home;
                stat.AwayOffsides = away;
                return true;
            case "throwins":
                stat.HomeThrowIns = home;
                stat.AwayThrowIns = away;
                return true;
            case "freekicks":
                stat.HomeFreeKicks = home;
                stat.AwayFreeKicks = away;
                return true;
            case "passes":
                stat.HomePasses = home;
                stat.AwayPasses = away;
                return true;
            case "accuratepasses":
                stat.HomeAccuratePasses = home;
                stat.AwayAccuratePasses = away;
                return true;
            case "longballs":
                stat.HomeLongBalls = home;
                stat.AwayLongBalls = away;
                return true;
            case "crosses":
                stat.HomeCrosses = home;
                stat.AwayCrosses = away;
                return true;
            case "tackles":
                stat.HomeTackles = home;
                stat.AwayTackles = away;
                return true;
            case "clearances":
                stat.HomeClearances = home;
                stat.AwayClearances = away;
                return true;
            case "touchesinoppositionbox":
                stat.HomeTouchesInOppositionBox = home;
                stat.AwayTouchesInOppositionBox = away;
                return true;
            case "finalthirdentries":
                stat.HomeFinalThirdEntries = home;
                stat.AwayFinalThirdEntries = away;
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeStatKey(string value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private async Task UpsertCalendarEventAsync(JsonElement eventElement, int requestedTournamentId, int requestedSeasonId, int requestedRound, string calendarFilePath, CancellationToken cancellationToken)
    {
        string eventId = GetScalarString(eventElement, "id");
        if (string.IsNullOrWhiteSpace(eventId))
            return;

        MatchEntity? match = await _db.Matches.FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);
        if (match is null)
        {
            match = new MatchEntity { EventId = eventId };
            _db.Matches.Add(match);
        }

        JsonElement homeTeam = GetProperty(eventElement, "homeTeam");
        JsonElement awayTeam = GetProperty(eventElement, "awayTeam");
        long? startTimestamp = GetNullableInt64(eventElement, "startTimestamp");

        match.FlashscoreId = GetString(eventElement, "flashscoreId");
        match.TournamentId = GetNullableInt32(eventElement, "tournament", "uniqueTournament", "id") ?? requestedTournamentId;
        match.LeagueName = Coalesce(GetNestedString(eventElement, "tournament", "uniqueTournament", "name"), GetNestedString(eventElement, "tournament", "name"));
        match.LeagueSlug = Coalesce(GetNestedString(eventElement, "tournament", "uniqueTournament", "slug"), GetNestedString(eventElement, "tournament", "slug"));
        match.CountryName = GetNestedString(eventElement, "tournament", "category", "country", "name");
        match.CountryCode = Coalesce(GetNestedString(eventElement, "tournament", "category", "country", "alpha3"), GetNestedString(eventElement, "tournament", "category", "country", "alpha2"));

        match.SeasonId = GetNullableInt32(eventElement, "season", "id") ?? requestedSeasonId;
        match.SeasonName = GetNestedString(eventElement, "season", "name");
        match.SeasonYear = GetNestedString(eventElement, "season", "year");
        match.RoundNumber = GetNullableInt32(eventElement, "roundInfo", "round") ?? requestedRound;

        match.HomeTeamId = GetScalarString(homeTeam, "id");
        match.HomeTeamName = GetString(homeTeam, "name");
        match.HomeTeamSlug = GetString(homeTeam, "slug");
        match.HomeTeamShortName = GetString(homeTeam, "shortName");

        match.AwayTeamId = GetScalarString(awayTeam, "id");
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

    private static bool ShouldReplaceStatistics(IReadOnlyCollection<MatchStatEntity> existingStats, IReadOnlyCollection<MatchStatEntity> parsedStats)
    {
        if (parsedStats.Count == 0)
            return false;

        if (existingStats.Count == 0)
            return true;

        bool existingHasAll = existingStats.Any(x => IsAllPeriod(x.Period));
        bool parsedHasAll = parsedStats.Any(x => IsAllPeriod(x.Period));
        if (existingHasAll && !parsedHasAll)
            return false;

        // A partial statistics payload should not erase a fuller historical import.
        if (parsedStats.Count < existingStats.Count)
            return false;

        return true;
    }

    private static bool IsAllPeriod(string period)
        => period.Equals("all", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldStoreIncident(string incidentType)
        => IsGoalIncidentType(incidentType)
           || incidentType.Equals("card", StringComparison.OrdinalIgnoreCase)
           || incidentType.Equals("period", StringComparison.OrdinalIgnoreCase);

    private static bool IsGoalIncidentType(string incidentType)
        => incidentType.Equals("goal", StringComparison.OrdinalIgnoreCase);

    private static string Coalesce(params string[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

    private static JsonElement GetProperty(JsonElement element, string propertyName)
        => element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty(propertyName, out JsonElement value) ? value : default;

    private static string GetString(JsonElement element, string propertyName)
        => element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string GetScalarString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out JsonElement value))
            return string.Empty;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
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

    private static string GetNestedScalarString(JsonElement element, params string[] path)
    {
        if (element.ValueKind == JsonValueKind.Undefined)
            return string.Empty;

        JsonElement current = element;
        foreach (string part in path)
        {
            if (!current.TryGetProperty(part, out current))
                return string.Empty;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? string.Empty,
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
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

    private static DateTime? GetNullableDateTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            return null;

        return DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static bool? GetNullableBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return null;

        return value.ValueKind == JsonValueKind.True ? true : value.ValueKind == JsonValueKind.False ? false : null;
    }
}
