using System.Globalization;
using System.Text;
using LiveTotalsHelper.Core.MonteCarlo;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Tools;

public sealed class StateWeibullExposureBuilderOptions
{
    public string League { get; init; } = string.Empty;
    public IReadOnlyList<string> Seasons { get; init; } = [];
    public IReadOnlyList<StateWeibullTimeBucket> TimeBuckets { get; init; } = StateWeibullTimeBucket.DefaultBuckets();
    public string OutputPath { get; init; } = "outputs/calibration/state-weibull-exposures.csv";
    public double DefaultFinalMinute { get; init; } = 96.0;
    public double MinimumInstantGoalIntervalMinutes { get; init; } = 0.01;
}

public sealed class StateWeibullExposureBuildResult
{
    public int MatchesLoaded { get; set; }
    public int MatchesUsed { get; set; }
    public int MatchesSkippedMissingScore { get; set; }
    public int MatchesSkippedInvalidTimeline { get; set; }
    public int ExposureRowsWritten { get; set; }
    public int GoalRowsWritten { get; set; }
    public List<string> Warnings { get; } = [];
}

public sealed class StateWeibullExposureRow
{
    public int MatchId { get; init; }
    public string EventId { get; init; } = string.Empty;
    public string League { get; init; } = string.Empty;
    public string LeagueSlug { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public int SeasonId { get; init; }
    public int RoundNumber { get; init; }
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string FinalScore { get; init; } = string.Empty;
    public int Sequence { get; init; }
    public string TimeBucket { get; init; } = string.Empty;
    public double BucketStartMinute { get; init; }
    public double BucketEndMinute { get; init; }
    public string ScoreBucket { get; init; } = string.Empty;
    public string ExactScore { get; init; } = string.Empty;
    public int HomeGoalsAtStart { get; init; }
    public int AwayGoalsAtStart { get; init; }
    public double StartMinute { get; init; }
    public double EndMinute { get; init; }
    public double ExposureMinutes { get; init; }
    public bool GoalHappened { get; init; }
    public double? GoalMinute { get; init; }
    public string GoalSide { get; init; } = string.Empty;
    public int? GoalHomeScore { get; init; }
    public int? GoalAwayScore { get; init; }
}

public sealed class StateWeibullExposureBuilder
{
    private const double Epsilon = 0.000001;
    private readonly LiveTotalsDbContext _db;

    public StateWeibullExposureBuilder(LiveTotalsDbContext db)
    {
        _db = db;
    }

