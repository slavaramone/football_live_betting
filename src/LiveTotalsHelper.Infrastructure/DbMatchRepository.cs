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
        DateTimeOffset from = DateTimeOffset.UtcNow.AddDays(-7);
        DateTimeOffset to = DateTimeOffset.UtcNow.AddDays(21);

        List<Persistence.Entities.MatchEntity> candidates = _db.Matches
            .AsNoTracking()
            .Where(x => x.StartTimeUtc == null || (x.StartTimeUtc >= from && x.StartTimeUtc <= to) || x.StatusType.ToLower() == "inprogress")
            .OrderBy(x => x.StartTimeUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.RoundNumber)
            .Take(500)
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
                MatchId = x.SofaScoreEventId.ToString(),
                League = x.LeagueName,
                HomeTeam = x.HomeTeamName,
                AwayTeam = x.AwayTeamName,
                Minute = 0,
                HomeGoals = x.HomeScoreCurrent ?? 0,
                AwayGoals = x.AwayScoreCurrent ?? 0,
                HomeRedCards = 0,
                AwayRedCards = 0,
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

    private static bool IsLeagueMatch(string requestedKey, string dbLeagueName)
    {
        string dbKey = NormalizeLeagueKey(dbLeagueName);
        if (dbKey == requestedKey)
            return true;

        // Handles UI names like "Latvia 1. Liga" versus SofaScore DB names like "1.Liga" / "1. Liga".
        if (requestedKey.EndsWith(dbKey, StringComparison.OrdinalIgnoreCase))
            return true;

        if (dbKey.EndsWith(requestedKey, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string NormalizeLeagueKey(string value)
    {
        value = (value ?? string.Empty).Trim().ToLowerInvariant();

        // Country prefixes are useful in UI but often not stored in SofaScore league name.
        value = value
            .Replace("latvia", string.Empty)
            .Replace("norwegian", string.Empty)
            .Replace("swedish", string.Empty)
            .Replace("sweden", string.Empty)
            .Replace("norway", string.Empty);

        return new string(value.Where(char.IsLetterOrDigit).ToArray());
    }
}
