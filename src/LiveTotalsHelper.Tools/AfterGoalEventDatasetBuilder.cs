using System.Globalization;
using System.Text;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Tools;

public sealed class AfterGoalEventDatasetOptions
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public int TournamentId { get; set; }
    public string Season { get; set; } = string.Empty;
    public string FromSeason { get; set; } = string.Empty;
    public string ToSeason { get; set; } = string.Empty;
    public int? MinMinute { get; set; }
    public int? MaxMinute { get; set; }
}

public sealed class AfterGoalEventBuildResult
{
    public List<AfterGoalEventRow> Rows { get; } = [];
    public List<AfterGoalEventWarning> Warnings { get; } = [];
    public int TotalMatchesScanned { get; set; }
    public int FinishedMatchesWithFinalScore { get; set; }
    public int MatchesIncluded { get; set; }
    public int MatchesSkippedNoValidGoals { get; set; }
    public int MatchesSkippedFinalScoreMismatch { get; set; }
}

public sealed class AfterGoalEventWarning
{
    public string LeagueKey { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public int? OfficialFinalHomeGoals { get; set; }
    public int? OfficialFinalAwayGoals { get; set; }
    public int? ReconstructedHomeGoals { get; set; }
    public int? ReconstructedAwayGoals { get; set; }
}

public sealed class AfterGoalEventRow
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string MatchId { get; set; } = string.Empty;
    public string MatchDate { get; set; } = string.Empty;
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public int GoalIndex { get; set; }
    public string GoalMinuteDisplay { get; set; } = string.Empty;
    public int GoalMinuteBase { get; set; }
    public int GoalStoppageMinutes { get; set; }
    public int GoalMinuteElapsed { get; set; }
    public string Period { get; set; } = string.Empty;
    public string ScoringTeam { get; set; } = string.Empty;
    public string ConcedingTeam { get; set; } = string.Empty;
    public bool IsHomeGoal { get; set; }
    public int ScoreBeforeHome { get; set; }
    public int ScoreBeforeAway { get; set; }
    public int ScoreAfterHome { get; set; }
    public int ScoreAfterAway { get; set; }
    public int TotalGoalsBefore { get; set; }
    public int TotalGoalsAfter { get; set; }
    public int ScoreGapAfter { get; set; }
    public int HomeLeadAfter { get; set; }
    public int AwayLeadAfter { get; set; }
    public bool IsEqualAfter { get; set; }
    public int RemainingGoalsAfterGoal { get; set; }
    public bool NextGoalHappened { get; set; }
    public string NextGoalMinuteDisplay { get; set; } = string.Empty;
    public string NextGoalMinuteElapsed { get; set; } = string.Empty;
    public string MinutesToNextGoal { get; set; } = string.Empty;
    public int FinalHomeGoals { get; set; }
    public int FinalAwayGoals { get; set; }
    public int FinalTotalGoals { get; set; }
}

internal sealed class StrictGoalEvent
{
    public required MatchEventEntity Source { get; init; }
    public required int GoalIndex { get; init; }
    public required int HomeBefore { get; init; }
    public required int AwayBefore { get; init; }
    public required int HomeAfter { get; init; }
    public required int AwayAfter { get; init; }
    public required bool IsHomeGoal { get; init; }
    public int BaseMinute => Math.Max(0, Source.Minute);
    public int StoppageMinutes => Math.Max(0, Source.AddedTime.GetValueOrDefault());
    public int ElapsedMinute => BaseMinute + StoppageMinutes;
    public int PeriodOrder => BaseMinute <= 45 ? 1 : BaseMinute <= 90 ? 2 : 3;
    public string MinuteDisplay => StoppageMinutes > 0
        ? string.Create(CultureInfo.InvariantCulture, $"{BaseMinute}+{StoppageMinutes}")
        : BaseMinute.ToString(CultureInfo.InvariantCulture);
}

