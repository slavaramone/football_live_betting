using System.Globalization;
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

        List<MatchEntity> matches = await matchQuery
            .OrderBy(x => x.SeasonId)
            .ThenBy(x => x.RoundNumber)
            .ThenBy(x => x.StartTimeUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        HashSet<int> matchIds = matches.Select(x => x.Id).ToHashSet();

        List<MatchEventEntity> events = matchIds.Count == 0
            ? []
            : await _db.MatchEvents.AsNoTracking()
                .Where(x => matchIds.Contains(x.MatchId))
                .OrderBy(x => x.MatchId)
                .ThenBy(x => x.TimeSeconds ?? x.Minute * 60)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

        List<MatchStatEntity> stats = matchIds.Count == 0
            ? []
            : await _db.MatchStats.AsNoTracking()
                .Where(x => matchIds.Contains(x.MatchId))
                .OrderBy(x => x.MatchId)
                .ThenBy(x => x.Period)
                .ToListAsync(cancellationToken);

        List<FlashscoreOddsEntity> odds = matchIds.Count == 0
            ? []
            : await _db.FlashscoreOdds.AsNoTracking()
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
        AddLeagueSeasonRoundSummary(result, matches);
        AddScoringDistributionSummary(result, matches, events);
        AddGoalTimingSummary(result, matches, events);
        AddModelReadinessSummary(result, matches, events);
        AddStatsCoverageSummary(result, matches, stats);
        AddOddsCoverageSummary(result, matches, odds);

        CheckRequiredMatchFields(result, matches);
        CheckDuplicateMatches(result, matches);
        CheckChildRowIdentityConsistency(result, matches, events, stats, odds);
        CheckFinishedScoreMatchesGoalEvents(result, matches, events);
        CheckGoalEventScoreProgression(result, matches, events);
        CheckHalfTimeScoreConsistency(result, matches, events);
        CheckEventMinuteRanges(result, matches, events);
        CheckNotStartedFixturesHaveNoDetails(result, matches, events, stats);
        CheckFinishedMatchesHaveDetails(result, matches, events, stats);
        CheckDuplicateExternalIncidentIds(result, events);
        CheckDuplicateStatPeriods(result, stats, matches);
        CheckDuplicateOddsRows(result, odds, matches);
        CheckRedCardsAgainstStats(result, matches, events, stats);
        CheckModelUsefulStats(result, matches, stats);
        CheckOddsSanity(result, matches, odds);
        CheckRoundCalendarCompleteness(result, matches);

        return result;
    }

    private static void AddDatasetSummary(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events, List<MatchStatEntity> stats, List<FlashscoreOddsEntity> odds)
    {
        var byStatus = matches
            .GroupBy(x => Normalize(x.StatusType))
            .OrderByDescending(x => x.Count())
            .Select(x => $"{PrintableKey(x.Key)}: {x.Count()}")
            .ToList();

        var byEventType = events
            .GroupBy(x => Normalize(x.IncidentType))
            .OrderByDescending(x => x.Count())
            .Select(x => $"{PrintableKey(x.Key)}: {x.Count()}")
            .ToList();

        var byStatsPeriod = stats
            .GroupBy(x => Normalize(x.Period))
            .OrderByDescending(x => x.Count())
            .Select(x => $"{PrintableKey(x.Key)}: {x.Count()}")
            .ToList();

        var byOddsMarket = odds
            .GroupBy(x => Normalize(x.Market))
            .OrderByDescending(x => x.Count())
            .Select(x => $"{PrintableKey(x.Key)}: {x.Count()}")
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
        check.Examples.Add("Status counts: " + JoinOrNone(byStatus));
        check.Examples.Add("Event type counts: " + JoinOrNone(byEventType));
        check.Examples.Add("Stats period counts: " + JoinOrNone(byStatsPeriod));
        check.Examples.Add("Odds market counts: " + JoinOrNone(byOddsMarket));
        result.Add(check);
    }

    private static void AddLeagueSeasonRoundSummary(DbValidationResult result, List<MatchEntity> matches)
    {
        var check = new DbValidationCheckResult
        {
            Name = "League / season / round summary",
            Severity = DbValidationSeverity.Info,
            Message = "Calendar coverage by league, season and imported rounds."
        };

        foreach (var leagueGroup in matches
            .GroupBy(x => new { League = NonEmpty(x.LeagueName, x.LeagueSlug), x.TournamentId })
            .OrderBy(x => x.Key.League)
            .ThenBy(x => x.Key.TournamentId))
        {
            check.Examples.Add($"{leagueGroup.Key.League} tournament={leagueGroup.Key.TournamentId}: {leagueGroup.Count()} matches");

            foreach (var seasonGroup in leagueGroup.GroupBy(x => new { x.SeasonId, x.SeasonName, x.SeasonYear }).OrderBy(x => x.Key.SeasonId))
            {
                string rounds = BuildRoundRangeSummary(seasonGroup.Select(x => x.RoundNumber));
                int finished = seasonGroup.Count(IsFinished);
                int fixtures = seasonGroup.Count(IsNotStarted);
                check.Examples.Add($"  season={seasonGroup.Key.SeasonId} {NonEmpty(seasonGroup.Key.SeasonName, seasonGroup.Key.SeasonYear)}: matches={seasonGroup.Count()}, finished={finished}, notStarted={fixtures}, rounds={rounds}");
            }
        }

        if (check.Examples.Count == 0)
            check.Examples.Add("No matches matched the selected filters.");

        result.Add(check);
    }

    private static void AddScoringDistributionSummary(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events)
    {
        List<MatchEntity> finished = matches.Where(IsFinished).ToList();
        var goalEventsByMatch = events.Where(IsGoal).GroupBy(x => x.MatchId).ToDictionary(x => x.Key, x => x.Count());
        int finishedWithScore = finished.Count(x => x.HomeScoreCurrent.HasValue && x.AwayScoreCurrent.HasValue);
        double avgGoals = finishedWithScore == 0 ? 0 : finished.Where(x => x.HomeScoreCurrent.HasValue && x.AwayScoreCurrent.HasValue).Average(x => x.HomeScoreCurrent.GetValueOrDefault() + x.AwayScoreCurrent.GetValueOrDefault());
        double avgGoalEvents = finished.Count == 0 ? 0 : finished.Average(x => goalEventsByMatch.GetValueOrDefault(x.Id));
        int nilNil = finished.Count(x => (x.HomeScoreCurrent ?? -1) == 0 && (x.AwayScoreCurrent ?? -1) == 0);
        int homeWins = finished.Count(x => x.HomeScoreCurrent.HasValue && x.AwayScoreCurrent.HasValue && x.HomeScoreCurrent.Value > x.AwayScoreCurrent.Value);
        int draws = finished.Count(x => x.HomeScoreCurrent.HasValue && x.AwayScoreCurrent.HasValue && x.HomeScoreCurrent.Value == x.AwayScoreCurrent.Value);
        int awayWins = finished.Count(x => x.HomeScoreCurrent.HasValue && x.AwayScoreCurrent.HasValue && x.HomeScoreCurrent.Value < x.AwayScoreCurrent.Value);

        var check = new DbValidationCheckResult
        {
            Name = "Scoring distribution summary",
            Severity = DbValidationSeverity.Info,
            Message = "Finished-match scoring level and result distribution for model readiness."
        };

        check.Examples.Add($"Finished matches with score: {finishedWithScore}/{finished.Count}");
        check.Examples.Add($"Average final goals from match score: {avgGoals.ToString("0.###", CultureInfo.InvariantCulture)}");
        check.Examples.Add($"Average goal events per finished match: {avgGoalEvents.ToString("0.###", CultureInfo.InvariantCulture)}");
        check.Examples.Add($"0-0 matches: {nilNil} ({Percent(nilNil, Math.Max(finishedWithScore, 1))})");
        check.Examples.Add($"Result split: homeWins={homeWins} ({Percent(homeWins, Math.Max(finishedWithScore, 1))}), draws={draws} ({Percent(draws, Math.Max(finishedWithScore, 1))}), awayWins={awayWins} ({Percent(awayWins, Math.Max(finishedWithScore, 1))})");

        foreach (var bucket in finished
            .Where(x => x.HomeScoreCurrent.HasValue && x.AwayScoreCurrent.HasValue)
            .GroupBy(x => TotalGoalsBucket(x.HomeScoreCurrent.GetValueOrDefault() + x.AwayScoreCurrent.GetValueOrDefault()))
            .OrderBy(x => x.Key))
        {
            check.Examples.Add($"Total goals {bucket.Key}: {bucket.Count()} ({Percent(bucket.Count(), finishedWithScore)})");
        }

        result.Add(check);
    }

    private static void AddGoalTimingSummary(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events)
    {
        var rawGoalsByMatch = events.Where(IsGoal).GroupBy(x => x.MatchId).ToDictionary(x => x.Key, x => x.ToList());
        var allGoals = new List<ReconstructedGoalEvent>();
        var reliableGoals = new List<ReconstructedGoalEvent>();
        int rawGoalIncidents = 0;
        int scoreJumpMatches = 0;
        int scoreJumpIncidents = 0;
        int missingScoreSnapshots = 0;
        int reliableMatches = 0;
        int unreliableMatches = 0;

        foreach (MatchEntity match in matches.Where(IsFinished))
        {
            rawGoalsByMatch.TryGetValue(match.Id, out List<MatchEventEntity>? rawGoals);
            GoalEventReconstruction reconstructed = GoalEventScoreReconstructor.Reconstruct(match, rawGoals ?? []);
            rawGoalIncidents += reconstructed.RawGoalIncidentCount;
            scoreJumpIncidents += reconstructed.ScoreJumpCount;
            if (reconstructed.ScoreJumpCount > 0)
                scoreJumpMatches++;
            missingScoreSnapshots += reconstructed.MissingScoreSnapshotCount;
            allGoals.AddRange(reconstructed.Goals);

            if (reconstructed.IsReliable)
            {
                reliableMatches++;
                reliableGoals.AddRange(reconstructed.Goals);
            }
            else
            {
                unreliableMatches++;
            }
        }

        List<ReconstructedGoalEvent> goals = reliableGoals.Count > 0 ? reliableGoals : allGoals;
        int totalGoals = goals.Count;

        var check = new DbValidationCheckResult
        {
            Name = "Goal timing summary",
            Severity = DbValidationSeverity.Info,
            Message = "Goal timing and score reconstruction quality. Score snapshots are reconstructed, score jumps are expanded, and unreliable timelines are excluded from the data-quality distribution."
        };

        if (allGoals.Count == 0)
        {
            check.Examples.Add("No goal events found.");
            result.Add(check);
            return;
        }

        int excludedGoals = allGoals.Count - reliableGoals.Count;
        check.Examples.Add($"Reconstructed goals: {allGoals.Count} from {rawGoalIncidents} raw goal incidents");
        check.Examples.Add($"Model-usable reliable goals: {reliableGoals.Count}; excluded unreliable reconstructed goals: {excludedGoals}");
        check.Examples.Add($"Reliable finished matches: {reliableMatches}; unreliable finished matches skipped by default: {unreliableMatches}");
        check.Examples.Add($"Score jumps expanded: {scoreJumpIncidents} incidents in {scoreJumpMatches} matches; missing score snapshots: {missingScoreSnapshots}");

        if (totalGoals == 0)
        {
            check.Examples.Add("No reliable goal timelines found. Timing distributions below are unavailable until details are reimported or --include-unreliable is used.");
            result.Add(check);
            return;
        }

        int firstHalf = goals.Count(x => x.Minute <= 45);
        int secondHalf = goals.Count(x => x.Minute > 45 && x.Minute <= 90);
        int late = goals.Count(x => x.Minute >= 76 && x.Minute <= 90);
        int stoppage = goals.Count(x => x.Source.AddedTime.GetValueOrDefault() > 0 || EffectiveMinute(x.Source) > 90);

        check.Examples.Add($"Model-usable distribution: 1H goals: {firstHalf} ({Percent(firstHalf, totalGoals)}), 2H goals: {secondHalf} ({Percent(secondHalf, totalGoals)}), 76-90 goals: {late} ({Percent(late, totalGoals)}), stoppage/added-time flagged: {stoppage} ({Percent(stoppage, totalGoals)})");

        foreach (var bucket in goals.GroupBy(x => MinuteBucket15(x.Minute)).OrderBy(x => MinuteBucketOrder(x.Key)))
            check.Examples.Add($"{bucket.Key}: {bucket.Count()} ({Percent(bucket.Count(), totalGoals)})");

        foreach (var stateBucket in goals.GroupBy(x => ScoreStateFromScore(x.HomeBefore, x.AwayBefore)).OrderByDescending(x => x.Count()))
            check.Examples.Add($"Score state before goal {stateBucket.Key}: {stateBucket.Count()} ({Percent(stateBucket.Count(), totalGoals)})");

        result.Add(check);
    }

    private static void AddModelReadinessSummary(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events)
    {
        var rawGoalsByMatch = events.Where(IsGoal).GroupBy(x => x.MatchId).ToDictionary(x => x.Key, x => x.ToList());
        List<MatchEntity> finished = matches.Where(IsFinished).ToList();
        var examples = new List<string>();
        var reimportExamples = new List<string>();

        int reliable = 0;
        int unreliable = 0;
        int scoreMismatches = 0;
        int finalScoreGoals = 0;
        int reconstructedGoals = 0;
        int reliableReconstructedGoals = 0;
        int scoreJumpMatches = 0;
        int scoreJumpIncidents = 0;
        int noGoalEventsNonNil = 0;

        foreach (MatchEntity match in finished)
        {
            int finalHome = match.HomeScoreCurrent ?? 0;
            int finalAway = match.AwayScoreCurrent ?? 0;
            finalScoreGoals += finalHome + finalAway;

            rawGoalsByMatch.TryGetValue(match.Id, out List<MatchEventEntity>? rawGoals);
            GoalEventReconstruction reconstructed = GoalEventScoreReconstructor.Reconstruct(match, rawGoals ?? []);
            reconstructedGoals += reconstructed.ExpandedGoalCount;
            scoreJumpIncidents += reconstructed.ScoreJumpCount;
            if (reconstructed.ScoreJumpCount > 0)
                scoreJumpMatches++;

            if (finalHome + finalAway > 0 && reconstructed.RawGoalIncidentCount == 0)
                noGoalEventsNonNil++;

            if (reconstructed.IsReliable)
            {
                reliable++;
                reliableReconstructedGoals += reconstructed.ExpandedGoalCount;
            }
            else
            {
                unreliable++;
                if (!reconstructed.FinalScoreMatchesMatch)
                    scoreMismatches++;

                if (reimportExamples.Count < 25)
                    reimportExamples.Add($"event {match.EventId} r{match.RoundNumber} {match.HomeTeamName} vs {match.AwayTeamName}: final {finalHome}-{finalAway}, reconstructed {reconstructed.FinalHomeFromEvents}-{reconstructed.FinalAwayFromEvents}, raw={reconstructed.RawGoalIncidentCount}, expanded={reconstructed.ExpandedGoalCount}");
            }
        }

        examples.Add($"Finished matches: {finished.Count}");
        examples.Add($"Model-usable reliable matches: {reliable}/{finished.Count} ({Percent(reliable, Math.Max(finished.Count, 1))})");
        examples.Add($"Unreliable matches skipped by default: {unreliable}/{finished.Count} ({Percent(unreliable, Math.Max(finished.Count, 1))})");
        examples.Add($"Final-score goals: {finalScoreGoals}; reconstructed goals: {reconstructedGoals}; reliable reconstructed goals used by model: {reliableReconstructedGoals}");
        examples.Add($"Score-jump recovery: {scoreJumpIncidents} incidents in {scoreJumpMatches} matches");
        examples.Add($"Final-score mismatches after reconstruction: {scoreMismatches}; non-0-0 matches with no goal events: {noGoalEventsNonNil}");
        if (reimportExamples.Count > 0)
        {
            examples.Add("Reimport/detail-refresh candidates:");
            examples.AddRange(reimportExamples);
        }

        AddCheck(result, "Model readiness from reconstructed timelines", unreliable == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            unreliable == 0
                ? "All finished matches have reliable reconstructed timelines for model building."
                : "Some finished matches still have incomplete goal timelines. Model builders skip them by default unless includeUnreliableMatches=true.",
            examples);
    }

    private static void AddStatsCoverageSummary(DbValidationResult result, List<MatchEntity> matches, List<MatchStatEntity> stats)
    {
        List<MatchEntity> finished = matches.Where(IsFinished).ToList();
        HashSet<int> finishedIds = finished.Select(x => x.Id).ToHashSet();
        List<MatchStatEntity> allRows = stats.Where(x => Normalize(x.Period) == "all" && finishedIds.Contains(x.MatchId)).ToList();
        HashSet<int> allRowMatchIds = allRows.Select(x => x.MatchId).ToHashSet();

        var check = new DbValidationCheckResult
        {
            Name = "Statistics coverage summary",
            Severity = DbValidationSeverity.Info,
            Message = "Coverage of parsed team statistics for finished matches."
        };

        check.Examples.Add($"Finished matches: {finished.Count}");
        check.Examples.Add($"Finished matches with ALL stats row: {allRowMatchIds.Count} ({Percent(allRowMatchIds.Count, Math.Max(finished.Count, 1))})");
        AddStatCoverage(check, allRows, "expected goals", x => x.HomeExpectedGoals.HasValue || x.AwayExpectedGoals.HasValue);
        AddStatCoverage(check, allRows, "total shots", x => x.HomeTotalShots.HasValue || x.AwayTotalShots.HasValue);
        AddStatCoverage(check, allRows, "shots on target", x => x.HomeShotsOnTarget.HasValue || x.AwayShotsOnTarget.HasValue);
        AddStatCoverage(check, allRows, "big chances", x => x.HomeBigChances.HasValue || x.AwayBigChances.HasValue);
        AddStatCoverage(check, allRows, "corners", x => x.HomeCornerKicks.HasValue || x.AwayCornerKicks.HasValue);
        AddStatCoverage(check, allRows, "possession", x => x.HomeBallPossession.HasValue || x.AwayBallPossession.HasValue);
        AddStatCoverage(check, allRows, "red cards", x => x.HomeRedCards.HasValue || x.AwayRedCards.HasValue);

        result.Add(check);
    }

    private static void AddOddsCoverageSummary(DbValidationResult result, List<MatchEntity> matches, List<FlashscoreOddsEntity> odds)
    {
        HashSet<int> matchIdsWithOdds = odds.Select(x => x.MatchId).ToHashSet();
        var totals = odds.Where(IsTotalOddsRow).ToList();
        var totalPairs = BuildTotalOddsPairs(totals);

        var check = new DbValidationCheckResult
        {
            Name = "Odds coverage summary",
            Severity = DbValidationSeverity.Info,
            Message = "Imported odds coverage and Over/Under pair availability."
        };

        check.Examples.Add($"Matches with any odds: {matchIdsWithOdds.Count}/{matches.Count} ({Percent(matchIdsWithOdds.Count, Math.Max(matches.Count, 1))})");
        int totalRowsWithoutLine = totals.Count(x => x.Line is null);
        check.Examples.Add($"Total-market rows: {totals.Count}");
        check.Examples.Add($"Total-market rows without parsed line: {totalRowsWithoutLine}");
        check.Examples.Add($"Complete total Over/Under pairs: {totalPairs.Count}");

        foreach (var market in odds.GroupBy(x => Normalize(x.Market)).OrderByDescending(x => x.Count()).Take(12))
            check.Examples.Add($"Market {PrintableKey(market.Key)}: rows={market.Count()}, matches={market.Select(x => x.MatchId).Distinct().Count()}");

        foreach (var line in totalPairs.GroupBy(x => x.Line).OrderBy(x => x.Key))
        {
            var overOdds = line.Select(x => x.Over).Where(x => x > 1).ToList();
            var underOdds = line.Select(x => x.Under).Where(x => x > 1).ToList();
            string overAvg = overOdds.Count == 0 ? "-" : overOdds.Average().ToString("0.###", CultureInfo.InvariantCulture);
            string underAvg = underOdds.Count == 0 ? "-" : underOdds.Average().ToString("0.###", CultureInfo.InvariantCulture);
            check.Examples.Add($"Total line {line.Key.ToString("0.###", CultureInfo.InvariantCulture)}: pairs={line.Count()}, avgOver={overAvg}, avgUnder={underAvg}");
        }

        result.Add(check);
    }

    private static void CheckRequiredMatchFields(DbValidationResult result, List<MatchEntity> matches)
    {
        var examples = new List<string>();

        foreach (MatchEntity match in matches)
        {
            if (string.IsNullOrWhiteSpace(match.EventId))
                examples.Add($"Match dbId={match.Id}: missing event id");
            if (string.IsNullOrWhiteSpace(match.FlashscoreId))
                examples.Add(Describe(match) + ": missing Flashscore id");
            if (match.TournamentId <= 0)
                examples.Add(Describe(match) + ": missing tournament id");
            if (string.IsNullOrWhiteSpace(match.LeagueName) && string.IsNullOrWhiteSpace(match.LeagueSlug))
                examples.Add(Describe(match) + ": missing league name/slug");
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
            if (string.IsNullOrWhiteSpace(match.CalendarJsonPath))
                examples.Add(Describe(match) + ": missing calendar json path");
        }

        AddCheck(result, "Required match fields", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Error,
            examples.Count == 0 ? "All matches have required model/import identifiers." : $"{examples.Count} match field problems found.", examples);
    }

    private static void CheckDuplicateMatches(DbValidationResult result, List<MatchEntity> matches)
    {
        var examples = new List<string>();

        examples.AddRange(matches
            .Where(x => !string.IsNullOrWhiteSpace(x.EventId))
            .GroupBy(x => x.EventId)
            .Where(x => x.Count() > 1)
            .Select(x => $"eventId={x.Key}: {x.Count()} match rows ({string.Join(", ", x.Select(m => $"dbId={m.Id} r{m.RoundNumber} {m.HomeTeamName}-{m.AwayTeamName}"))})"));

        examples.AddRange(matches
            .Where(x => !string.IsNullOrWhiteSpace(x.FlashscoreId))
            .GroupBy(x => x.FlashscoreId)
            .Where(x => x.Count() > 1)
            .Select(x => $"flashscoreId={x.Key}: {x.Count()} match rows ({string.Join(", ", x.Select(m => $"dbId={m.Id} event={m.EventId}"))})"));

        examples.AddRange(matches
            .GroupBy(x => new
            {
                x.SeasonId,
                x.RoundNumber,
                Home = Normalize(x.HomeTeamName),
                Away = Normalize(x.AwayTeamName),
                Start = x.StartTimeUtc?.UtcDateTime.Date
            })
            .Where(x => x.Count() > 1)
            .Select(x => $"season={x.Key.SeasonId} round={x.Key.RoundNumber} {x.First().HomeTeamName} vs {x.First().AwayTeamName} date={x.Key.Start:yyyy-MM-dd}: {x.Count()} rows"));

        AddCheck(result, "Duplicate match rows", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Error,
            examples.Count == 0 ? "No duplicated match identifiers or fixture keys found." : $"{examples.Count} duplicate match groups found.", examples);
    }

    private static void CheckChildRowIdentityConsistency(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events, List<MatchStatEntity> stats, List<FlashscoreOddsEntity> odds)
    {
        Dictionary<int, MatchEntity> matchById = matches.ToDictionary(x => x.Id);
        var examples = new List<string>();

        foreach (MatchEventEntity row in events)
        {
            if (!matchById.TryGetValue(row.MatchId, out MatchEntity? match))
                continue;
            if (!string.Equals(row.EventId, match.EventId, StringComparison.Ordinal))
                examples.Add($"event row id={row.Id}: EventId {row.EventId} does not match parent {match.EventId}");
        }

        foreach (MatchStatEntity row in stats)
        {
            if (!matchById.TryGetValue(row.MatchId, out MatchEntity? match))
                continue;
            if (!string.Equals(row.EventId, match.EventId, StringComparison.Ordinal))
                examples.Add($"stat row id={row.Id}: EventId {row.EventId} does not match parent {match.EventId}");
        }

        foreach (FlashscoreOddsEntity row in odds)
        {
            if (!matchById.TryGetValue(row.MatchId, out MatchEntity? match))
                continue;
            if (!string.Equals(row.EventId, match.EventId, StringComparison.Ordinal))
                examples.Add($"odds row id={row.Id}: EventId {row.EventId} does not match parent {match.EventId}");
        }

        AddCheck(result, "Child row identity consistency", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Error,
            examples.Count == 0 ? "Child rows use the same EventId as their parent match." : $"{examples.Count} child rows have inconsistent EventId.", examples);
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

            eventsByMatch.TryGetValue(match.Id, out List<MatchEventEntity>? rawGoals);
            GoalEventReconstruction reconstructed = GoalEventScoreReconstructor.Reconstruct(match, rawGoals ?? []);

            if (!reconstructed.FinalScoreMatchesMatch)
                examples.Add($"{Describe(match)}: score {match.HomeScoreCurrent}-{match.AwayScoreCurrent}, reconstructed goal score {reconstructed.FinalHomeFromEvents}-{reconstructed.FinalAwayFromEvents}, raw incidents={reconstructed.RawGoalIncidentCount}, expanded={reconstructed.ExpandedGoalCount}");
        }

        AddCheck(result, "Finished score vs reconstructed goal events", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            examples.Count == 0 ? "Reconstructed goal events match the final/current score for finished matches." : $"{examples.Count} finished matches still have final-score mismatch after reconstruction. These matches are treated as unreliable and skipped by model builders by default.", examples);
    }

    private static void CheckGoalEventScoreProgression(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events)
    {
        Dictionary<int, MatchEntity> matchById = matches.ToDictionary(x => x.Id);
        var examples = new List<string>();

        foreach (IGrouping<int, MatchEventEntity> group in events.Where(IsGoal).GroupBy(x => x.MatchId))
        {
            if (!matchById.TryGetValue(group.Key, out MatchEntity? match))
                continue;

            List<MatchEventEntity> goals = group.OrderBy(EventSortKey).ThenBy(x => x.Id).ToList();
            int previousHome = 0;
            int previousAway = 0;
            foreach (MatchEventEntity goal in goals)
            {
                if (goal.HomeScore is null || goal.AwayScore is null)
                {
                    examples.Add($"{Describe(match)} goal minute {goal.Minute}: missing score after goal");
                    continue;
                }

                int deltaHome = goal.HomeScore.Value - previousHome;
                int deltaAway = goal.AwayScore.Value - previousAway;
                int deltaTotal = deltaHome + deltaAway;

                if (deltaHome < 0 || deltaAway < 0 || deltaTotal <= 0)
                {
                    examples.Add($"{Describe(match)} goal minute {goal.Minute}: invalid score snapshot {previousHome}-{previousAway} -> {goal.HomeScore}-{goal.AwayScore}");
                }
                else if (deltaTotal > 1)
                {
                    examples.Add($"{Describe(match)} goal minute {goal.Minute}: score jump {previousHome}-{previousAway} -> {goal.HomeScore}-{goal.AwayScore}; validator/model expands it into {deltaTotal} goals at this minute");
                }
                else
                {
                    bool sideFromScoreIsHome = deltaHome == 1;
                    bool ownGoal = Normalize(goal.IncidentClass).Contains("owngoal", StringComparison.Ordinal);
                    if (sideFromScoreIsHome != goal.IsHome && !ownGoal)
                        examples.Add($"{Describe(match)} goal minute {goal.Minute}: IsHome={goal.IsHome} conflicts with score progression {previousHome}-{previousAway} -> {goal.HomeScore}-{goal.AwayScore}");
                }

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
            examples.Count == 0 ? "Goal score snapshots have valid one-goal progression and side direction." : $"{examples.Count} goal score-snapshot/progression notes found.", examples);
    }

    private static void CheckHalfTimeScoreConsistency(DbValidationResult result, List<MatchEntity> matches, List<MatchEventEntity> events)
    {
        var goalsByMatch = events.Where(IsGoal).GroupBy(x => x.MatchId).ToDictionary(x => x.Key, x => x.OrderBy(EventSortKey).ThenBy(e => e.Id).ToList());
        var examples = new List<string>();

        foreach (MatchEntity match in matches.Where(IsFinished))
        {
            if (match.HomeScorePeriod1 is null && match.AwayScorePeriod1 is null)
                continue;

            goalsByMatch.TryGetValue(match.Id, out List<MatchEventEntity>? rawGoals);
            GoalEventReconstruction reconstructed = GoalEventScoreReconstructor.Reconstruct(match, rawGoals ?? []);
            int homeFirstHalfGoals = reconstructed.Goals.Count(x => x.IsHomeGoal && x.Minute <= 45);
            int awayFirstHalfGoals = reconstructed.Goals.Count(x => !x.IsHomeGoal && x.Minute <= 45);

            int periodHome = match.HomeScorePeriod1 ?? 0;
            int periodAway = match.AwayScorePeriod1 ?? 0;
            if (periodHome != homeFirstHalfGoals || periodAway != awayFirstHalfGoals)
                examples.Add($"{Describe(match)}: period1 score {periodHome}-{periodAway}, reconstructed goals <=45 {homeFirstHalfGoals}-{awayFirstHalfGoals}");

            if (match.HomeScoreCurrent.HasValue && match.HomeScorePeriod2.HasValue && match.HomeScorePeriod1.HasValue && match.HomeScorePeriod1.Value + match.HomeScorePeriod2.Value != match.HomeScoreCurrent.Value)
                examples.Add($"{Describe(match)}: home period score {match.HomeScorePeriod1}+{match.HomeScorePeriod2} != current {match.HomeScoreCurrent}");
            if (match.AwayScoreCurrent.HasValue && match.AwayScorePeriod2.HasValue && match.AwayScorePeriod1.HasValue && match.AwayScorePeriod1.Value + match.AwayScorePeriod2.Value != match.AwayScoreCurrent.Value)
                examples.Add($"{Describe(match)}: away period score {match.AwayScorePeriod1}+{match.AwayScorePeriod2} != current {match.AwayScoreCurrent}");
        }

        AddCheck(result, "Half-time / period score consistency", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            examples.Count == 0 ? "Stored period scores are consistent with reconstructed goal events where period scores exist." : $"{examples.Count} period-score inconsistencies found.", examples);
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

            if (matchEvent.AddedTime is < 0 or > 30)
                examples.Add($"{Describe(matchById, matchEvent.MatchId)} {matchEvent.IncidentType} minute {matchEvent.Minute}: suspicious addedTime {matchEvent.AddedTime}");
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
            bool hasScore = match.HomeScoreCurrent.HasValue || match.AwayScoreCurrent.HasValue;
            if (eventCount > 0 || statCount > 0 || hasScore)
                examples.Add($"{Describe(match)}: not-started fixture has score={match.HomeScoreCurrent}-{match.AwayScoreCurrent}, {eventCount} events and {statCount} stats rows");
        }

        AddCheck(result, "Future fixtures without details", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            examples.Count == 0 ? "Not-started fixtures do not have score/incidents/statistics imported." : $"{examples.Count} not-started fixtures contain detail rows.", examples);
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
            .Select(x => $"event {x.Key.EventId}, incident {x.Key.IncidentId}, type {PrintableKey(x.Key.Type)}: {x.Count()} rows")
            .ToList();

        AddCheck(result, "Duplicate external incident ids", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Error,
            examples.Count == 0 ? "No duplicated incident ids found." : $"{examples.Count} duplicated incident ids found.", examples);
    }

    private static void CheckDuplicateStatPeriods(DbValidationResult result, List<MatchStatEntity> stats, List<MatchEntity> matches)
    {
        Dictionary<int, MatchEntity> matchById = matches.ToDictionary(x => x.Id);
        var examples = stats
            .GroupBy(x => new { x.MatchId, Period = Normalize(x.Period) })
            .Where(x => x.Count() > 1)
            .Select(x => $"{Describe(matchById, x.Key.MatchId)}: period {PrintableKey(x.Key.Period)} has {x.Count()} stat rows")
            .ToList();

        AddCheck(result, "Duplicate statistics periods", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            examples.Count == 0 ? "No duplicated statistics period rows found." : $"{examples.Count} duplicated statistics periods found.", examples);
    }

    private static void CheckDuplicateOddsRows(DbValidationResult result, List<FlashscoreOddsEntity> odds, List<MatchEntity> matches)
    {
        Dictionary<int, MatchEntity> matchById = matches.ToDictionary(x => x.Id);
        List<string> exactDuplicateExamples = odds
            .GroupBy(x => new
            {
                x.MatchId,
                Market = Normalize(x.Market),
                Bookmaker = Normalize(x.Bookmaker),
                Selection = Normalize(x.Selection),
                Line = x.Line is null ? "" : x.Line.Value.ToString("0.####", CultureInfo.InvariantCulture),
                Odds = x.Odds.ToString("0.####", CultureInfo.InvariantCulture),
                Path = Normalize(x.OddsJsonPath),
                Downloaded = x.DownloadedAtUtc,
                Imported = x.ImportedAtUtc
            })
            .Where(x => x.Count() > 1)
            .Select(x => $"{Describe(matchById, x.Key.MatchId)}: exact odds duplicate market={PrintableKey(x.Key.Market)} bookmaker={PrintableKey(x.Key.Bookmaker)} selection={PrintableKey(x.Key.Selection)} line={x.Key.Line} odds={x.Key.Odds} count={x.Count()}")
            .ToList();

        int multiPriceGroups = odds
            .GroupBy(x => new
            {
                x.MatchId,
                Market = Normalize(x.Market),
                Bookmaker = Normalize(x.Bookmaker),
                Selection = Normalize(x.Selection),
                Line = x.Line is null ? "" : x.Line.Value.ToString("0.####", CultureInfo.InvariantCulture)
            })
            .Count(x => x.Select(row => row.Odds.ToString("0.####", CultureInfo.InvariantCulture)).Distinct().Count() > 1);

        var examples = new List<string>();
        examples.AddRange(exactDuplicateExamples);
        if (exactDuplicateExamples.Count == 0 && multiPriceGroups > 0)
            examples.Add($"No exact duplicate rows. {multiPriceGroups} market/selection/line groups contain multiple prices; treated as odds history/depth, not duplicates.");

        AddCheck(result, "Duplicate odds rows", exactDuplicateExamples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            exactDuplicateExamples.Count == 0 ? "No exact duplicate odds rows found." : $"{exactDuplicateExamples.Count} exact duplicate odds groups found.", examples);
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

        string[] usefulKeys = ["totalshotsongoal", "shotsongoal", "cornerkicks", "ballpossession"];
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

    private static void CheckOddsSanity(DbValidationResult result, List<MatchEntity> matches, List<FlashscoreOddsEntity> odds)
    {
        Dictionary<int, MatchEntity> matchById = matches.ToDictionary(x => x.Id);
        var examples = new List<string>();

        foreach (FlashscoreOddsEntity row in odds)
        {
            if (row.Odds <= 1.0 || row.Odds > 1000)
                examples.Add($"{Describe(matchById, row.MatchId)}: suspicious odds {row.Odds.ToString("0.###", CultureInfo.InvariantCulture)} market={row.Market} selection={row.Selection} line={row.Line}");
        }

        foreach (TotalOddsPair pair in BuildTotalOddsPairs(odds.Where(IsTotalOddsRow)).Where(x => x.Over > 1 && x.Under > 1))
        {
            double overRound = 1.0 / pair.Over + 1.0 / pair.Under;
            if (overRound < 0.98 || overRound > 1.35)
                examples.Add($"{Describe(matchById, pair.MatchId)}: total line {pair.Line.ToString("0.###", CultureInfo.InvariantCulture)} bookmaker={pair.Bookmaker} overround={overRound.ToString("0.###", CultureInfo.InvariantCulture)} over={pair.Over} under={pair.Under}");
        }

        AddCheck(result, "Odds sanity", examples.Count == 0 ? DbValidationSeverity.Info : DbValidationSeverity.Warning,
            examples.Count == 0 ? "Odds are inside expected ranges and total pairs have reasonable overround." : $"{examples.Count} suspicious odds rows/pairs found.", examples);
    }

    private static void CheckRoundCalendarCompleteness(DbValidationResult result, List<MatchEntity> matches)
    {
        var examples = new List<string>();

        var groups = matches
            .GroupBy(x => new { x.SeasonId, x.RoundNumber })
            .OrderBy(x => x.Key.SeasonId)
            .ThenBy(x => x.Key.RoundNumber)
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

    private static void AddStatCoverage(DbValidationCheckResult check, List<MatchStatEntity> allRows, string label, Func<MatchStatEntity, bool> predicate)
    {
        int count = allRows.Count(predicate);
        check.Examples.Add($"ALL stats with {label}: {count}/{allRows.Count} ({Percent(count, Math.Max(allRows.Count, 1))})");
    }

    private static List<TotalOddsPair> BuildTotalOddsPairs(IEnumerable<FlashscoreOddsEntity> totalRows)
    {
        var pairs = new List<TotalOddsPair>();
        foreach (var group in totalRows
            .Where(x => x.Line.HasValue)
            .GroupBy(x => new { x.MatchId, Bookmaker = Normalize(x.Bookmaker), Line = Math.Round(x.Line!.Value, 4) }))
        {
            FlashscoreOddsEntity? over = group.Where(x => IsOverSelection(x.Selection)).OrderBy(OddsSortKey).ThenBy(x => x.Id).LastOrDefault();
            FlashscoreOddsEntity? under = group.Where(x => IsUnderSelection(x.Selection)).OrderBy(OddsSortKey).ThenBy(x => x.Id).LastOrDefault();
            if (over is null || under is null)
                continue;

            pairs.Add(new TotalOddsPair(group.Key.MatchId, group.Key.Bookmaker, group.Key.Line, over.Odds, under.Odds));
        }

        return pairs;
    }

    private static DateTimeOffset OddsSortKey(FlashscoreOddsEntity row)
        => row.DownloadedAtUtc.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(row.DownloadedAtUtc.Value, DateTimeKind.Utc))
            : row.ImportedAtUtc;

    private static bool IsFinished(MatchEntity match)
    {
        string status = Normalize(match.StatusType);
        return status is "finished" or "ended" or "afterpenalties" or "aet" or "ap" or "ft";
    }

    private static bool IsNotStarted(MatchEntity match)
    {
        string status = Normalize(match.StatusType);
        return status is "notstarted" or "not_started" or "scheduled" or "fixture" or "future" or "ns";
    }

    private static bool IsGoal(MatchEventEntity matchEvent)
        => Normalize(matchEvent.IncidentType) == "goal";

    private static bool IsCard(MatchEventEntity matchEvent)
        => Normalize(matchEvent.IncidentType) == "card";

    private static bool IsHomeRedCard(MatchEventEntity matchEvent)
        => IsCard(matchEvent) && matchEvent.IsHome && Normalize(matchEvent.IncidentClass).Contains("red", StringComparison.Ordinal);

    private static bool IsAwayRedCard(MatchEventEntity matchEvent)
        => IsCard(matchEvent) && !matchEvent.IsHome && Normalize(matchEvent.IncidentClass).Contains("red", StringComparison.Ordinal);

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

    private static bool IsTotalOddsRow(FlashscoreOddsEntity row)
    {
        string market = Normalize(row.Market);
        return market.Contains("total", StringComparison.Ordinal)
               || market.Contains("overunder", StringComparison.Ordinal)
               || market.Contains("goals", StringComparison.Ordinal);
    }

    private static bool IsOverSelection(string selection)
    {
        string normalized = Normalize(selection);
        return normalized.StartsWith("over", StringComparison.Ordinal) || normalized == "o" || normalized.Contains("over", StringComparison.Ordinal);
    }

    private static bool IsUnderSelection(string selection)
    {
        string normalized = Normalize(selection);
        return normalized.StartsWith("under", StringComparison.Ordinal) || normalized == "u" || normalized.Contains("under", StringComparison.Ordinal);
    }

    private static int EffectiveMinute(MatchEventEntity matchEvent)
    {
        if (matchEvent.Minute > 0)
            return matchEvent.Minute;
        if (matchEvent.TimeSeconds.HasValue)
            return Math.Max(0, matchEvent.TimeSeconds.Value / 60);
        return 0;
    }

    private static int EventSortKey(MatchEventEntity matchEvent)
        => matchEvent.TimeSeconds ?? (EffectiveMinute(matchEvent) * 60 + matchEvent.AddedTime.GetValueOrDefault() * 60);

    private static string ScoreStateFromScore(int homeBefore, int awayBefore)
    {
        if (homeBefore < 0 || awayBefore < 0)
            return "Unknown";
        if (homeBefore == 0 && awayBefore == 0)
            return "NilNil";
        int diff = Math.Abs(homeBefore - awayBefore);
        if (diff == 0)
            return "LevelWithGoals";
        if (diff == 1)
            return "OneGoalMargin";
        if (diff == 2)
            return "TwoGoalMargin";
        return "ThreePlusGoalMargin";
    }

    private static string MinuteBucket15(int minute)
    {
        if (minute <= 15)
            return "1-15";
        if (minute <= 30)
            return "16-30";
        if (minute <= 45)
            return "31-45";
        if (minute <= 60)
            return "46-60";
        if (minute <= 75)
            return "61-75";
        if (minute <= 90)
            return "76-90";
        return "90+";
    }

    private static int MinuteBucketOrder(string bucket)
        => bucket switch
        {
            "1-15" => 1,
            "16-30" => 2,
            "31-45" => 3,
            "46-60" => 4,
            "61-75" => 5,
            "76-90" => 6,
            "90+" => 7,
            _ => 99
        };

    private static string TotalGoalsBucket(int totalGoals)
        => totalGoals >= 6 ? "6+" : totalGoals.ToString(CultureInfo.InvariantCulture);

    private static string BuildRoundRangeSummary(IEnumerable<int> rounds)
    {
        List<int> values = rounds.Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        if (values.Count == 0)
            return "none";
        return $"{values.First()}-{values.Last()} ({values.Count} distinct)";
    }

    private static string Percent(int numerator, int denominator)
    {
        if (denominator <= 0)
            return "0.0%";
        return (100.0 * numerator / denominator).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? ""
            : value.Trim()
                .Replace(" ", "", StringComparison.Ordinal)
                .Replace("_", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal)
                .Replace("/", "", StringComparison.Ordinal)
                .Replace("\\", "", StringComparison.Ordinal)
                .ToLowerInvariant();

    private static string PrintableKey(string value)
        => string.IsNullOrWhiteSpace(value) ? "<empty>" : value;

    private static string NonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "<empty>";

    private static string JoinOrNone(List<string> values)
        => values.Count == 0 ? "none" : string.Join(", ", values);

    private static string Describe(MatchEntity match)
        => $"event {NonEmpty(match.EventId, match.FlashscoreId)} r{match.RoundNumber} {match.HomeTeamName} vs {match.AwayTeamName}";

    private static string Describe(Dictionary<int, MatchEntity> matchesById, int matchId)
        => matchesById.TryGetValue(matchId, out MatchEntity? match) ? Describe(match) : $"matchId {matchId}";

    private sealed record TotalOddsPair(int MatchId, string Bookmaker, double Line, double Over, double Under);
}

public sealed class DbValidationOptions
{
    public string League { get; set; } = string.Empty;
    public int SeasonId { get; set; }
    public List<int> Rounds { get; } = [];
    public bool FailOnWarnings { get; set; }
    public int MaxExamplesPerCheck { get; set; } = 20;
    public string OutputPath { get; set; } = string.Empty;
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
