using System.Text.Json;

namespace LiveTotalsHelper.Infrastructure.SofaScore;

public sealed class SofaScoreEventSummary
{
    public long EventId { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public long? StartTimestamp { get; init; }
    public string StatusType { get; init; } = string.Empty;
    public string StatusDescription { get; init; } = string.Empty;
    public string TournamentName { get; init; } = string.Empty;
    public string TournamentSlug { get; init; } = string.Empty;
    public string SeasonName { get; init; } = string.Empty;
    public string SeasonYear { get; init; } = string.Empty;
    public int? Round { get; init; }

    public static IReadOnlyList<SofaScoreEventSummary> FromCalendarJson(string calendarJson)
    {
        using JsonDocument document = JsonDocument.Parse(calendarJson);
        if (!document.RootElement.TryGetProperty("events", out JsonElement eventsElement) || eventsElement.ValueKind != JsonValueKind.Array)
            return [];

        var events = new List<SofaScoreEventSummary>();
        foreach (JsonElement eventElement in eventsElement.EnumerateArray())
        {
            long eventId = eventElement.TryGetProperty("id", out JsonElement idElement) && idElement.TryGetInt64(out long id)
                ? id
                : 0;

            if (eventId <= 0)
                continue;

            events.Add(new SofaScoreEventSummary
            {
                EventId = eventId,
                Slug = GetString(eventElement, "slug"),
                HomeTeam = GetNestedString(eventElement, "homeTeam", "name"),
                AwayTeam = GetNestedString(eventElement, "awayTeam", "name"),
                StartTimestamp = GetNullableInt64(eventElement, "startTimestamp"),
                StatusType = GetNestedString(eventElement, "status", "type"),
                StatusDescription = GetNestedString(eventElement, "status", "description"),
                TournamentName = GetNestedString(eventElement, "tournament", "uniqueTournament", "name"),
                TournamentSlug = GetNestedString(eventElement, "tournament", "uniqueTournament", "slug"),
                SeasonName = GetNestedString(eventElement, "season", "name"),
                SeasonYear = GetNestedString(eventElement, "season", "year"),
                Round = GetNullableInt32(eventElement, "roundInfo", "round")
            });
        }

        return events;
    }

    private static string GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string GetNestedString(JsonElement element, params string[] path)
    {
        JsonElement current = element;
        foreach (string part in path)
        {
            if (!current.TryGetProperty(part, out current))
                return string.Empty;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : string.Empty;
    }

    private static long? GetNullableInt64(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt64(out long parsed))
            return parsed;

        return null;
    }

    private static int? GetNullableInt32(JsonElement element, params string[] path)
    {
        JsonElement current = element;
        foreach (string part in path)
        {
            if (!current.TryGetProperty(part, out current))
                return null;
        }

        return current.TryGetInt32(out int parsed) ? parsed : null;
    }
}
