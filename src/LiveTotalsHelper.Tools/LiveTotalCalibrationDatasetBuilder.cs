using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using LiveTotalsHelper.Modeling;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalCalibrationDatasetOptions
{
    public string League { get; set; } = string.Empty;
    public List<int> SeasonIds { get; } = [];
    public List<int> Rounds { get; } = [];
    public string ModelPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public double EmpiricalWeight { get; set; } = 0.80;
    public bool IncludeUnreliableMatches { get; set; }
    public int MaxExamples { get; set; } = 20;
    public List<int> SnapshotMinutes { get; } = [10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85];
}

public sealed class LiveTotalCalibrationDatasetResult
{
    public int MatchesChecked { get; set; }
    public int FinishedMatches { get; set; }
    public int ReliableFinishedMatches { get; set; }
    public int UnreliableFinishedMatches { get; set; }
    public int StatesWritten { get; set; }
    public List<int> SeasonsIncluded { get; } = [];
    public string OutputPath { get; set; } = string.Empty;
    public List<string> Warnings { get; } = [];
}

public sealed class LiveTotalCalibrationDatasetBuilder
{
    private readonly LiveTotalsDbContext _db;
    private readonly LiveTotalCalibrationDatasetOptions _options;

    public LiveTotalCalibrationDatasetBuilder(LiveTotalsDbContext db, LiveTotalCalibrationDatasetOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<LiveTotalCalibrationDatasetResult> BuildAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();
        WeibullModelFile model = await LoadModelAsync(_options.ModelPath, cancellationToken);

        var result = new LiveTotalCalibrationDatasetResult { OutputPath = _options.OutputPath };

        IQueryable<MatchEntity> query = _db.Matches.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(_options.League))
            query = query.Where(x => x.LeagueName == _options.League || x.LeagueSlug == _options.League);
        if (_options.SeasonIds.Count > 0)
            query = query.Where(x => _options.SeasonIds.Contains(x.SofaScoreSeasonId));
        if (_options.Rounds.Count > 0)
            query = query.Where(x => _options.Rounds.Contains(x.RoundNumber));

        List<MatchEntity> matches = await query
            .OrderBy(x => x.StartTimeUtc)
            .ThenBy(x => x.SofaScoreEventId)
            .ToListAsync(cancellationToken);

        result.MatchesChecked = matches.Count;
        result.SeasonsIncluded.AddRange(matches.Select(x => x.SofaScoreSeasonId).Distinct().OrderBy(x => x));

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

        var rows = new List<LiveTotalCalibrationDatasetRow>();
        var unreliableExamples = new List<string>();

        foreach (MatchEntity match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsFinished(match))
                continue;

            result.FinishedMatches++;
            List<MatchEventEntity> matchEvents = eventsByMatch.GetValueOrDefault(match.Id) ?? [];
            List<MatchEventEntity> goals = matchEvents.Where(x => x.IncidentType == "goal").ToList();
            int finalHome = match.HomeScoreCurrent ?? 0;
            int finalAway = match.AwayScoreCurrent ?? 0;
            bool reliable = finalHome == goals.Count(x => x.IsHome) && finalAway == goals.Count(x => !x.IsHome);

            if (reliable)
            {
                result.ReliableFinishedMatches++;
            }
            else
            {
                result.UnreliableFinishedMatches++;
                if (unreliableExamples.Count < _options.MaxExamples)
                    unreliableExamples.Add($"event {match.SofaScoreEventId}: final {finalHome}-{finalAway}, goal events {goals.Count(x => x.IsHome)}-{goals.Count(x => !x.IsHome)}");
                if (!_options.IncludeUnreliableMatches)
                    continue;
            }

            int finalTotal = finalHome + finalAway;

