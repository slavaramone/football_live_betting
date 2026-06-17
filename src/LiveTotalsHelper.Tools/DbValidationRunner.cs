using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Tools;

public sealed class DbValidationRunner
{
    private readonly LiveTotalsDbContext _db;
    private readonly DbValidationOptions _options;

    public DbValidationRunner(LiveTotalsDbContext db, DbValidationOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<DbValidationResult> RunAsync(CancellationToken cancellationToken)
    {
        IQueryable<MatchEntity> matchQuery = _db.Matches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(_options.League))
            matchQuery = matchQuery.Where(x => x.LeagueName == _options.League || x.LeagueSlug == _options.League);

        if (_options.SeasonId > 0)
            matchQuery = matchQuery.Where(x => x.SeasonId == _options.SeasonId);

        if (_options.Rounds.Count > 0)
            matchQuery = matchQuery.Where(x => _options.Rounds.Contains(x.RoundNumber));

        List<MatchEntity> matches = await matchQuery.OrderBy(x => x.RoundNumber).ThenBy(x => x.StartTimeUtc).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        HashSet<int> matchIds = matches.Select(x => x.Id).ToHashSet();

        List<MatchEventEntity> events = await _db.MatchEvents.AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId))
            .OrderBy(x => x.MatchId)
            .ThenBy(x => x.TimeSeconds ?? x.Minute * 60)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        List<MatchStatEntity> stats = await _db.MatchStats.AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId))
            .OrderBy(x => x.MatchId)
            .ThenBy(x => x.Period)
            .ToListAsync(cancellationToken);

        List<FlashscoreOddsEntity> odds = await _db.FlashscoreOdds.AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId))
            .OrderBy(x => x.MatchId)
            .ThenBy(x => x.Market)
            .ThenBy(x => x.Line)
            .ThenBy(x => x.Selection)
            .ToListAsync(cancellationToken);

        var result = new DbValidationResult
        {
            MatchesChecked = matches.Count,
            EventsChecked = events.Count,
            MatchStatsChecked = stats.Count,
            OddsChecked = odds.Count
        };

        AddDatasetSummary(result, matches, events, stats, odds);
        CheckRequiredMatchFields(result, matches);
        CheckFinishedScoreMatchesGoalEvents(result, matches, events);
        CheckGoalEventScoreProgression(result, matches, events);
        CheckEventMinuteRanges(result, matches, events);
        CheckNotStartedFixturesHaveNoDetails(result, matches, events, stats);
        CheckFinishedMatchesHaveDetails(result, matches, events, stats);
        CheckDuplicateExternalIncidentIds(result, events);
        CheckRedCardsAgainstStats(result, matches, events, stats);
        CheckModelUsefulStats(result, matches, stats);
        CheckRoundCalendarCompleteness(result, matches);

        return result;
    }

    private static void AddDatasetSummary(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events, List<MatchStatEntity> stats, List<FlashscoreOddsEntity> odds)
    {
        var byStatus = matches
            .GroupBy(x => Normalize(x.StatusType))
            .OrderByDescending(x => x.Count())
            .Select(x => $"{x.Key}: {x.Count()}")
            .ToList();

        var byEventType = events
            .GroupBy(x => Normalize(x.IncidentType))
            .OrderByDescending(x => x.Count())
            .Select(x => $"{x.Key}: {x.Count()}")
            .ToList();

        var byStatsPeriod = stats
            .GroupBy(x => Normalize(x.Period))
            .OrderByDescending(x => x.Count())
            .Select(x => $"{x.Key}: {x.Count()}")
            .ToList();

        var byOddsMarket = odds
            .GroupBy(x => Normalize(x.Market))
            .OrderByDescending(x => x.Count())
            .Select(x => $"{x.Key}: {x.Count()}")
            .ToList();

        var check = new DbValidationCheckResult
        {
            Name = "Dataset summary",
            Severity = DbValidationSeverity.Info,
            Message = "Basic imported row counts and distributions."
        };
        check.Examples.Add($"Matches: {matches.Count}");
        check.Examples.Add($"MatchEvents: {events.Count}");
        check.Examples.Add($"MatchStats: {stats.Count}");
        check.Examples.Add($"FlashscoreOdds: {odds.Count}");
        check.Examples.Add("Status counts: " + (byStatus.Count == 0 ? "none" : string.Join(", ", byStatus)));
        check.Examples.Add("Event type counts: " + (byEventType.Count == 0 ? "none" : string.Join(", ", byEventType)));
        check.Examples.Add("Stats period counts: " + (byStatsPeriod.Count == 0 ? "none" : string.Join(", ", byStatsPeriod)));
        check.Examples.Add("Odds market counts: " + (byOddsMarket.Count == 0 ? "none" : string.Join(", ", byOddsMarket)));
        result.Add(check);
    }

    private static void CheckRequiredMatchFields(DbValidationResult result, List<MatchEntity> matches)
    {
        var examples = new List<string>();

        foreach (MatchEntity match in matches)
        {
            if (string.IsNullOrWhiteSpace(match.EventId))
                examples.Add($"Match dbId={match.Id}: missing event id");
            if (match.SeasonId <= 0)
                examples.Add(Describe(match) + ": missing season id");
            if (match.RoundNumber <= 0)
                examples.Add(Describe(match) + ": missing/invalid round number");
            if (string.IsNullOrWhiteSpace(match.HomeTeamId) || string.IsNullOrWhiteSpace(match.AwayTeamId))
                examples.Add(Describe(match) + ": missing team id");
            if (match.HomeTeamId == match.AwayTeamId && !string.IsNullOrWhiteSpace(match.HomeTeamId))
                examples.Add(Describe(match) + ": home and away team ids are the same");
            if (string.IsNullOrWhiteSpace(match.HomeTeamName) || string.IsNullOrWhiteSpace(match.AwayTeamName))
                examples.Add(Describe(match) + ": missing team name");
            if (match.StartTimeUtc is null)
                examples.Add(Describe(match) + ": missing start time");
        }

        AddCheck(result, "Required match fields", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Error,
            examples.Count == 0 ? "All matches have required model/import identifiers." : $"{examples.Count} match field problems found.", examples);
    }

    private static void CheckFinishedScoreMatchesGoalEvents(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events)
    {
        var eventsByMatch = events.Where(IsGoal).GroupBy(x => x.MatchId).ToDictionary(x => x.Key, x => x.ToList());
        var examples = new List<string>();

        foreach (MatchEntity match in matches.Where(IsFinished))
        {
            if (match.HomeScoreCurrent is null || match.AwayScoreCurrent is null)
            {
                examples.Add(Describe(match) + ": finished match has null current score");
                continue;
            }

            eventsByMatch.TryGetValue(match.Id, out List<MatchEventEntity>? goals);
            int homeGoalsFromEvents = goals?.Count(x => x.IsHome) ?? 0;
            int awayGoalsFromEvents = goals?.Count(x => !x.IsHome) ?? 0;

            if (homeGoalsFromEvents != match.HomeScoreCurrent.Value || awayGoalsFromEvents != match.AwayScoreCurrent.Value)
            {
                examples.Add($"{Describe(match)}: score {match.HomeScoreCurrent}-{match.AwayScoreCurrent}, goal events {homeGoalsFromEvents}-{awayGoalsFromEvents}");
            }
        }

        AddCheck(result, "Finished score vs goal events", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Error,
            examples.Count == 0 ? "Goal events match the final/current score for finished matches." : $"{examples.Count} finished matches have score/goal-event mismatch.", examples);
    }

    private static void CheckGoalEventScoreProgression(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events)
    {
        Dictionary<int, MatchEntity> matchById = matches.ToDictionary(x => x.Id);
        var examples = new List<string>();

        foreach (IGrouping<int, MatchEventEntity> group in events.Where(IsGoal).GroupBy(x => x.MatchId))
        {
            if (!matchById.TryGetValue(group.Key, out MatchEntity? match))
                continue;

            List<MatchEventEntity> goals = group.OrderBy(x => x.TimeSeconds ?? x.Minute * 60).ThenBy(x => x.Id).ToList();
            int previousHome = 0;
            int previousAway = 0;
            for (int i = 0; i < goals.Count; i++)
            {
                MatchEventEntity goal = goals[i];
                if (goal.HomeScore is null || goal.AwayScore is null)
                {
                    examples.Add($"{Describe(match)} goal minute {goal.Minute}: missing score after goal");
                    continue;
                }

                int deltaHome = goal.HomeScore.Value - previousHome;
                int deltaAway = goal.AwayScore.Value - previousAway;
                if (deltaHome + deltaAway != 1 || deltaHome < 0 || deltaAway < 0)
                    examples.Add($"{Describe(match)} goal minute {goal.Minute}: impossible score progression {previousHome}-{previousAway} -> {goal.HomeScore}-{goal.AwayScore}");

                previousHome = goal.HomeScore.Value;
                previousAway = goal.AwayScore.Value;
            }

            MatchEventEntity? lastGoal = goals.LastOrDefault();
            if (lastGoal is not null && IsFinished(match) && match.HomeScoreCurrent is not null && match.AwayScoreCurrent is not null)
            {
                if (lastGoal.HomeScore != match.HomeScoreCurrent || lastGoal.AwayScore != match.AwayScoreCurrent)
                    examples.Add($"{Describe(match)}: last goal score {lastGoal.HomeScore}-{lastGoal.AwayScore} does not equal match score {match.HomeScoreCurrent}-{match.AwayScoreCurrent}");
            }
        }

        AddCheck(result, "Goal score progression", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            examples.Count == 0 ? "Goal events have valid score progression." : $"{examples.Count} goal progression issues found.", examples);
    }

    private static void CheckEventMinuteRanges(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events)
    {
        Dictionary<int, MatchEntity> matchById = matches.ToDictionary(x => x.Id);
        var examples = new List<string>();

        foreach (MatchEventEntity matchEvent in events.Where(x => IsGoal(x) || IsCard(x)))
        {
            if (matchEvent.Minute < 0 || matchEvent.Minute > 130)
                examples.Add($"{Describe(matchById, matchEvent.MatchId)} {matchEvent.IncidentType} incidentId={matchEvent.IncidentId}: invalid minute {matchEvent.Minute}");

            if (matchEvent.TimeSeconds is < 0 or > 7800)
                examples.Add($"{Describe(matchById, matchEvent.MatchId)} {matchEvent.IncidentType} minute {matchEvent.Minute}: invalid timeSeconds {matchEvent.TimeSeconds}");
        }

        AddCheck(result, "Event minute ranges", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            examples.Count == 0 ? "Goal/card event minutes are inside expected football ranges." : $"{examples.Count} invalid event times found.", examples);
    }

    private static void CheckNotStartedFixturesHaveNoDetails(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events, List<MatchStatEntity> stats)
    {
        var eventsByMatch = events.GroupBy(x => x.MatchId).ToDictionary(x => x.Key, x => x.Count());
        var statsByMatch = stats.GroupBy(x => x.MatchId).ToDictionary(x => x.Key, x => x.Count());
        var examples = new List<string>();

        foreach (MatchEntity match in matches.Where(IsNotStarted))
        {
            int eventCount = eventsByMatch.GetValueOrDefault(match.Id);
            int statCount = statsByMatch.GetValueOrDefault(match.Id);
            if (eventCount > 0 || statCount > 0)
                examples.Add($"{Describe(match)}: not-started fixture has {eventCount} events and {statCount} stats rows");
        }

        AddCheck(result, "Future fixtures without details", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            examples.Count == 0 ? "Not-started fixtures do not have incidents/statistics imported." : $"{examples.Count} not-started fixtures contain detail rows.", examples);
    }

    private static void CheckFinishedMatchesHaveDetails(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events, List<MatchStatEntity> stats)
    {
        var eventsByMatch = events.GroupBy(x => x.MatchId).ToDictionary(x => x.Key, x => x.ToList());
        var statsByMatch = stats.GroupBy(x => x.MatchId).ToDictionary(x => x.Key, x => x.Count());
        var examples = new List<string>();

        foreach (MatchEntity match in matches.Where(IsFinished))
        {
            int eventCount = eventsByMatch.GetValueOrDefault(match.Id)?.Count ?? 0;
            int statCount = statsByMatch.GetValueOrDefault(match.Id);
            int expectedTotalGoals = (match.HomeScoreCurrent ?? 0) + (match.AwayScoreCurrent ?? 0);
            int goalCount = eventsByMatch.GetValueOrDefault(match.Id)?.Count(IsGoal) ?? 0;

            if (expectedTotalGoals > 0 && goalCount == 0)
                examples.Add($"{Describe(match)}: finished non-0-0 match has no goal events");
            if (eventCount == 0)
                examples.Add($"{Describe(match)}: finished match has no stored incidents");
            if (statCount == 0)
                examples.Add($"{Describe(match)}: finished match has no team statistics");
        }

        AddCheck(result, "Finished matches have details", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            examples.Count == 0 ? "Finished matches have expected event/stat detail rows." : $"{examples.Count} missing-detail issues found for finished matches.", examples);
    }

    private static void CheckDuplicateExternalIncidentIds(DbValidationResult result, List<MatchEventEntity> events)
    {
        var examples = events
            .Where(x => !string.IsNullOrWhiteSpace(x.IncidentId))
            .GroupBy(x => new { x.EventId, x.IncidentId, Type = Normalize(x.IncidentType) })
            .Where(x => x.Count() > 1)
            .Select(x => $"event {x.Key.EventId}, incident {x.Key.IncidentId}, type {x.Key.Type}: {x.Count()} rows")
            .ToList();

        AddCheck(result, "Duplicate external incident ids", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Error,
            examples.Count == 0 ? "No duplicated incident ids found." : $"{examples.Count} duplicated incident ids found.", examples);
    }

    private static void CheckRedCardsAgainstStats(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events, List<MatchStatEntity> stats)
    {
        Dictionary<int, MatchEntity> matchById = matches.ToDictionary(x => x.Id);
        var cardRowsByMatch = events.Where(IsCard).GroupBy(x => x.MatchId).ToDictionary(x => x.Key, x => x.ToList());
        var redCardStats = stats
            .Where(x => Normalize(x.Period) == "all" && (x.HomeRedCards.HasValue || x.AwayRedCards.HasValue))
            .GroupBy(x => x.MatchId)
            .Select(x => x.First())
            .ToList();

        var examples = new List<string>();
        foreach (MatchStatEntity stat in redCardStats)
        {
            if (!matchById.TryGetValue(stat.MatchId, out MatchEntity? match))
                continue;

            cardRowsByMatch.TryGetValue(stat.MatchId, out List<MatchEventEntity>? cards);
            int homeRedCards = cards?.Count(IsHomeRedCard) ?? 0;
            int awayRedCards = cards?.Count(IsAwayRedCard) ?? 0;
            int statHome = Convert.ToInt32(stat.HomeRedCards ?? 0);
            int statAway = Convert.ToInt32(stat.AwayRedCards ?? 0);

            if (homeRedCards != statHome || awayRedCards != statAway)
                examples.Add($"{Describe(match)}: redCards stat {statHome}-{statAway}, card events {homeRedCards}-{awayRedCards}");
        }

        AddCheck(result, "Red-card stats vs card events", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            examples.Count == 0 ? "Red-card stat rows match red-card incidents where available." : $"{examples.Count} red-card stat/event mismatches found.", examples);
    }

    private static void CheckModelUsefulStats(DbValidationResult result, List<MatchEntity> matches, List<MatchStatEntity> stats)
    {
        var statsByMatch = stats.GroupBy(x => x.MatchId).ToDictionary(x => x.Key, x => x.ToList());
        var examples = new List<string>();

        string[] usefulKeys = ["expectedgoals", "totalshotsongoal", "shotsongoal", "cornerkicks", "ballpossession"];
        foreach (MatchEntity match in matches.Where(IsFinished))
        {
            if (!statsByMatch.TryGetValue(match.Id, out List<MatchStatEntity>? matchStats) || matchStats.Count == 0)
                continue;

            MatchStatEntity? allStats = matchStats.FirstOrDefault(x => Normalize(x.Period) == "all");
            if (allStats is null)
                continue;

            List<string> missing = usefulKeys.Where(key => !HasUsefulStat(allStats, key)).ToList();
            if (missing.Count > 0)
                examples.Add($"{Describe(match)}: missing useful stat keys: {string.Join(", ", missing)}");
        }

        AddCheck(result, "Model-useful team stats coverage", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            examples.Count == 0 ? "Finished matches with statistics contain the main useful keys." : $"{examples.Count} finished matches with stats miss useful keys.", examples);
    }

    private static void CheckRoundCalendarCompleteness(DbValidationResult result, List<MatchEntity> matches)
    {
        var examples = new List<string>();

        var groups = matches
            .GroupBy(x => new { x.SeasonId, x.RoundNumber })
            .OrderBy(x => x.Key.RoundNumber)
            .ToList();

        foreach (var group in groups)
        {
            int count = group.Count();
            if (count == 0)
                continue;

            if (count < 2)
                examples.Add($"season {group.Key.SeasonId} round {group.Key.RoundNumber}: only {count} match imported");
        }

        AddCheck(result, "Round calendar completeness", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            examples.Count == 0 ? "Every imported round has at least two matches." : $"{examples.Count} suspiciously small round calendars found.", examples);
    }

    private static void AddCheck(DbValidationResult result, string name, DbValidationSeverity severity, string message, List<string> examples)
    {
        result.Add(new DbValidationCheckResult
        {
            Name = name,
            Severity = severity,
            Message = message,
            Examples = examples
        });
    }

    private static bool IsFinished(MatchEntity match)
    {
        string status = Normalize(match.StatusType);
        return status is "finished" or "ended" or "afterpenalties" or "aet";
    }

    private static bool IsNotStarted(MatchEntity match)
    {
        string status = Normalize(match.StatusType);
        return status is "notstarted" or "not_started" or "scheduled";
    }

    private static bool IsGoal(MatchEventEntity matchEvent)
        => Normalize(matchEvent.IncidentType) == "goal";

    private static bool IsCard(MatchEventEntity matchEvent)
        => Normalize(matchEvent.IncidentType) == "card";

    private static bool IsHomeRedCard(MatchEventEntity matchEvent)
        => IsCard(matchEvent) && matchEvent.IsHome && Normalize(matchEvent.IncidentClass).Contains("red");

    private static bool IsAwayRedCard(MatchEventEntity matchEvent)
        => IsCard(matchEvent) && !matchEvent.IsHome && Normalize(matchEvent.IncidentClass).Contains("red");

    private static bool HasUsefulStat(MatchStatEntity stat, string key)
        => key switch
        {
            "expectedgoals" => stat.HomeExpectedGoals.HasValue || stat.AwayExpectedGoals.HasValue,
            "totalshotsongoal" => stat.HomeTotalShots.HasValue || stat.AwayTotalShots.HasValue,
            "shotsongoal" => stat.HomeShotsOnTarget.HasValue || stat.AwayShotsOnTarget.HasValue,
            "cornerkicks" => stat.HomeCornerKicks.HasValue || stat.AwayCornerKicks.HasValue,
            "ballpossession" => stat.HomeBallPossession.HasValue || stat.AwayBallPossession.HasValue,
            _ => false
        };

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? "" : value.Trim().Replace(" ", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();

    private static string Describe(MatchEntity match)
        => $"event {match.EventId} r{match.RoundNumber} {match.HomeTeamName} vs {match.AwayTeamName}";

    private static string Describe(Dictionary<int, MatchEntity> matchesById, int matchId)
        => matchesById.TryGetValue(matchId, out MatchEntity? match) ? Describe(match) : $"matchId {matchId}";
}

public sealed class DbValidationOptions
{
    public string League { get; set; } = string.Empty;
    public int SeasonId { get; set; }
    public List<int> Rounds { get; } = [];
    public bool FailOnWarnings { get; set; }
    public int MaxExamplesPerCheck { get; set; } = 20;
}

public sealed class DbValidationResult
{
    public int MatchesChecked { get; set; }
    public int EventsChecked { get; set; }
    public int MatchStatsChecked { get; set; }
    public int OddsChecked { get; set; }
    public List<DbValidationCheckResult> Checks { get; } = [];
    public int ErrorCount => Checks.Count(x => x.Severity == DbValidationSeverity.Error);
    public int WarningCount => Checks.Count(x => x.Severity == DbValidationSeverity.Warning);
    public int InfoCount => Checks.Count(x => x.Severity == DbValidationSeverity.Info);

    public void Add(DbValidationCheckResult check) => Checks.Add(check);
}

public sealed class DbValidationCheckResult
{
    public string Name { get; set; } = string.Empty;
    public DbValidationSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Examples { get; set; } = [];
}

public enum DbValidationSeverity
{
    Info,
    Warning,
    Error
}
