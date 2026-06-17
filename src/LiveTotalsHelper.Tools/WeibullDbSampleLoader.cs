using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using LiveTotalsHelper.Modeling;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Tools;

public sealed class WeibullDbSampleOptions
{
    public string League { get; set; } = string.Empty;
    public List<int> SeasonIds { get; } = [];
    public List<int> Rounds { get; } = [];
    public string GroupByColumn { get; set; } = string.Empty;
    public int MaxMinute { get; set; } = 90;
    public bool IncludeUnreliableMatches { get; set; }
    public int MaxExamples { get; set; } = 20;
}

public sealed class WeibullDbSampleResult
{
    public int MatchesChecked { get; set; }
    public int FinishedMatches { get; set; }
    public int ReliableFinishedMatches { get; set; }
    public int UnreliableFinishedMatches { get; set; }
    public int GoalsLoaded { get; set; }
    public List<int> SeasonsIncluded { get; } = [];
    public List<WeibullGoalTimingRow> Rows { get; } = [];
    public List<string> Warnings { get; } = [];
}

public sealed class WeibullDbSampleLoader
{
    private readonly LiveTotalsDbContext _db;
    private readonly WeibullDbSampleOptions _options;

    public WeibullDbSampleLoader(LiveTotalsDbContext db, WeibullDbSampleOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<WeibullDbSampleResult> LoadAsync(CancellationToken cancellationToken)
    {
        Validate();

        var result = new WeibullDbSampleResult();

        IQueryable<MatchEntity> query = _db.Matches.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(_options.League))
            query = query.Where(x => x.LeagueName == _options.League || x.LeagueSlug == _options.League);
        if (_options.SeasonIds.Count > 0)
            query = query.Where(x => _options.SeasonIds.Contains(x.SeasonId));
        if (_options.Rounds.Count > 0)
            query = query.Where(x => _options.Rounds.Contains(x.RoundNumber));

        List<MatchEntity> matches = await query
            .OrderBy(x => x.StartTimeUtc)
            .ThenBy(x => x.EventId)
            .ToListAsync(cancellationToken);

        result.MatchesChecked = matches.Count;
        result.SeasonsIncluded.AddRange(matches.Select(x => x.SeasonId).Distinct().OrderBy(x => x));

        HashSet<int> matchIds = matches.Select(x => x.Id).ToHashSet();
        List<MatchEventEntity> events = await _db.MatchEvents.AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId))
            .OrderBy(x => x.MatchId)
            .ThenBy(x => x.TimeSeconds ?? (x.Minute * 60))
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        Dictionary<int, List<MatchEventEntity>> eventsByMatch = events
            .GroupBy(x => x.MatchId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var unreliableExamples = new List<string>();

        foreach (MatchEntity match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsFinished(match))
                continue;

            result.FinishedMatches++;
            List<MatchEventEntity> matchEvents = eventsByMatch.GetValueOrDefault(match.Id) ?? [];
            List<MatchEventEntity> rawGoals = matchEvents
                .Where(x => x.IncidentType == "goal")
                .OrderBy(x => x.TimeSeconds ?? (x.Minute * 60))
                .ThenBy(x => x.Id)
                .ToList();

            int finalHome = match.HomeScoreCurrent ?? 0;
            int finalAway = match.AwayScoreCurrent ?? 0;
            GoalEventReconstruction reconstructedGoals = GoalEventScoreReconstructor.Reconstruct(match, rawGoals);
            bool reliable = reconstructedGoals.IsReliable;

            if (reliable)
            {
                result.ReliableFinishedMatches++;
            }
            else
            {
                result.UnreliableFinishedMatches++;
                if (unreliableExamples.Count < _options.MaxExamples)
                    unreliableExamples.Add($"event {match.EventId}: final {finalHome}-{finalAway}, reconstructed goals {reconstructedGoals.FinalHomeFromEvents}-{reconstructedGoals.FinalAwayFromEvents}, raw incidents={reconstructedGoals.RawGoalIncidentCount}, expanded={reconstructedGoals.ExpandedGoalCount}");
                if (!_options.IncludeUnreliableMatches)
                    continue;
            }

            foreach (ReconstructedGoalEvent goal in reconstructedGoals.Goals)
            {
                int minute = goal.Minute;
                if (minute <= 0)
                    continue;

                string groupValue = ResolveGroupValue(_options.GroupByColumn, goal.HomeBefore, goal.AwayBefore);
                result.Rows.Add(new WeibullGoalTimingRow
                {
                    Minute = Math.Min(minute, _options.MaxMinute),
                    SeasonId = match.SeasonId,
                    MatchId = match.EventId.ToString(),
                    League = match.LeagueName,
                    GroupValue = groupValue
                });
            }
        }

        result.GoalsLoaded = result.Rows.Count;

        if (unreliableExamples.Count > 0)
            result.Warnings.Add($"Unreliable matches skipped: {result.UnreliableFinishedMatches}. Examples: {string.Join("; ", unreliableExamples)}");

        return result;
    }

    private void Validate()
    {
        if (_options.MaxMinute <= 0)
            throw new ArgumentException("--max-minute must be greater than 0.");

        if (!string.IsNullOrWhiteSpace(_options.GroupByColumn) &&
            !_options.GroupByColumn.Equals("ScoreStateBefore", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("DB-backed fit currently supports only --group-by ScoreStateBefore.");
    }

    private static bool IsFinished(MatchEntity match) =>
        match.StatusType.Equals("finished", StringComparison.OrdinalIgnoreCase) ||
        match.StatusDescription.Equals("Ended", StringComparison.OrdinalIgnoreCase) ||
        match.StatusDescription.Equals("Finished", StringComparison.OrdinalIgnoreCase);

    private static string ResolveGroupValue(string groupByColumn, int homeBefore, int awayBefore)
    {
        if (string.IsNullOrWhiteSpace(groupByColumn))
            return "All";

        if (groupByColumn.Equals("ScoreStateBefore", StringComparison.OrdinalIgnoreCase))
            return ScoreStateResolver.FromScore(homeBefore, awayBefore);

        return "All";
    }
}
