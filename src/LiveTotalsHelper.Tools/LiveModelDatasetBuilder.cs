using System.Globalization;
using System.Text;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Tools;

public sealed class LiveModelDatasetOptions
{
    public string League { get; set; } = string.Empty;
    public int SeasonId { get; set; }
    public List<int> SeasonIds { get; } = [];
    public List<int> Rounds { get; } = [];
    public string OutputPath { get; set; } = string.Empty;
    public int FromMinute { get; set; } = 1;
    public int ToMinute { get; set; } = 89;
    public int MinuteStep { get; set; } = 1;
    public int HistoryMatches { get; set; } = 10;
    public int MaxModelMinute { get; set; } = 90;
    public bool IncludeUnreliableMatches { get; set; }
    public int MaxExamples { get; set; } = 20;
}

public sealed class LiveModelDatasetResult
{
    public int MatchesChecked { get; set; }
    public int FinishedMatches { get; set; }
    public int ReliableFinishedMatches { get; set; }
    public int UnreliableFinishedMatches { get; set; }
    public int SnapshotRowsWritten { get; set; }
    public List<int> SeasonsIncluded { get; } = [];
    public string OutputPath { get; set; } = string.Empty;
    public List<string> Warnings { get; } = [];
}

public sealed class LiveModelDatasetBuilder
{
    private readonly LiveTotalsDbContext _db;
    private readonly LiveModelDatasetOptions _options;

    public LiveModelDatasetBuilder(LiveTotalsDbContext db, LiveModelDatasetOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<LiveModelDatasetResult> BuildAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        var result = new LiveModelDatasetResult { OutputPath = _options.OutputPath };
        List<int> requestedSeasonIds = GetSeasonIds(_options);

        IQueryable<MatchEntity> selectedQuery = _db.Matches.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(_options.League))
            selectedQuery = selectedQuery.Where(x => x.LeagueName == _options.League || x.LeagueSlug == _options.League);
        if (requestedSeasonIds.Count > 0)
            selectedQuery = selectedQuery.Where(x => requestedSeasonIds.Contains(x.SofaScoreSeasonId));
        if (_options.Rounds.Count > 0)
            selectedQuery = selectedQuery.Where(x => _options.Rounds.Contains(x.RoundNumber));

        List<MatchEntity> selectedMatches = await selectedQuery
            .OrderBy(x => x.StartTimeUtc)
            .ThenBy(x => x.SofaScoreEventId)
            .ToListAsync(cancellationToken);

        result.MatchesChecked = selectedMatches.Count;
        result.SeasonsIncluded.AddRange(selectedMatches.Select(x => x.SofaScoreSeasonId).Distinct().OrderBy(x => x));