    public async Task<StateWeibullExposureBuildResult> BuildAsync(
        StateWeibullExposureBuilderOptions options,
        CancellationToken cancellationToken)
    {
        if (options.TimeBuckets.Count == 0)
            throw new ArgumentException("At least one time bucket is required.", nameof(options));

        var result = new StateWeibullExposureBuildResult();
        List<StateWeibullExposureRow> rows = [];

        IQueryable<MatchEntity> query = _db.Matches
            .AsNoTracking()
            .Include(x => x.Events);

        if (!string.IsNullOrWhiteSpace(options.League))
        {
            string league = options.League.Trim();
            query = query.Where(x =>
                x.LeagueName == league ||
                x.LeagueSlug == league ||
                x.EventId == league);
        }

        if (options.Seasons.Count > 0)
        {
            string[] seasonStrings = options.Seasons.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            int[] seasonIds = seasonStrings
                .Where(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .Select(x => int.Parse(x, CultureInfo.InvariantCulture))
                .ToArray();

            query = query.Where(x =>
                seasonStrings.Contains(x.SeasonYear) ||
                seasonStrings.Contains(x.SeasonName) ||
                seasonIds.Contains(x.SeasonId));
        }

        List<MatchEntity> matches = await query
            .OrderBy(x => x.LeagueSlug)
            .ThenBy(x => x.SeasonYear)
            .ThenBy(x => x.RoundNumber)
            .ThenBy(x => x.StartTimeUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        result.MatchesLoaded = matches.Count;

        foreach (MatchEntity match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<StateWeibullExposureRow> matchRows = BuildRowsForMatch(match, options, result);
            if (matchRows.Count == 0)
                continue;

            result.MatchesUsed++;
            rows.AddRange(matchRows);
        }

        result.ExposureRowsWritten = rows.Count;
        result.GoalRowsWritten = rows.Count(x => x.GoalHappened);

        await WriteCsvAsync(rows, options.OutputPath, cancellationToken);
        return result;
    }

    private static List<StateWeibullExposureRow> BuildRowsForMatch(
        MatchEntity match,
        StateWeibullExposureBuilderOptions options,
        StateWeibullExposureBuildResult result)
    {
        if (!match.HomeScoreCurrent.HasValue || !match.AwayScoreCurrent.HasValue)
        {
            result.MatchesSkippedMissingScore++;
            return [];
        }

        int finalHomeGoals = match.HomeScoreCurrent.Value;
        int finalAwayGoals = match.AwayScoreCurrent.Value;
        if (finalHomeGoals < 0 || finalAwayGoals < 0)
        {
            result.MatchesSkippedMissingScore++;
            return [];
        }

        List<GoalSnapshot> rawGoals = match.Events
            .Where(IsScoringGoal)
            .Select(ToGoalSnapshot)
            .OrderBy(x => x.Minute)
            .ThenBy(x => x.HomeScore + x.AwayScore)
            .ThenBy(x => x.EventRowId)
            .ToList();

        if (!TryValidateGoalTimeline(rawGoals, finalHomeGoals, finalAwayGoals, out List<GoalSnapshot> goals, out string invalidReason))
        {
            result.MatchesSkippedInvalidTimeline++;
            if (result.Warnings.Count < 25)
                result.Warnings.Add($"Skipped match {match.Id} {match.HomeTeamName} vs {match.AwayTeamName}: {invalidReason}");
            return [];
        }

        double lastKnownGoalMinute = goals.Count == 0 ? 0.0 : goals.Max(x => x.Minute);
        double finalMinute = Math.Max(options.DefaultFinalMinute, lastKnownGoalMinute);
        double maxBucketEnd = options.TimeBuckets.Max(x => x.EndMinute);
        finalMinute = Math.Min(finalMinute, maxBucketEnd);

        int currentHomeGoals = 0;
        int currentAwayGoals = 0;
        double currentMinute = 0.0;
        int sequence = 1;
        var rows = new List<StateWeibullExposureRow>();

        foreach (GoalSnapshot goal in goals)
        {
            double goalMinute = goal.Minute;
            if (goalMinute <= currentMinute + Epsilon)
                goalMinute = currentMinute + Math.Max(options.MinimumInstantGoalIntervalMinutes, 0.001);

            GoalSnapshot intervalGoal = goal with { Minute = goalMinute };
            bool goalInsideConfiguredBuckets = goalMinute <= maxBucketEnd + Epsilon;

            AddRowsForInterval(
                rows,
                match,
                finalHomeGoals,
                finalAwayGoals,
                currentHomeGoals,
                currentAwayGoals,
                currentMinute,
                Math.Min(goalMinute, maxBucketEnd),
                goalInsideConfiguredBuckets ? intervalGoal : null,
                options.TimeBuckets,
                ref sequence);

            currentHomeGoals = goal.HomeScore;
            currentAwayGoals = goal.AwayScore;
            currentMinute = goalMinute;
        }

        if (currentMinute < finalMinute - Epsilon)
        {
            AddRowsForInterval(
                rows,
                match,
                finalHomeGoals,
                finalAwayGoals,
                currentHomeGoals,
                currentAwayGoals,
                currentMinute,
                finalMinute,
                goal: null,
                options.TimeBuckets,
                ref sequence);
        }

        return rows;
    }

    private static void AddRowsForInterval(
        List<StateWeibullExposureRow> rows,
        MatchEntity match,
        int finalHomeGoals,
        int finalAwayGoals,
        int homeGoalsAtStart,
        int awayGoalsAtStart,
        double startMinute,
        double endMinute,
        GoalSnapshot? goal,
        IReadOnlyList<StateWeibullTimeBucket> timeBuckets,
        ref int sequence)
    {
        if (endMinute <= startMinute + Epsilon)
            return;

        foreach (StateWeibullTimeBucket bucket in timeBuckets)
        {
            if (!bucket.Overlaps(startMinute, endMinute))
                continue;

            double segmentStart = Math.Max(startMinute, bucket.StartMinute);
            double segmentEnd = Math.Min(endMinute, bucket.EndMinute);
            if (segmentEnd <= segmentStart + Epsilon)
                continue;

            bool goalHappened = goal is not null && Math.Abs(segmentEnd - Math.Min(goal.Minute, timeBuckets.Max(x => x.EndMinute))) < 0.0001;
            string goalSide = string.Empty;
            if (goalHappened && goal is not null)
            {
                if (goal.HomeScore > homeGoalsAtStart)
                    goalSide = "home";
                else if (goal.AwayScore > awayGoalsAtStart)
                    goalSide = "away";
            }

            rows.Add(new StateWeibullExposureRow
            {
                MatchId = match.Id,
                EventId = match.EventId,
                League = match.LeagueName,
                LeagueSlug = match.LeagueSlug,
                Season = string.IsNullOrWhiteSpace(match.SeasonYear) ? match.SeasonName : match.SeasonYear,
                SeasonId = match.SeasonId,
                RoundNumber = match.RoundNumber,
                HomeTeam = match.HomeTeamName,
                AwayTeam = match.AwayTeamName,
                FinalScore = $"{finalHomeGoals}-{finalAwayGoals}",
                Sequence = sequence++,
                TimeBucket = bucket.Key,
                BucketStartMinute = bucket.StartMinute,
                BucketEndMinute = bucket.EndMinute,
                ScoreBucket = StateWeibullScoreBucketer.ResolveScoreBucket(homeGoalsAtStart, awayGoalsAtStart),
                ExactScore = StateWeibullScoreBucketer.ResolveExactScore(homeGoalsAtStart, awayGoalsAtStart),
                HomeGoalsAtStart = homeGoalsAtStart,
                AwayGoalsAtStart = awayGoalsAtStart,
                StartMinute = segmentStart,
                EndMinute = segmentEnd,
                ExposureMinutes = segmentEnd - segmentStart,
                GoalHappened = goalHappened,
                GoalMinute = goalHappened ? segmentEnd : null,
                GoalSide = goalHappened ? goalSide : string.Empty,
                GoalHomeScore = goalHappened ? goal?.HomeScore : null,
                GoalAwayScore = goalHappened ? goal?.AwayScore : null
            });
        }
    }

    private static bool TryValidateGoalTimeline(
        List<GoalSnapshot> rawGoals,
        int finalHomeGoals,
        int finalAwayGoals,
        out List<GoalSnapshot> goals,
        out string invalidReason)
    {
        goals = [];
        invalidReason = string.Empty;

        int currentHome = 0;
        int currentAway = 0;
        int currentTotal = 0;

        foreach (GoalSnapshot goal in rawGoals)
        {
            int goalTotal = goal.HomeScore + goal.AwayScore;
            bool scoresNonDecreasing = goal.HomeScore >= currentHome && goal.AwayScore >= currentAway;
            bool exactlyOneGoalAdded = goalTotal == currentTotal + 1;

            if (!scoresNonDecreasing || !exactlyOneGoalAdded)
            {
                invalidReason = $"invalid goal score sequence at {goal.Minute.ToString("0.##", CultureInfo.InvariantCulture)}': previous {currentHome}-{currentAway}, next {goal.HomeScore}-{goal.AwayScore}";
                return false;
            }

            goals.Add(goal);
            currentHome = goal.HomeScore;
            currentAway = goal.AwayScore;
            currentTotal = goalTotal;
        }

        if (currentHome != finalHomeGoals || currentAway != finalAwayGoals)
        {
            invalidReason = $"goal timeline ends {currentHome}-{currentAway}, final score is {finalHomeGoals}-{finalAwayGoals}";
            return false;
        }

        return true;
    }

    private static bool IsScoringGoal(MatchEventEntity matchEvent)
        => matchEvent.IncidentType.Equals("goal", StringComparison.OrdinalIgnoreCase)
           && matchEvent.HomeScore.HasValue
           && matchEvent.AwayScore.HasValue;

    private static GoalSnapshot ToGoalSnapshot(MatchEventEntity matchEvent)
        => new(
            matchEvent.Id,
            EffectiveMinute(matchEvent),
            matchEvent.HomeScore!.Value,
            matchEvent.AwayScore!.Value,
            matchEvent.IsHome);

    private static double EffectiveMinute(MatchEventEntity matchEvent)
    {
        if (matchEvent.TimeSeconds.HasValue && matchEvent.TimeSeconds.Value > 0)
            return Math.Round(matchEvent.TimeSeconds.Value / 60.0, 4);

        return matchEvent.Minute + Math.Max(0, matchEvent.AddedTime ?? 0);
    }

    private static async Task WriteCsvAsync(
        IReadOnlyList<StateWeibullExposureRow> rows,
        string outputPath,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        builder.AppendLine("match_id,event_id,league,league_slug,season,season_id,round,home_team,away_team,final_score,sequence,time_bucket,bucket_start_minute,bucket_end_minute,score_bucket,exact_score,home_goals_at_start,away_goals_at_start,start_minute,end_minute,exposure_minutes,goal_happened,goal_minute,goal_side,goal_home_score,goal_away_score");

        foreach (StateWeibullExposureRow row in rows)
        {
            builder.Append(row.MatchId.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(Csv(row.EventId)); builder.Append(',');
            builder.Append(Csv(row.League)); builder.Append(',');
            builder.Append(Csv(row.LeagueSlug)); builder.Append(',');
            builder.Append(Csv(row.Season)); builder.Append(',');
            builder.Append(row.SeasonId.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(row.RoundNumber.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(Csv(row.HomeTeam)); builder.Append(',');
            builder.Append(Csv(row.AwayTeam)); builder.Append(',');
            builder.Append(Csv(row.FinalScore)); builder.Append(',');
            builder.Append(row.Sequence.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(Csv(row.TimeBucket)); builder.Append(',');
            builder.Append(FormatDouble(row.BucketStartMinute)); builder.Append(',');
            builder.Append(FormatDouble(row.BucketEndMinute)); builder.Append(',');
            builder.Append(Csv(row.ScoreBucket)); builder.Append(',');
            builder.Append(Csv(row.ExactScore)); builder.Append(',');
            builder.Append(row.HomeGoalsAtStart.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(row.AwayGoalsAtStart.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(FormatDouble(row.StartMinute)); builder.Append(',');
            builder.Append(FormatDouble(row.EndMinute)); builder.Append(',');
            builder.Append(FormatDouble(row.ExposureMinutes)); builder.Append(',');
            builder.Append(row.GoalHappened ? "1" : "0"); builder.Append(',');
            builder.Append(row.GoalMinute.HasValue ? FormatDouble(row.GoalMinute.Value) : string.Empty); builder.Append(',');
            builder.Append(Csv(row.GoalSide)); builder.Append(',');
            builder.Append(row.GoalHomeScore?.ToString(CultureInfo.InvariantCulture) ?? string.Empty); builder.Append(',');
            builder.Append(row.GoalAwayScore?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(fullPath, builder.ToString(), Encoding.UTF8, cancellationToken);
    }

    private static string FormatDouble(double value)
        => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Csv(string value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private sealed record GoalSnapshot(
        int EventRowId,
        double Minute,
        int HomeScore,
        int AwayScore,
        bool IsHome);
}