public sealed class AfterGoalEventDatasetBuilder
{
    private static readonly string[] RowHeaders =
    [
        "LeagueKey",
        "LeagueName",
        "Season",
        "MatchId",
        "MatchDate",
        "HomeTeam",
        "AwayTeam",
        "GoalIndex",
        "GoalMinuteDisplay",
        "GoalMinuteBase",
        "GoalStoppageMinutes",
        "GoalMinuteElapsed",
        "Period",
        "ScoringTeam",
        "ConcedingTeam",
        "IsHomeGoal",
        "ScoreBeforeHome",
        "ScoreBeforeAway",
        "ScoreAfterHome",
        "ScoreAfterAway",
        "TotalGoalsBefore",
        "TotalGoalsAfter",
        "ScoreGapAfter",
        "HomeLeadAfter",
        "AwayLeadAfter",
        "IsEqualAfter",
        "RemainingGoalsAfterGoal",
        "NextGoalHappened",
        "NextGoalMinuteDisplay",
        "NextGoalMinuteElapsed",
        "MinutesToNextGoal",
        "FinalHomeGoals",
        "FinalAwayGoals",
        "FinalTotalGoals"
    ];

    private static readonly string[] WarningHeaders =
    [
        "LeagueKey",
        "Season",
        "MatchId",
        "HomeTeam",
        "AwayTeam",
        "Reason",
        "Details",
        "OfficialFinalHomeGoals",
        "OfficialFinalAwayGoals",
        "ReconstructedHomeGoals",
        "ReconstructedAwayGoals"
    ];

    private readonly LiveTotalsDbContext _db;

    public AfterGoalEventDatasetBuilder(LiveTotalsDbContext db)
    {
        _db = db;
    }

    public async Task<AfterGoalEventBuildResult> BuildAsync(AfterGoalEventDatasetOptions options, CancellationToken cancellationToken)
    {
        IQueryable<MatchEntity> query = _db.Matches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(options.LeagueName))
        {
            string league = options.LeagueName;
            query = query.Where(x => x.LeagueName == league || x.LeagueSlug == league);
        }

        if (options.TournamentId > 0)
        {
            int tournamentId = options.TournamentId;
            query = query.Where(x => x.TournamentId == tournamentId);
        }

        ApplySeasonFilters(ref query, options);

        List<MatchEntity> matches = await query
            .OrderBy(x => x.StartTimeUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var result = new AfterGoalEventBuildResult
        {
            TotalMatchesScanned = matches.Count
        };

        List<int> finishedMatchIds = matches
            .Where(x => IsFinished(x) && x.HomeScoreCurrent.HasValue && x.AwayScoreCurrent.HasValue)
            .Select(x => x.Id)
            .ToList();

        result.FinishedMatchesWithFinalScore = finishedMatchIds.Count;

        List<MatchEventEntity> goalEvents = finishedMatchIds.Count == 0
            ? []
            : await _db.MatchEvents.AsNoTracking()
                .Where(x => finishedMatchIds.Contains(x.MatchId) && x.IncidentType == "goal")
                .OrderBy(x => x.MatchId)
                .ThenBy(x => x.Minute)
                .ThenBy(x => x.AddedTime)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);

        Dictionary<int, List<MatchEventEntity>> goalsByMatch = goalEvents
            .GroupBy(x => x.MatchId)
            .ToDictionary(x => x.Key, x => x.ToList());

        foreach (MatchEntity match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsFinished(match) || match.HomeScoreCurrent is null || match.AwayScoreCurrent is null)
                continue;

            goalsByMatch.TryGetValue(match.Id, out List<MatchEventEntity>? rawGoals);
            List<StrictGoalEvent> validGoals = ReconstructStrictGoals(match, rawGoals ?? [], result);

            if (validGoals.Count == 0)
            {
                int finalTotal = match.HomeScoreCurrent.Value + match.AwayScoreCurrent.Value;
                result.MatchesSkippedNoValidGoals++;
                AddWarning(result, options, match, finalTotal == 0 ? "ScorelessMatch" : "NoValidGoals",
                    finalTotal == 0
                        ? "Official final score is 0-0; no after-goal rows are expected."
                        : "No valid scoring goal incidents with reliable score snapshots were found.",
                    reconstructedHome: 0,
                    reconstructedAway: 0);
                continue;
            }