            foreach (int minute in _options.SnapshotMinutes.Distinct().OrderBy(x => x))
            {
                List<MatchEventEntity> goalsBeforeMinute = goals.Where(x => GoalMinuteForModel(x) < minute).ToList();
                int homeGoals = goalsBeforeMinute.Count(x => x.IsHome);
                int awayGoals = goalsBeforeMinute.Count(x => !x.IsHome);
                int currentTotal = homeGoals + awayGoals;
                int homeRedCards = matchEvents.Count(x => IsRedCard(x) && x.IsHome && EventMinuteForModel(x) < minute);
                int awayRedCards = matchEvents.Count(x => IsRedCard(x) && !x.IsHome && EventMinuteForModel(x) < minute);
                int lastGoalMinute = goalsBeforeMinute.Count == 0 ? -1 : goalsBeforeMinute.Max(GoalMinuteForModel);

                string scoreState = ScoreStateResolver.FromScore(homeGoals, awayGoals);
                TimingModelSource source = ResolveTimingModel(model, scoreState);
                TimingBlendResult timing = TimingShareCalculator.Calculate(new TimingBlendInput
                {
                    Minute = Math.Clamp(minute, 0, model.MaxMinute > 0 ? model.MaxMinute : 90),
                    ShapeK = source.ShapeK,
                    ScaleLambda = source.ScaleLambda,
                    CdfAtMaxMinute = source.CdfAtMaxMinute,
                    EmpiricalBuckets = MapBuckets(source.EmpiricalBuckets),
                    EmpiricalWeight = _options.EmpiricalWeight
                });

                rows.Add(new LiveTotalCalibrationDatasetRow
                {
                    LeagueName = match.LeagueName,
                    LeagueSlug = match.LeagueSlug,
                    SofaScoreSeasonId = match.SofaScoreSeasonId,
                    SeasonName = match.SeasonName,
                    SeasonYear = match.SeasonYear,
                    RoundNumber = match.RoundNumber,
                    MatchId = match.Id,
                    SofaScoreEventId = match.SofaScoreEventId,
                    StartTimeUtc = match.StartTimeUtc,
                    HomeTeamName = match.HomeTeamName,
                    AwayTeamName = match.AwayTeamName,
                    Minute = minute,
                    HomeGoals = homeGoals,
                    AwayGoals = awayGoals,
                    CurrentTotalGoals = currentTotal,
                    GoalDifference = homeGoals - awayGoals,
                    ScoreState = scoreState,
                    DetailedScoreState = DetailedScoreState(homeGoals, awayGoals),
                    HomeRedCards = homeRedCards,
                    AwayRedCards = awayRedCards,
                    LastGoalMinute = lastGoalMinute,
                    HasRecentGoal = lastGoalMinute >= 0 && minute >= lastGoalMinute && minute - lastGoalMinute <= 2,
                    SelectedTimingGroup = source.GroupName,
                    TimingFallback = source.FallbackReason,
                    EmpiricalWeight = timing.EmpiricalWeight,
                    WeibullRemainingShare = timing.WeibullRemainingShare,
                    EmpiricalRemainingShare = timing.EmpiricalRemainingShare,
                    TimingRemainingShare = timing.BlendedRemainingShare,
                    ActualFinalHomeGoals = finalHome,
                    ActualFinalAwayGoals = finalAway,
                    ActualFinalTotalGoals = finalTotal,
                    ActualRemainingGoals = finalTotal - currentTotal,
                    AnyFutureGoal = finalTotal > currentTotal,
                    IsReliableMatch = reliable
                });
            }
        }

        if (unreliableExamples.Count > 0)
            result.Warnings.Add($"Unreliable matches skipped: {result.UnreliableFinishedMatches}. Examples: {string.Join("; ", unreliableExamples)}");

        string outputPath = ResolveOutputPath();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
        await File.WriteAllTextAsync(outputPath, ToCsv(rows), Encoding.UTF8, cancellationToken);
        result.OutputPath = outputPath;
        result.StatesWritten = rows.Count;
        return result;
    }

    private static async Task<WeibullModelFile> LoadModelAsync(string modelPath, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(modelPath);
        return await JsonSerializer.DeserializeAsync<WeibullModelFile>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, cancellationToken) ?? throw new InvalidOperationException("Could not read timing model JSON.");
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ModelPath))
            throw new ArgumentException("Missing required argument --model.");
        if (!File.Exists(_options.ModelPath))
            throw new FileNotFoundException("Timing model JSON was not found.", _options.ModelPath);
        if (_options.EmpiricalWeight < 0.0 || _options.EmpiricalWeight > 1.0)
            throw new ArgumentException("--empirical-weight must be between 0 and 1.");
        if (_options.SnapshotMinutes.Count == 0)
            throw new ArgumentException("At least one snapshot minute is required.");
        if (_options.SnapshotMinutes.Any(x => x < 0 || x > 90))
            throw new ArgumentException("Snapshot minutes must be between 0 and 90.");
    }

    private string ResolveOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
            return _options.OutputPath;

        string safeLeague = string.IsNullOrWhiteSpace(_options.League)
            ? "all"
            : new string(_options.League.Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-').ToArray()).Trim('-');
        string seasons = _options.SeasonIds.Count == 0 ? "all-seasons" : string.Join('-', _options.SeasonIds.OrderBy(x => x));
        return Path.Combine("data", "datasets", $"{safeLeague}-{seasons}-live-total-calibration.csv");
    }

    private static bool IsFinished(MatchEntity match) =>
        match.StatusType.Equals("finished", StringComparison.OrdinalIgnoreCase) ||
        match.StatusDescription.Equals("Ended", StringComparison.OrdinalIgnoreCase) ||
        match.StatusDescription.Equals("Finished", StringComparison.OrdinalIgnoreCase);

    private static int GoalMinuteForModel(MatchEventEntity e) => EventMinuteForModel(e);

    private static int EventMinuteForModel(MatchEventEntity e)
    {
        int minute = Math.Max(0, e.Minute);
        if (minute >= 90) return 90;
        if (minute >= 45 && e.AddedTime is > 0) return 45;
        return Math.Min(90, minute);
    }

    private static bool IsRedCard(MatchEventEntity e) =>
        e.IncidentType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
        (e.IncidentClass?.Contains("red", StringComparison.OrdinalIgnoreCase) == true);

    private static string DetailedScoreState(int homeGoals, int awayGoals)
    {
        if (homeGoals == 0 && awayGoals == 0) return "NilNil";
        if (homeGoals == awayGoals) return "LevelWithGoals";
        int margin = Math.Abs(homeGoals - awayGoals);
        return margin switch
        {
            1 => "OneGoalMargin",
            2 => "TwoGoalMargin",
            _ => "ThreePlusGoalMargin"
        };
    }

    private static TimingModelSource ResolveTimingModel(WeibullModelFile model, string scoreState)
    {
        TimingModelGroupResult? group = model.Groups.FirstOrDefault(g => g.GroupName.Equals(scoreState, StringComparison.OrdinalIgnoreCase));
        if (group is not null)
        {
            return new TimingModelSource
            {
                GroupName = group.GroupName,
                ShapeK = group.ShapeK,
                ScaleLambda = group.ScaleLambda,
                CdfAtMaxMinute = group.CdfAtMaxMinute,
                EmpiricalBuckets = group.EmpiricalBuckets
            };
        }

        string fallback = model.Groups.Count > 0
            ? $"Timing group '{scoreState}' was not found; falling back to All/root model."
            : string.Empty;

        return new TimingModelSource
        {
            GroupName = "All",
            FallbackReason = fallback,
            ShapeK = model.Weibull.ShapeK,
            ScaleLambda = model.Weibull.ScaleLambda,
            CdfAtMaxMinute = model.Weibull.CdfAtMaxMinute,
            EmpiricalBuckets = model.Empirical.Buckets
        };
    }

    private static List<EmpiricalTimingBucketModel> MapBuckets(IEnumerable<EmpiricalTimingBucket> buckets)
    {
        return buckets.Select(x => new EmpiricalTimingBucketModel
        {
            FromMinuteExclusive = x.FromMinuteExclusive,
            ToMinuteInclusive = x.ToMinuteInclusive,
            Label = x.Label,
            GoalCount = x.GoalCount,
            GoalShare = x.GoalShare,
            CumulativeShareBefore = x.CumulativeShareBefore,
            CumulativeShareAfter = x.CumulativeShareAfter
        }).ToList();
    }

    private static string ToCsv(IReadOnlyCollection<LiveTotalCalibrationDatasetRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', Header));
        foreach (LiveTotalCalibrationDatasetRow row in rows)
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
        "LeagueName", "LeagueSlug", "SofaScoreSeasonId", "SeasonName", "SeasonYear", "RoundNumber", "MatchId", "SofaScoreEventId", "StartTimeUtc",
        "HomeTeamName", "AwayTeamName",
        "Minute", "HomeGoals", "AwayGoals", "CurrentTotalGoals", "GoalDifference", "ScoreState", "DetailedScoreState",
        "HomeRedCards", "AwayRedCards", "LastGoalMinute", "HasRecentGoal",
        "SelectedTimingGroup", "TimingFallback", "EmpiricalWeight", "WeibullRemainingShare", "EmpiricalRemainingShare", "TimingRemainingShare",
        "ActualFinalHomeGoals", "ActualFinalAwayGoals", "ActualFinalTotalGoals", "ActualRemainingGoals", "AnyFutureGoal", "IsReliableMatch"
    ];
}

