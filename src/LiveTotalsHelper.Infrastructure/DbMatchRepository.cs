using LiveTotalsHelper.Core.Models;
using LiveTotalsHelper.Core.Services;
using LiveTotalsHelper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Infrastructure;

public sealed class DbMatchRepository : IMatchRepository
{
    private readonly LiveTotalsDbContext _db;

    public DbMatchRepository(LiveTotalsDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<MatchSnapshot> GetLiveMatches(string league)
    {
        string requestedKey = NormalizeLeagueKey(league);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset from = now.AddHours(-3);
        DateTimeOffset to = DateTimeOffset.UtcNow.AddDays(21);

        List<Persistence.Entities.MatchEntity> candidates = _db.Matches
            .AsNoTracking()
            .Where(x =>
                (x.StartTimeUtc == null ||
                 (x.StartTimeUtc >= from && x.StartTimeUtc <= to) ||
                 x.StatusType.ToLower() == "inprogress" ||
                 x.StatusType.ToLower() == "live"))
            .OrderBy(x => x.StartTimeUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.RoundNumber)
            .Take(1000)
            .ToList();

        candidates = candidates
            .Where(x => !IsFinishedOrAbandonedStatus(x.StatusType))
            .Where(x => IsInProgressStatus(x.StatusType) || x.StartTimeUtc == null || x.StartTimeUtc >= from)
            .ToList();

        if (!string.IsNullOrWhiteSpace(requestedKey))
        {
            candidates = candidates
                .Where(x => IsLeagueMatch(requestedKey, x.LeagueName))
                .ToList();
        }

        return candidates
            .Take(120)
            .Select(x => new MatchSnapshot
            {
                MatchId = x.EventId,
                League = x.LeagueName,
                HomeTeam = x.HomeTeamName,
                AwayTeam = x.AwayTeamName,
                Minute = 0,
                HomeGoals = IsInProgressStatus(x.StatusType) ? x.HomeScoreCurrent ?? 0 : 0,
                AwayGoals = IsInProgressStatus(x.StatusType) ? x.AwayScoreCurrent ?? 0 : 0,
                HomeRedCards = 0,
                AwayRedCards = 0,
                IsFixture = !IsInProgressStatus(x.StatusType),
                BestSignal = BuildStatusText(x.StatusType, x.StartTimeUtc, x.RoundNumber),
                BestEdgePercent = 0.0
            })
            .ToList();
    }

    private static string BuildStatusText(string status, DateTimeOffset? startTimeUtc, int round)
    {
        string time = startTimeUtc.HasValue
            ? startTimeUtc.Value.ToLocalTime().ToString("dd.MM HH:mm")
            : "no time";

        return $"R{round} {status} {time}";
    }

    private static bool IsInProgressStatus(string status)
    {
        string normalized = NormalizeStatus(status);
        return normalized is "inprogress" or "live";
    }

    private static bool IsFinishedOrAbandonedStatus(string status)
    {
        string normalized = NormalizeStatus(status);
        return normalized is
            "finished" or
            "ended" or
            "afterextra" or
            "afterpenalties" or
            "cancelled" or
            "canceled" or
            "postponed" or
            "interrupted" or
            "abandoned" or
            "walkover" or
            "awarded";
    }

    private static string NormalizeStatus(string status)
    {
        return new string((status ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static bool IsLeagueMatch(string requestedKey, string dbLeagueName)
    {
        string dbKey = NormalizeLeagueKey(dbLeagueName);
        if (dbKey == requestedKey)
            return true;

        // Handles UI names like "Latvia 1. Liga" versus stored league names like "1.Liga" / "1. Liga".
        if (requestedKey.EndsWith(dbKey, StringComparison.OrdinalIgnoreCase))
            return true;

        if (dbKey.EndsWith(requestedKey, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string NormalizeLeagueKey(string value)
    {
        value = (value ?? string.Empty).Trim().ToLowerInvariant();

        // Country prefixes are useful in UI but often not stored in the league name.
        value = value
            .Replace("latvia", string.Empty)
            .Replace("norwegian", string.Empty)
            .Replace("swedish", string.Empty)
            .Replace("sweden", string.Empty)
            .Replace("norway", string.Empty);

        return new string(value.Where(char.IsLetterOrDigit).ToArray());
    }
}