            int reconstructedHome = validGoals[^1].HomeAfter;
            int reconstructedAway = validGoals[^1].AwayAfter;
            if (reconstructedHome != match.HomeScoreCurrent.Value || reconstructedAway != match.AwayScoreCurrent.Value)
            {
                result.MatchesSkippedFinalScoreMismatch++;
                AddWarning(result, options, match, "FinalScoreMismatch",
                    string.Create(CultureInfo.InvariantCulture, $"reconstructed={reconstructedHome}-{reconstructedAway}; official={match.HomeScoreCurrent.Value}-{match.AwayScoreCurrent.Value}"),
                    reconstructedHome,
                    reconstructedAway);
                continue;
            }

            List<StrictGoalEvent> filteredGoals = validGoals
                .Where(x => !options.MinMinute.HasValue || x.ElapsedMinute >= options.MinMinute.Value)
                .Where(x => !options.MaxMinute.HasValue || x.ElapsedMinute <= options.MaxMinute.Value)
                .ToList();

            if (filteredGoals.Count == 0)
            {
                result.MatchesSkippedNoValidGoals++;
                AddWarning(result, options, match, "NoGoalsInMinuteRange", "Valid goals exist, but none match the requested minute filters.", reconstructedHome, reconstructedAway);
                continue;
            }

            result.MatchesIncluded++;
            for (int i = 0; i < filteredGoals.Count; i++)
            {
                StrictGoalEvent goal = filteredGoals[i];
                StrictGoalEvent? next = validGoals.FirstOrDefault(x => x.GoalIndex > goal.GoalIndex);
                result.Rows.Add(CreateRow(options, match, goal, next, result, reconstructedHome, reconstructedAway));
            }