internal sealed class LiveTotalCalibrationDatasetRow
{
    public string LeagueName { get; set; } = string.Empty;
    public string LeagueSlug { get; set; } = string.Empty;
    public int SofaScoreSeasonId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public string SeasonYear { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public int MatchId { get; set; }
    public long SofaScoreEventId { get; set; }
    public DateTimeOffset? StartTimeUtc { get; set; }
    public string HomeTeamName { get; set; } = string.Empty;
    public string AwayTeamName { get; set; } = string.Empty;
    public int Minute { get; set; }
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    public int CurrentTotalGoals { get; set; }
    public int GoalDifference { get; set; }
    public string ScoreState { get; set; } = string.Empty;
    public string DetailedScoreState { get; set; } = string.Empty;
    public int HomeRedCards { get; set; }
    public int AwayRedCards { get; set; }
    public int LastGoalMinute { get; set; }
    public bool HasRecentGoal { get; set; }
    public string SelectedTimingGroup { get; set; } = string.Empty;
    public string TimingFallback { get; set; } = string.Empty;
    public double EmpiricalWeight { get; set; }
    public double WeibullRemainingShare { get; set; }
    public double EmpiricalRemainingShare { get; set; }
    public double TimingRemainingShare { get; set; }
    public int ActualFinalHomeGoals { get; set; }
    public int ActualFinalAwayGoals { get; set; }
    public int ActualFinalTotalGoals { get; set; }
    public int ActualRemainingGoals { get; set; }
    public bool AnyFutureGoal { get; set; }
    public bool IsReliableMatch { get; set; }

    public string[] ToValues()
    {
        static string D(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        static string B(bool value) => value ? "1" : "0";

        return
        [
            LeagueName, LeagueSlug, SofaScoreSeasonId.ToString(CultureInfo.InvariantCulture), SeasonName, SeasonYear, RoundNumber.ToString(CultureInfo.InvariantCulture), MatchId.ToString(CultureInfo.InvariantCulture), SofaScoreEventId.ToString(CultureInfo.InvariantCulture), StartTimeUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            HomeTeamName, AwayTeamName,
            Minute.ToString(CultureInfo.InvariantCulture), HomeGoals.ToString(CultureInfo.InvariantCulture), AwayGoals.ToString(CultureInfo.InvariantCulture), CurrentTotalGoals.ToString(CultureInfo.InvariantCulture), GoalDifference.ToString(CultureInfo.InvariantCulture), ScoreState, DetailedScoreState,
            HomeRedCards.ToString(CultureInfo.InvariantCulture), AwayRedCards.ToString(CultureInfo.InvariantCulture), LastGoalMinute.ToString(CultureInfo.InvariantCulture), B(HasRecentGoal),
            SelectedTimingGroup, TimingFallback, D(EmpiricalWeight), D(WeibullRemainingShare), D(EmpiricalRemainingShare), D(TimingRemainingShare),
            ActualFinalHomeGoals.ToString(CultureInfo.InvariantCulture), ActualFinalAwayGoals.ToString(CultureInfo.InvariantCulture), ActualFinalTotalGoals.ToString(CultureInfo.InvariantCulture), ActualRemainingGoals.ToString(CultureInfo.InvariantCulture), B(AnyFutureGoal), B(IsReliableMatch)
        ];
    }
}