        if (selectedMatches.Count == 0)
        {
            result.Warnings.Add("No matches found for the provided filters.");
            string emptyPath = ResolveOutputPath(_options.OutputPath, _options.League, requestedSeasonIds);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(emptyPath)) ?? ".");
            await File.WriteAllTextAsync(emptyPath, ToCsv([]), Encoding.UTF8, cancellationToken);
            result.OutputPath = emptyPath;
            return result;
        }

        // Load all finished league matches up to the end of the selected period so historical features
        // can use only matches played before each target match.
        DateTimeOffset? maxSelectedStart = selectedMatches
            .Where(x => x.StartTimeUtc.HasValue)
            .Select(x => x.StartTimeUtc)
            .Max();
        if (!maxSelectedStart.HasValue)
            throw new InvalidOperationException("Selected matches do not contain StartTimeUtc values, so chronological history features cannot be built.");
        IQueryable<MatchEntity> historyMatchQuery = _db.Matches.AsNoTracking()
            .Where(x => x.StartTimeUtc != null && x.StartTimeUtc <= maxSelectedStart.Value);
        if (!string.IsNullOrWhiteSpace(_options.League))
            historyMatchQuery = historyMatchQuery.Where(x => x.LeagueName == _options.League || x.LeagueSlug == _options.League);

        List<MatchEntity> allLeagueMatches = await historyMatchQuery
            .OrderBy(x => x.StartTimeUtc)
            .ThenBy(x => x.SofaScoreEventId)
            .ToListAsync(cancellationToken);

        HashSet<int> selectedMatchIds = selectedMatches.Select(x => x.Id).ToHashSet();
        HashSet<int> allMatchIds = allLeagueMatches.Select(x => x.Id).ToHashSet();

        List<MatchEventEntity> allEvents = await _db.MatchEvents.AsNoTracking()
            .Where(x => allMatchIds.Contains(x.MatchId))
            .OrderBy(x => x.MatchId)
            .ThenBy(x => x.TimeSeconds ?? (x.Minute * 60))
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        List<MatchTeamStatEntity> allStats = await _db.MatchTeamStats.AsNoTracking()
            .Where(x => allMatchIds.Contains(x.MatchId))
            .ToListAsync(cancellationToken);

        Dictionary<int, List<MatchEventEntity>> eventsByMatch = allEvents
            .GroupBy(x => x.MatchId)
            .ToDictionary(x => x.Key, x => x.ToList());

        Dictionary<int, Dictionary<string, MatchTeamStatEntity>> statsByMatch = allStats
            .Where(x => Normalize(x.Period) == "all")
            .GroupBy(x => x.MatchId)
            .ToDictionary(
                x => x.Key,
                x => x.GroupBy(s => Normalize(s.Key)).ToDictionary(g => g.Key, g => g.First()));

        var historyByTeam = new Dictionary<long, List<TeamHistoricalMatch>>();
        var rows = new List<LiveModelDatasetRow>();
        var unreliableExamples = new List<string>();

        foreach (MatchEntity match in allLeagueMatches)
        {
            if (!IsFinished(match))
                continue;

            eventsByMatch.TryGetValue(match.Id, out List<MatchEventEntity>? matchEvents);
            matchEvents ??= [];
            List<MatchEventEntity> matchGoals = matchEvents.Where(IsGoal).ToList();

            int finalHome = match.HomeScoreCurrent ?? 0;
            int finalAway = match.AwayScoreCurrent ?? 0;
            int eventHomeGoals = matchGoals.Count(x => x.IsHome);
            int eventAwayGoals = matchGoals.Count(x => !x.IsHome);
            bool reliable = finalHome == eventHomeGoals && finalAway == eventAwayGoals;

            if (selectedMatchIds.Contains(match.Id))
            {
                result.FinishedMatches++;
                if (reliable)
                    result.ReliableFinishedMatches++;
                else
                {
                    result.UnreliableFinishedMatches++;
                    if (unreliableExamples.Count < _options.MaxExamples)
                        unreliableExamples.Add($"event {match.SofaScoreEventId} r{match.RoundNumber} {match.HomeTeamName} vs {match.AwayTeamName}: score {finalHome}-{finalAway}, goal events {eventHomeGoals}-{eventAwayGoals}");
                }

                if (reliable || _options.IncludeUnreliableMatches)
                {
                    TeamHistoryFeatures homeHistory = BuildHistoryFeatures(historyByTeam.GetValueOrDefault(match.HomeTeamSofaScoreId), _options.HistoryMatches);
                    TeamHistoryFeatures awayHistory = BuildHistoryFeatures(historyByTeam.GetValueOrDefault(match.AwayTeamSofaScoreId), _options.HistoryMatches);

                    foreach (int minute in SnapshotMinutes())
                    {
                        rows.Add(BuildSnapshotRow(match, matchEvents, matchGoals, finalHome, finalAway, reliable, minute, homeHistory, awayHistory));
                    }
                }
            }

            if (reliable)
            {
                AddHistoricalMatch(historyByTeam, match, statsByMatch.GetValueOrDefault(match.Id), finalHome, finalAway);
            }
        }

        if (unreliableExamples.Count > 0)
        {
            result.Warnings.Add($"Excluded {result.UnreliableFinishedMatches} unreliable finished matches because score does not match goal events.");
            result.Warnings.AddRange(unreliableExamples);
        }

        string outputPath = ResolveOutputPath(_options.OutputPath, _options.League, requestedSeasonIds);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
        await File.WriteAllTextAsync(outputPath, ToCsv(rows), Encoding.UTF8, cancellationToken);

        result.SnapshotRowsWritten = rows.Count;
        result.OutputPath = outputPath;
        return result;
    }

    private LiveModelDatasetRow BuildSnapshotRow(
        MatchEntity match,
        IReadOnlyCollection<MatchEventEntity> matchEvents,
        IReadOnlyCollection<MatchEventEntity> matchGoals,
        int finalHome,
        int finalAway,
        bool reliable,
        int minute,
        TeamHistoryFeatures homeHistory,
        TeamHistoryFeatures awayHistory)
    {
        List<MatchEventEntity> goalsSoFar = matchGoals.Where(x => EventMinute(x) <= minute).ToList();
        List<MatchEventEntity> cardsSoFar = matchEvents.Where(IsRedCard).Where(x => EventMinute(x) <= minute).ToList();

        int homeGoals = goalsSoFar.Count(x => x.IsHome);
        int awayGoals = goalsSoFar.Count(x => !x.IsHome);
        int totalGoals = homeGoals + awayGoals;
        int goalDiff = homeGoals - awayGoals;
        int absGoalDiff = Math.Abs(goalDiff);
        int homeRedCards = cardsSoFar.Count(x => x.IsHome);
        int awayRedCards = cardsSoFar.Count(x => !x.IsHome);
        int? lastGoalMinute = goalsSoFar.Count == 0 ? null : goalsSoFar.Max(EventMinute);

        return new LiveModelDatasetRow
        {
            LeagueName = match.LeagueName,
            LeagueSlug = match.LeagueSlug,
            SofaScoreUniqueTournamentId = match.SofaScoreUniqueTournamentId,
            SofaScoreSeasonId = match.SofaScoreSeasonId,
            SeasonName = match.SeasonName,
            SeasonYear = match.SeasonYear,
            RoundNumber = match.RoundNumber,
            MatchId = match.Id,
            SofaScoreEventId = match.SofaScoreEventId,
            StartTimeUtc = match.StartTimeUtc,
            HomeTeamSofaScoreId = match.HomeTeamSofaScoreId,
            HomeTeamName = match.HomeTeamName,
            AwayTeamSofaScoreId = match.AwayTeamSofaScoreId,
            AwayTeamName = match.AwayTeamName,
            Minute = minute,
            Phase = ResolvePhase(minute),
            HomeGoals = homeGoals,
            AwayGoals = awayGoals,
            TotalGoals = totalGoals,
            GoalDifference = goalDiff,
            AbsGoalDifference = absGoalDiff,
            ScoreState = ScoreState(absGoalDiff),
            LeadingTeam = goalDiff > 0 ? "Home" : goalDiff < 0 ? "Away" : "Level",
            HomeRedCards = homeRedCards,
            AwayRedCards = awayRedCards,
            RedCardDifference = homeRedCards - awayRedCards,
            AnyRedCard = homeRedCards + awayRedCards > 0,
            LastGoalMinute = lastGoalMinute,
            MinutesSinceLastGoal = lastGoalMinute.HasValue ? minute - lastGoalMinute.Value : null,
            GoalsLast5Minutes = goalsSoFar.Count(x => EventMinute(x) > minute - 5),
            GoalsLast10Minutes = goalsSoFar.Count(x => EventMinute(x) > minute - 10),
            GoalsLast15Minutes = goalsSoFar.Count(x => EventMinute(x) > minute - 15),
            HomeGoalsLast15Minutes = goalsSoFar.Count(x => x.IsHome && EventMinute(x) > minute - 15),
            AwayGoalsLast15Minutes = goalsSoFar.Count(x => !x.IsHome && EventMinute(x) > minute - 15),
            HomeHistory = homeHistory,
            AwayHistory = awayHistory,
            HistoryGoalDiffPerMatch = homeHistory.GoalsForPerMatch - awayHistory.GoalsForPerMatch,
            HistoryXgDiffPerMatch = homeHistory.ExpectedGoalsForPerMatch - awayHistory.ExpectedGoalsForPerMatch,
            HistoryShotsOnGoalDiffPerMatch = homeHistory.ShotsOnGoalForPerMatch - awayHistory.ShotsOnGoalForPerMatch,
            HistoryTotalShotsOnGoalDiffPerMatch = homeHistory.TotalShotsOnGoalForPerMatch - awayHistory.TotalShotsOnGoalForPerMatch,
            HistoryCornerDiffPerMatch = homeHistory.CornerKicksForPerMatch - awayHistory.CornerKicksForPerMatch,
            HistoryPossessionDiffPerMatch = homeHistory.BallPossessionForPerMatch - awayHistory.BallPossessionForPerMatch,
            HistoryRedCardDiffPerMatch = homeHistory.RedCardsForPerMatch - awayHistory.RedCardsForPerMatch,
            FinalHomeGoals = finalHome,
            FinalAwayGoals = finalAway,
            FinalTotalGoals = finalHome + finalAway,
            RemainingHomeGoals = finalHome - homeGoals,
            RemainingAwayGoals = finalAway - awayGoals,
            RemainingTotalGoals = finalHome + finalAway - totalGoals,
            AnyGoalAfterSnapshot = finalHome + finalAway > totalGoals,
            IsReliableMatch = reliable
        };
    }

    private static TeamHistoryFeatures BuildHistoryFeatures(IReadOnlyCollection<TeamHistoricalMatch>? history, int historyMatches)
    {
        TeamHistoricalMatch[] matches = history is null
            ? []
            : history.OrderByDescending(x => x.StartTimeUtc).Take(historyMatches).ToArray();

        return new TeamHistoryFeatures
        {
            MatchesUsed = matches.Length,
            GoalsForPerMatch = Average(matches, x => x.GoalsFor),
            GoalsAgainstPerMatch = Average(matches, x => x.GoalsAgainst),
            ExpectedGoalsForPerMatch = Average(matches, x => x.ExpectedGoalsFor),
            ExpectedGoalsAgainstPerMatch = Average(matches, x => x.ExpectedGoalsAgainst),
            TotalShotsOnGoalForPerMatch = Average(matches, x => x.TotalShotsOnGoalFor),
            TotalShotsOnGoalAgainstPerMatch = Average(matches, x => x.TotalShotsOnGoalAgainst),
            ShotsOnGoalForPerMatch = Average(matches, x => x.ShotsOnGoalFor),
            ShotsOnGoalAgainstPerMatch = Average(matches, x => x.ShotsOnGoalAgainst),
            CornerKicksForPerMatch = Average(matches, x => x.CornerKicksFor),
            CornerKicksAgainstPerMatch = Average(matches, x => x.CornerKicksAgainst),
            BallPossessionForPerMatch = Average(matches, x => x.BallPossessionFor),
            BallPossessionAgainstPerMatch = Average(matches, x => x.BallPossessionAgainst),
            RedCardsForPerMatch = Average(matches, x => x.RedCardsFor),
            RedCardsAgainstPerMatch = Average(matches, x => x.RedCardsAgainst)
        };
    }

    private static void AddHistoricalMatch(
        IDictionary<long, List<TeamHistoricalMatch>> historyByTeam,
        MatchEntity match,
        IReadOnlyDictionary<string, MatchTeamStatEntity>? stats,
        int finalHome,
        int finalAway)
    {
        AddOne(match.HomeTeamSofaScoreId, new TeamHistoricalMatch
        {
            StartTimeUtc = match.StartTimeUtc ?? DateTimeOffset.MinValue,
            GoalsFor = finalHome,
            GoalsAgainst = finalAway,
            ExpectedGoalsFor = Stat(stats, "expectedgoals", true),
            ExpectedGoalsAgainst = Stat(stats, "expectedgoals", false),
            TotalShotsOnGoalFor = Stat(stats, "totalshotsongoal", true),
            TotalShotsOnGoalAgainst = Stat(stats, "totalshotsongoal", false),
            ShotsOnGoalFor = Stat(stats, "shotsongoal", true),
            ShotsOnGoalAgainst = Stat(stats, "shotsongoal", false),
            CornerKicksFor = Stat(stats, "cornerkicks", true),
            CornerKicksAgainst = Stat(stats, "cornerkicks", false),
            BallPossessionFor = Stat(stats, "ballpossession", true),
            BallPossessionAgainst = Stat(stats, "ballpossession", false),
            RedCardsFor = Stat(stats, "redcards", true),
            RedCardsAgainst = Stat(stats, "redcards", false)
        });

        AddOne(match.AwayTeamSofaScoreId, new TeamHistoricalMatch
        {
            StartTimeUtc = match.StartTimeUtc ?? DateTimeOffset.MinValue,
            GoalsFor = finalAway,
            GoalsAgainst = finalHome,
            ExpectedGoalsFor = Stat(stats, "expectedgoals", false),
            ExpectedGoalsAgainst = Stat(stats, "expectedgoals", true),
            TotalShotsOnGoalFor = Stat(stats, "totalshotsongoal", false),
            TotalShotsOnGoalAgainst = Stat(stats, "totalshotsongoal", true),
            ShotsOnGoalFor = Stat(stats, "shotsongoal", false),
            ShotsOnGoalAgainst = Stat(stats, "shotsongoal", true),
            CornerKicksFor = Stat(stats, "cornerkicks", false),
            CornerKicksAgainst = Stat(stats, "cornerkicks", true),
            BallPossessionFor = Stat(stats, "ballpossession", false),
            BallPossessionAgainst = Stat(stats, "ballpossession", true),
            RedCardsFor = Stat(stats, "redcards", false),
            RedCardsAgainst = Stat(stats, "redcards", true)
        });

        void AddOne(long teamId, TeamHistoricalMatch item)
        {
            if (!historyByTeam.TryGetValue(teamId, out List<TeamHistoricalMatch>? history))
            {
                history = [];
                historyByTeam[teamId] = history;
            }
            history.Add(item);
        }
    }

    private IEnumerable<int> SnapshotMinutes()
    {
        for (int minute = _options.FromMinute; minute <= _options.ToMinute; minute += _options.MinuteStep)
            yield return minute;
    }

    private static double? Stat(IReadOnlyDictionary<string, MatchTeamStatEntity>? stats, string key, bool home)
    {
        if (stats is null || !stats.TryGetValue(key, out MatchTeamStatEntity? stat))
            return null;

        return home ? stat.HomeValue : stat.AwayValue;
    }

    private static double Average<T>(IReadOnlyCollection<T> rows, Func<T, double?> selector)
    {
        double[] values = rows.Select(selector).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        return values.Length == 0 ? 0.0 : values.Average();
    }

    private static int EventMinute(MatchEventEntity item)
    {
        int minute;
        if (item.TimeSeconds is > 0)
            minute = Math.Max(1, (int)Math.Ceiling(item.TimeSeconds.Value / 60.0));
        else
            minute = Math.Max(1, item.Minute + Math.Max(0, item.AddedTime ?? 0));

        return Math.Min(minute, 90);
    }

    private static bool IsFinished(MatchEntity match)
        => string.Equals(match.StatusType, "finished", StringComparison.OrdinalIgnoreCase)
           || string.Equals(match.StatusDescription, "Ended", StringComparison.OrdinalIgnoreCase)
           || string.Equals(match.StatusDescription, "Finished", StringComparison.OrdinalIgnoreCase);

    private static bool IsGoal(MatchEventEntity item)
        => string.Equals(item.IncidentType, "goal", StringComparison.OrdinalIgnoreCase);

    private static bool IsRedCard(MatchEventEntity item)
        => string.Equals(item.IncidentType, "card", StringComparison.OrdinalIgnoreCase)
           && Normalize(item.IncidentClass).Contains("red", StringComparison.Ordinal);

    private static string ResolvePhase(int minute)
        => minute <= 45 ? "FirstHalf" : "SecondHalf";

    private static string ScoreState(int absGoalDiff)
        => absGoalDiff switch
        {
            0 => "Level",
            1 => "OneGoalMargin",
            2 => "TwoGoalMargin",
            _ => "ThreePlusGoalMargin"
        };

    private static List<int> GetSeasonIds(LiveModelDatasetOptions options)
    {
        var seasonIds = options.SeasonIds.Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        if (options.SeasonId > 0 && !seasonIds.Contains(options.SeasonId))
        {
            seasonIds.Add(options.SeasonId);
            seasonIds.Sort();
        }
        return seasonIds;
    }

    private static string ResolveOutputPath(string outputPath, string league, IReadOnlyCollection<int> seasonIds)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
            return outputPath;

        string leaguePart = string.IsNullOrWhiteSpace(league) ? "all-leagues" : SlugifySimple(league);
        string seasonPart = seasonIds.Count switch
        {
            0 => "all-seasons",
            1 => $"season-{seasonIds.First()}",
            _ => $"seasons-{seasonIds.Count}"
        };
        return Path.Combine("data", "datasets", $"{leaguePart}-{seasonPart}-live-model.csv");
    }

    private static string SlugifySimple(string value)
    {
        var sb = new StringBuilder();
        foreach (char ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    private void ValidateOptions()
    {
        if (_options.FromMinute < 1)
            throw new ArgumentException("--from-minute must be at least 1.");
        if (_options.ToMinute < _options.FromMinute)
            throw new ArgumentException("--to-minute must be >= --from-minute.");
        if (_options.ToMinute > _options.MaxModelMinute)
            throw new ArgumentException("--to-minute must be <= --max-model-minute.");
        if (_options.MinuteStep < 1)
            throw new ArgumentException("--minute-step must be at least 1.");
        if (_options.HistoryMatches < 1)
            throw new ArgumentException("--history-matches must be at least 1.");
    }

    private static string Normalize(string? value)
        => new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string ToCsv(IReadOnlyCollection<LiveModelDatasetRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', Header));
        foreach (LiveModelDatasetRow row in rows)
            sb.AppendLine(string.Join(',', row.ToValues().Select(EscapeCsv)));
        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static readonly string[] Header =
    [
        "LeagueName", "LeagueSlug", "SofaScoreUniqueTournamentId", "SofaScoreSeasonId", "SeasonName", "SeasonYear", "RoundNumber", "MatchId", "SofaScoreEventId", "StartTimeUtc",
        "HomeTeamSofaScoreId", "HomeTeamName", "AwayTeamSofaScoreId", "AwayTeamName",
        "Minute", "Phase", "HomeGoals", "AwayGoals", "TotalGoals", "GoalDifference", "AbsGoalDifference", "ScoreState", "LeadingTeam",
        "HomeRedCards", "AwayRedCards", "RedCardDifference", "AnyRedCard", "LastGoalMinute", "MinutesSinceLastGoal", "GoalsLast5Minutes", "GoalsLast10Minutes", "GoalsLast15Minutes", "HomeGoalsLast15Minutes", "AwayGoalsLast15Minutes",
        "HomeHistoryMatchesUsed", "HomeGoalsForPerMatch", "HomeGoalsAgainstPerMatch", "HomeExpectedGoalsForPerMatch", "HomeExpectedGoalsAgainstPerMatch", "HomeTotalShotsOnGoalForPerMatch", "HomeTotalShotsOnGoalAgainstPerMatch", "HomeShotsOnGoalForPerMatch", "HomeShotsOnGoalAgainstPerMatch", "HomeCornerKicksForPerMatch", "HomeCornerKicksAgainstPerMatch", "HomeBallPossessionForPerMatch", "HomeBallPossessionAgainstPerMatch", "HomeRedCardsForPerMatch", "HomeRedCardsAgainstPerMatch",
        "AwayHistoryMatchesUsed", "AwayGoalsForPerMatch", "AwayGoalsAgainstPerMatch", "AwayExpectedGoalsForPerMatch", "AwayExpectedGoalsAgainstPerMatch", "AwayTotalShotsOnGoalForPerMatch", "AwayTotalShotsOnGoalAgainstPerMatch", "AwayShotsOnGoalForPerMatch", "AwayShotsOnGoalAgainstPerMatch", "AwayCornerKicksForPerMatch", "AwayCornerKicksAgainstPerMatch", "AwayBallPossessionForPerMatch", "AwayBallPossessionAgainstPerMatch", "AwayRedCardsForPerMatch", "AwayRedCardsAgainstPerMatch",
        "HistoryGoalDiffPerMatch", "HistoryXgDiffPerMatch", "HistoryTotalShotsOnGoalDiffPerMatch", "HistoryShotsOnGoalDiffPerMatch", "HistoryCornerDiffPerMatch", "HistoryPossessionDiffPerMatch", "HistoryRedCardDiffPerMatch",
        "FinalHomeGoals", "FinalAwayGoals", "FinalTotalGoals", "RemainingHomeGoals", "RemainingAwayGoals", "RemainingTotalGoals", "AnyGoalAfterSnapshot", "IsReliableMatch"
    ];
}

internal sealed class TeamHistoricalMatch
{
    public DateTimeOffset StartTimeUtc { get; set; }
    public double? GoalsFor { get; set; }
    public double? GoalsAgainst { get; set; }
    public double? ExpectedGoalsFor { get; set; }
    public double? ExpectedGoalsAgainst { get; set; }
    public double? TotalShotsOnGoalFor { get; set; }
    public double? TotalShotsOnGoalAgainst { get; set; }
    public double? ShotsOnGoalFor { get; set; }
    public double? ShotsOnGoalAgainst { get; set; }
    public double? CornerKicksFor { get; set; }
    public double? CornerKicksAgainst { get; set; }
    public double? BallPossessionFor { get; set; }
    public double? BallPossessionAgainst { get; set; }
    public double? RedCardsFor { get; set; }
    public double? RedCardsAgainst { get; set; }
}

internal sealed class TeamHistoryFeatures
{
    public int MatchesUsed { get; set; }
    public double GoalsForPerMatch { get; set; }
    public double GoalsAgainstPerMatch { get; set; }
    public double ExpectedGoalsForPerMatch { get; set; }
    public double ExpectedGoalsAgainstPerMatch { get; set; }
    public double TotalShotsOnGoalForPerMatch { get; set; }
    public double TotalShotsOnGoalAgainstPerMatch { get; set; }
    public double ShotsOnGoalForPerMatch { get; set; }
    public double ShotsOnGoalAgainstPerMatch { get; set; }
    public double CornerKicksForPerMatch { get; set; }
    public double CornerKicksAgainstPerMatch { get; set; }
    public double BallPossessionForPerMatch { get; set; }
    public double BallPossessionAgainstPerMatch { get; set; }
    public double RedCardsForPerMatch { get; set; }
    public double RedCardsAgainstPerMatch { get; set; }
}

internal sealed class LiveModelDatasetRow
{
    public string LeagueName { get; set; } = string.Empty;
    public string LeagueSlug { get; set; } = string.Empty;
    public int SofaScoreUniqueTournamentId { get; set; }
    public int SofaScoreSeasonId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public string SeasonYear { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public int MatchId { get; set; }
    public long SofaScoreEventId { get; set; }
    public DateTimeOffset? StartTimeUtc { get; set; }
    public long HomeTeamSofaScoreId { get; set; }
    public string HomeTeamName { get; set; } = string.Empty;
    public long AwayTeamSofaScoreId { get; set; }
    public string AwayTeamName { get; set; } = string.Empty;
    public int Minute { get; set; }
    public string Phase { get; set; } = string.Empty;
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    public int TotalGoals { get; set; }
    public int GoalDifference { get; set; }
    public int AbsGoalDifference { get; set; }
    public string ScoreState { get; set; } = string.Empty;
    public string LeadingTeam { get; set; } = string.Empty;
    public int HomeRedCards { get; set; }
    public int AwayRedCards { get; set; }
    public int RedCardDifference { get; set; }
    public bool AnyRedCard { get; set; }
    public int? LastGoalMinute { get; set; }
    public int? MinutesSinceLastGoal { get; set; }
    public int GoalsLast5Minutes { get; set; }
    public int GoalsLast10Minutes { get; set; }
    public int GoalsLast15Minutes { get; set; }
    public int HomeGoalsLast15Minutes { get; set; }
    public int AwayGoalsLast15Minutes { get; set; }
    public TeamHistoryFeatures HomeHistory { get; set; } = new();
    public TeamHistoryFeatures AwayHistory { get; set; } = new();
    public double HistoryGoalDiffPerMatch { get; set; }
    public double HistoryXgDiffPerMatch { get; set; }
    public double HistoryTotalShotsOnGoalDiffPerMatch { get; set; }
    public double HistoryShotsOnGoalDiffPerMatch { get; set; }
    public double HistoryCornerDiffPerMatch { get; set; }
    public double HistoryPossessionDiffPerMatch { get; set; }
    public double HistoryRedCardDiffPerMatch { get; set; }
    public int FinalHomeGoals { get; set; }
    public int FinalAwayGoals { get; set; }
    public int FinalTotalGoals { get; set; }
    public int RemainingHomeGoals { get; set; }
    public int RemainingAwayGoals { get; set; }
    public int RemainingTotalGoals { get; set; }
    public bool AnyGoalAfterSnapshot { get; set; }
    public bool IsReliableMatch { get; set; }

    public string[] ToValues()
    {
        static string D(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        static string N(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        static string B(bool value) => value ? "1" : "0";

        return
        [
            LeagueName, LeagueSlug, SofaScoreUniqueTournamentId.ToString(CultureInfo.InvariantCulture), SofaScoreSeasonId.ToString(CultureInfo.InvariantCulture), SeasonName, SeasonYear, RoundNumber.ToString(CultureInfo.InvariantCulture), MatchId.ToString(CultureInfo.InvariantCulture), SofaScoreEventId.ToString(CultureInfo.InvariantCulture), StartTimeUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            HomeTeamSofaScoreId.ToString(CultureInfo.InvariantCulture), HomeTeamName, AwayTeamSofaScoreId.ToString(CultureInfo.InvariantCulture), AwayTeamName,
            Minute.ToString(CultureInfo.InvariantCulture), Phase, HomeGoals.ToString(CultureInfo.InvariantCulture), AwayGoals.ToString(CultureInfo.InvariantCulture), TotalGoals.ToString(CultureInfo.InvariantCulture), GoalDifference.ToString(CultureInfo.InvariantCulture), AbsGoalDifference.ToString(CultureInfo.InvariantCulture), ScoreState, LeadingTeam,
            HomeRedCards.ToString(CultureInfo.InvariantCulture), AwayRedCards.ToString(CultureInfo.InvariantCulture), RedCardDifference.ToString(CultureInfo.InvariantCulture), B(AnyRedCard), N(LastGoalMinute), N(MinutesSinceLastGoal), GoalsLast5Minutes.ToString(CultureInfo.InvariantCulture), GoalsLast10Minutes.ToString(CultureInfo.InvariantCulture), GoalsLast15Minutes.ToString(CultureInfo.InvariantCulture), HomeGoalsLast15Minutes.ToString(CultureInfo.InvariantCulture), AwayGoalsLast15Minutes.ToString(CultureInfo.InvariantCulture),
            HomeHistory.MatchesUsed.ToString(CultureInfo.InvariantCulture), D(HomeHistory.GoalsForPerMatch), D(HomeHistory.GoalsAgainstPerMatch), D(HomeHistory.ExpectedGoalsForPerMatch), D(HomeHistory.ExpectedGoalsAgainstPerMatch), D(HomeHistory.TotalShotsOnGoalForPerMatch), D(HomeHistory.TotalShotsOnGoalAgainstPerMatch), D(HomeHistory.ShotsOnGoalForPerMatch), D(HomeHistory.ShotsOnGoalAgainstPerMatch), D(HomeHistory.CornerKicksForPerMatch), D(HomeHistory.CornerKicksAgainstPerMatch), D(HomeHistory.BallPossessionForPerMatch), D(HomeHistory.BallPossessionAgainstPerMatch), D(HomeHistory.RedCardsForPerMatch), D(HomeHistory.RedCardsAgainstPerMatch),
            AwayHistory.MatchesUsed.ToString(CultureInfo.InvariantCulture), D(AwayHistory.GoalsForPerMatch), D(AwayHistory.GoalsAgainstPerMatch), D(AwayHistory.ExpectedGoalsForPerMatch), D(AwayHistory.ExpectedGoalsAgainstPerMatch), D(AwayHistory.TotalShotsOnGoalForPerMatch), D(AwayHistory.TotalShotsOnGoalAgainstPerMatch), D(AwayHistory.ShotsOnGoalForPerMatch), D(AwayHistory.ShotsOnGoalAgainstPerMatch), D(AwayHistory.CornerKicksForPerMatch), D(AwayHistory.CornerKicksAgainstPerMatch), D(AwayHistory.BallPossessionForPerMatch), D(AwayHistory.BallPossessionAgainstPerMatch), D(AwayHistory.RedCardsForPerMatch), D(AwayHistory.RedCardsAgainstPerMatch),
            D(HistoryGoalDiffPerMatch), D(HistoryXgDiffPerMatch), D(HistoryTotalShotsOnGoalDiffPerMatch), D(HistoryShotsOnGoalDiffPerMatch), D(HistoryCornerDiffPerMatch), D(HistoryPossessionDiffPerMatch), D(HistoryRedCardDiffPerMatch),
            FinalHomeGoals.ToString(CultureInfo.InvariantCulture), FinalAwayGoals.ToString(CultureInfo.InvariantCulture), FinalTotalGoals.ToString(CultureInfo.InvariantCulture), RemainingHomeGoals.ToString(CultureInfo.InvariantCulture), RemainingAwayGoals.ToString(CultureInfo.InvariantCulture), RemainingTotalGoals.ToString(CultureInfo.InvariantCulture), B(AnyGoalAfterSnapshot), B(IsReliableMatch)
        ];
    }
}