            ValidateRowsForMatch(result, options, match, validGoals, reconstructedHome, reconstructedAway);
        }

        return result;
    }

    public static async Task WriteRowsCsvAsync(string path, IReadOnlyList<AfterGoalEventRow> rows, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var writer = new StreamWriter(fullPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(string.Join(",", RowHeaders));
        foreach (AfterGoalEventRow row in rows)
            await writer.WriteLineAsync(ToCsvLine(RowValues(row)));
    }

    public static async Task WriteWarningsCsvAsync(string path, IReadOnlyList<AfterGoalEventWarning> warnings, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var writer = new StreamWriter(fullPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(string.Join(",", WarningHeaders));
        foreach (AfterGoalEventWarning warning in warnings)
            await writer.WriteLineAsync(ToCsvLine(WarningValues(warning)));
    }

    private static void ApplySeasonFilters(ref IQueryable<MatchEntity> query, AfterGoalEventDatasetOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Season))
        {
            string season = options.Season;
            if (int.TryParse(season, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seasonId))
                query = query.Where(x => x.SeasonId == seasonId || x.SeasonYear == season || x.SeasonName == season);
            else
                query = query.Where(x => x.SeasonYear == season || x.SeasonName == season);
        }

        if (!string.IsNullOrWhiteSpace(options.FromSeason) && !TryParseSeasonBound(options.FromSeason, out _))
            throw new ArgumentException("Argument --from-season must be an integer season id/year.");

        if (!string.IsNullOrWhiteSpace(options.ToSeason) && !TryParseSeasonBound(options.ToSeason, out _))
            throw new ArgumentException("Argument --to-season must be an integer season id/year.");

        if (TryParseSeasonBound(options.FromSeason, out int fromSeason))
            query = query.Where(x => x.SeasonId >= fromSeason);

        if (TryParseSeasonBound(options.ToSeason, out int toSeason))
            query = query.Where(x => x.SeasonId <= toSeason);
    }

    internal static List<StrictGoalEvent> ReconstructStrictGoals(MatchEntity match, IEnumerable<MatchEventEntity> rawGoals, AfterGoalEventBuildResult? result = null)
    {
        var goals = new List<StrictGoalEvent>();
        int home = 0;
        int away = 0;

        foreach (MatchEventEntity goal in rawGoals.OrderBy(ChronologicalSortKey).ThenBy(x => x.Id))
        {
            if (goal.HomeScore is null || goal.AwayScore is null)
                continue;

            int targetHome = goal.HomeScore.Value;
            int targetAway = goal.AwayScore.Value;
            int deltaHome = targetHome - home;
            int deltaAway = targetAway - away;
            int deltaTotal = deltaHome + deltaAway;

            if (deltaTotal != 1 || deltaHome < 0 || deltaAway < 0)
                continue;

            bool isHomeGoal = deltaHome == 1;
            goals.Add(new StrictGoalEvent
            {
                Source = goal,
                GoalIndex = goals.Count + 1,
                HomeBefore = home,
                AwayBefore = away,
                HomeAfter = targetHome,
                AwayAfter = targetAway,
                IsHomeGoal = isHomeGoal
            });

            home = targetHome;
            away = targetAway;
        }

        return goals;
    }

    private static AfterGoalEventRow CreateRow(
        AfterGoalEventDatasetOptions options,
        MatchEntity match,
        StrictGoalEvent goal,
        StrictGoalEvent? next,
        AfterGoalEventBuildResult result,
        int reconstructedHome,
        int reconstructedAway)
    {
        int finalHome = match.HomeScoreCurrent.GetValueOrDefault();
        int finalAway = match.AwayScoreCurrent.GetValueOrDefault();
        int totalAfter = goal.HomeAfter + goal.AwayAfter;
        int finalTotal = finalHome + finalAway;

        int? minutesToNextGoal = next is null
            ? null
            : CalculateGoalMinuteDelta(goal, next, result, options, match, reconstructedHome, reconstructedAway);

        return new AfterGoalEventRow
        {
            LeagueKey = Coalesce(options.LeagueKey, match.LeagueSlug, match.LeagueName),
            LeagueName = Coalesce(match.LeagueName, options.LeagueName),
            Season = SeasonLabel(match),
            MatchId = Coalesce(match.EventId, match.FlashscoreId, match.Id.ToString(CultureInfo.InvariantCulture)),
            MatchDate = match.StartTimeUtc?.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            HomeTeam = match.HomeTeamName,
            AwayTeam = match.AwayTeamName,
            GoalIndex = goal.GoalIndex,
            GoalMinuteDisplay = goal.MinuteDisplay,
            GoalMinuteBase = goal.BaseMinute,
            GoalStoppageMinutes = goal.StoppageMinutes,
            GoalMinuteElapsed = goal.ElapsedMinute,
            Period = Period(goal),
            ScoringTeam = goal.IsHomeGoal ? match.HomeTeamName : match.AwayTeamName,
            ConcedingTeam = goal.IsHomeGoal ? match.AwayTeamName : match.HomeTeamName,
            IsHomeGoal = goal.IsHomeGoal,
            ScoreBeforeHome = goal.HomeBefore,
            ScoreBeforeAway = goal.AwayBefore,
            ScoreAfterHome = goal.HomeAfter,
            ScoreAfterAway = goal.AwayAfter,
            TotalGoalsBefore = goal.HomeBefore + goal.AwayBefore,
            TotalGoalsAfter = totalAfter,
            ScoreGapAfter = Math.Abs(goal.HomeAfter - goal.AwayAfter),
            HomeLeadAfter = Math.Max(0, goal.HomeAfter - goal.AwayAfter),
            AwayLeadAfter = Math.Max(0, goal.AwayAfter - goal.HomeAfter),
            IsEqualAfter = goal.HomeAfter == goal.AwayAfter,
            RemainingGoalsAfterGoal = finalTotal - totalAfter,
            NextGoalHappened = next is not null,
            NextGoalMinuteDisplay = next?.MinuteDisplay ?? string.Empty,
            NextGoalMinuteElapsed = next?.ElapsedMinute.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            MinutesToNextGoal = minutesToNextGoal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            FinalHomeGoals = finalHome,
            FinalAwayGoals = finalAway,
            FinalTotalGoals = finalTotal
        };
    }

    private static int CalculateGoalMinuteDelta(
        StrictGoalEvent current,
        StrictGoalEvent next,
        AfterGoalEventBuildResult result,
        AfterGoalEventDatasetOptions options,
        MatchEntity match,
        int reconstructedHome,
        int reconstructedAway,
        bool warnOnClamp = true)
    {
        int delta;
        if (current.PeriodOrder == next.PeriodOrder)
        {
            delta = next.ElapsedMinute - current.ElapsedMinute;
        }
        else if (current.PeriodOrder == 1 && next.PeriodOrder == 2)
        {
            int currentClockForDelta = Math.Min(current.ElapsedMinute, 45);
            delta = next.ElapsedMinute - currentClockForDelta;
        }
        else
        {
            delta = next.ElapsedMinute - current.ElapsedMinute;
        }

        if (delta >= 0)
            return delta;

        if (warnOnClamp)
        {
            AddWarning(result, options, match, "NegativeMinuteDeltaClamped",
                string.Create(CultureInfo.InvariantCulture, $"goalIndex={current.GoalIndex}; current={current.MinuteDisplay}; next={next.MinuteDisplay}; rawDelta={delta}"),
                reconstructedHome,
                reconstructedAway);
        }

        return 0;
    }

    private static void ValidateRowsForMatch(
        AfterGoalEventBuildResult result,
        AfterGoalEventDatasetOptions options,
        MatchEntity match,
        IReadOnlyList<StrictGoalEvent> goals,
        int reconstructedHome,
        int reconstructedAway)
    {
        for (int i = 0; i < goals.Count; i++)
        {
            StrictGoalEvent goal = goals[i];
            if (goal.GoalIndex != i + 1)
            {
                AddWarning(result, options, match, "InvalidGoalIndex",
                    string.Create(CultureInfo.InvariantCulture, $"expected={i + 1}; actual={goal.GoalIndex}; minute={goal.MinuteDisplay}"),
                    reconstructedHome,
                    reconstructedAway);
            }

            bool hasNext = i < goals.Count - 1;
            if (hasNext)
            {
                StrictGoalEvent next = goals[i + 1];
                if (CompareChronology(goal, next) > 0)
                {
                    AddWarning(result, options, match, "UnstableGoalOrder",
                        string.Create(CultureInfo.InvariantCulture, $"goalIndex={goal.GoalIndex}; current={goal.MinuteDisplay}; next={next.MinuteDisplay}"),
                        reconstructedHome,
                        reconstructedAway);
                }

                CalculateGoalMinuteDelta(goal, next, result, options, match, reconstructedHome, reconstructedAway, warnOnClamp: false);
            }
        }
    }

    private static void AddWarning(
        AfterGoalEventBuildResult result,
        AfterGoalEventDatasetOptions options,
        MatchEntity match,
        string reason,
        string details,
        int? reconstructedHome = null,
        int? reconstructedAway = null)
    {
        result.Warnings.Add(new AfterGoalEventWarning
        {
            LeagueKey = Coalesce(options.LeagueKey, match.LeagueSlug, match.LeagueName),
            Season = SeasonLabel(match),
            MatchId = Coalesce(match.EventId, match.FlashscoreId, match.Id.ToString(CultureInfo.InvariantCulture)),
            HomeTeam = match.HomeTeamName,
            AwayTeam = match.AwayTeamName,
            Reason = reason,
            Details = details,
            OfficialFinalHomeGoals = match.HomeScoreCurrent,
            OfficialFinalAwayGoals = match.AwayScoreCurrent,
            ReconstructedHomeGoals = reconstructedHome,
            ReconstructedAwayGoals = reconstructedAway
        });
    }

    private static bool IsFinished(MatchEntity match)
    {
        string status = Normalize(match.StatusType);
        return status is "finished" or "ended" or "ft" or "aet";
    }

    private static int ChronologicalSortKey(MatchEventEntity matchEvent)
    {
        int minute = Math.Max(0, matchEvent.Minute);
        int added = Math.Max(0, matchEvent.AddedTime.GetValueOrDefault());
        int periodOrder = minute <= 45 ? 1 : minute <= 90 ? 2 : 3;
        return periodOrder * 1_000_000 + minute * 100 + added;
    }

    private static int CompareChronology(StrictGoalEvent left, StrictGoalEvent right)
    {
        int period = left.PeriodOrder.CompareTo(right.PeriodOrder);
        if (period != 0)
            return period;

        int minute = left.BaseMinute.CompareTo(right.BaseMinute);
        if (minute != 0)
            return minute;

        int stoppage = left.StoppageMinutes.CompareTo(right.StoppageMinutes);
        if (stoppage != 0)
            return stoppage;

        return left.Source.Id.CompareTo(right.Source.Id);
    }

    private static string Period(StrictGoalEvent goal)
    {
        if (goal.BaseMinute <= 45)
            return "1H";
        if (goal.BaseMinute <= 90)
            return "2H";
        return "ET";
    }

    private static bool TryParseSeasonBound(string value, out int season)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out season);

    private static string SeasonLabel(MatchEntity match)
        => Coalesce(match.SeasonYear, match.SeasonName, match.SeasonId.ToString(CultureInfo.InvariantCulture));

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string Coalesce(params string[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

    private static IEnumerable<string> RowValues(AfterGoalEventRow row)
    {
        yield return row.LeagueKey;
        yield return row.LeagueName;
        yield return row.Season;
        yield return row.MatchId;
        yield return row.MatchDate;
        yield return row.HomeTeam;
        yield return row.AwayTeam;
        yield return row.GoalIndex.ToString(CultureInfo.InvariantCulture);
        yield return row.GoalMinuteDisplay;
        yield return row.GoalMinuteBase.ToString(CultureInfo.InvariantCulture);
        yield return row.GoalStoppageMinutes.ToString(CultureInfo.InvariantCulture);
        yield return row.GoalMinuteElapsed.ToString(CultureInfo.InvariantCulture);
        yield return row.Period;
        yield return row.ScoringTeam;
        yield return row.ConcedingTeam;
        yield return row.IsHomeGoal.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        yield return row.ScoreBeforeHome.ToString(CultureInfo.InvariantCulture);
        yield return row.ScoreBeforeAway.ToString(CultureInfo.InvariantCulture);
        yield return row.ScoreAfterHome.ToString(CultureInfo.InvariantCulture);
        yield return row.ScoreAfterAway.ToString(CultureInfo.InvariantCulture);
        yield return row.TotalGoalsBefore.ToString(CultureInfo.InvariantCulture);
        yield return row.TotalGoalsAfter.ToString(CultureInfo.InvariantCulture);
        yield return row.ScoreGapAfter.ToString(CultureInfo.InvariantCulture);
        yield return row.HomeLeadAfter.ToString(CultureInfo.InvariantCulture);
        yield return row.AwayLeadAfter.ToString(CultureInfo.InvariantCulture);
        yield return row.IsEqualAfter.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        yield return row.RemainingGoalsAfterGoal.ToString(CultureInfo.InvariantCulture);
        yield return row.NextGoalHappened.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        yield return row.NextGoalMinuteDisplay;
        yield return row.NextGoalMinuteElapsed;
        yield return row.MinutesToNextGoal;
        yield return row.FinalHomeGoals.ToString(CultureInfo.InvariantCulture);
        yield return row.FinalAwayGoals.ToString(CultureInfo.InvariantCulture);
        yield return row.FinalTotalGoals.ToString(CultureInfo.InvariantCulture);
    }

    private static IEnumerable<string> WarningValues(AfterGoalEventWarning warning)
    {
        yield return warning.LeagueKey;
        yield return warning.Season;
        yield return warning.MatchId;
        yield return warning.HomeTeam;
        yield return warning.AwayTeam;
        yield return warning.Reason;
        yield return warning.Details;
        yield return warning.OfficialFinalHomeGoals?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        yield return warning.OfficialFinalAwayGoals?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        yield return warning.ReconstructedHomeGoals?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        yield return warning.ReconstructedAwayGoals?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string ToCsvLine(IEnumerable<string> values)
        => string.Join(",", values.Select(Csv));

    private static string Csv(string? value)
    {
        string text = value ?? string.Empty;
        return text.Contains('"') || text.Contains(',') || text.Contains('\r') || text.Contains('\n')
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }
}
