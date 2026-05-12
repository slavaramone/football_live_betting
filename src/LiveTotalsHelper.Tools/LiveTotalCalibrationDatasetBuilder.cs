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
    public bool IncludeEventTriggers { get; set; } = true;
    public int MaxExamples { get; set; } = 20;
    public List<int> SnapshotMinutes { get; } = [10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85];
}

public sealed class LiveTotalCalibrationDatasetResult
{
    public int MatchesChecked { get; set; }
    public int FinishedMatches { get; set; }
    public int ReliableFinishedMatches { get; set; }
    public int UnreliableFinishedMatches { get; set; }
    public int FixedMinuteStatesWritten { get; set; }
    public int AfterGoalStatesWritten { get; set; }
    public int AfterRedCardStatesWritten { get; set; }
    public int StatesWritten => FixedMinuteStatesWritten + AfterGoalStatesWritten + AfterRedCardStatesWritten;
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
            List<MatchEventEntity> orderedEvents = matchEvents
                .OrderBy(x => x.TimeSeconds ?? (x.Minute * 60))
                .ThenBy(x => x.Id)
                .ToList();
            List<MatchEventEntity> goals = orderedEvents.Where(IsGoal).ToList();

            int finalHome = match.HomeScoreCurrent ?? 0;
            int finalAway = match.AwayScoreCurrent ?? 0;
            int finalTotal = finalHome + finalAway;
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

            foreach (int minute in _options.SnapshotMinutes.Distinct().OrderBy(x => x))
            {
                List<MatchEventEntity> eventsBeforeMinute = orderedEvents.Where(x => EventMinuteForModel(x) < minute).ToList();
                MatchState state = BuildState(eventsBeforeMinute);
                rows.Add(CreateRow(
                    match,
                    model,
                    finalHome,
                    finalAway,
                    finalTotal,
                    reliable,
                    minute,
                    LiveTotalStateTrigger.FixedMinute,
                    triggerEventMinute: -1,
                    triggerEventSide: string.Empty,
                    state));
                result.FixedMinuteStatesWritten++;
            }

            if (_options.IncludeEventTriggers)
            {
                var state = new MatchState();

                foreach (MatchEventEntity e in orderedEvents)
                {
                    int minute = EventMinuteForModel(e);

                    if (IsGoal(e))
                    {
                        if (e.IsHome)
                            state.HomeGoals++;
                        else
                            state.AwayGoals++;

                        state.LastGoalMinute = minute;

                        rows.Add(CreateRow(
                            match,
                            model,
                            finalHome,
                            finalAway,
                            finalTotal,
                            reliable,
                            minute,
                            LiveTotalStateTrigger.AfterGoal,
                            minute,
                            e.IsHome ? "Home" : "Away",
                            state.Clone()));
                        result.AfterGoalStatesWritten++;
                    }
                    else if (IsRedCard(e))
                    {
                        if (e.IsHome)
                            state.HomeRedCards++;
                        else
                            state.AwayRedCards++;

                        rows.Add(CreateRow(
                            match,
                            model,
                            finalHome,
                            finalAway,
                            finalTotal,
                            reliable,
                            minute,
                            LiveTotalStateTrigger.AfterRedCard,
                            minute,
                            e.IsHome ? "Home" : "Away",
                            state.Clone()));
                        result.AfterRedCardStatesWritten++;
                    }
                }
            }

        }

        if (unreliableExamples.Count > 0)
            result.Warnings.Add($"Unreliable matches skipped: {result.UnreliableFinishedMatches}. Examples: {string.Join("; ", unreliableExamples)}");

        string outputPath = ResolveOutputPath();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
        await File.WriteAllTextAsync(outputPath, ToCsv(rows), Encoding.UTF8, cancellationToken);
        result.OutputPath = outputPath;
        return result;
    }

    private LiveTotalCalibrationDatasetRow CreateRow(
        MatchEntity match,
        WeibullModelFile model,
        int finalHome,
        int finalAway,
        int finalTotal,
        bool reliable,
        int minute,
        string stateTrigger,
        int triggerEventMinute,
        string triggerEventSide,
        MatchState state)
    {
        int currentTotal = state.HomeGoals + state.AwayGoals;
        int minutesSinceLastGoal = state.LastGoalMinute < 0 ? -1 : Math.Max(0, minute - state.LastGoalMinute);
        LiveTotalTimingEvaluation timing = LiveTotalTimingEvaluator.Evaluate(
            model,
            minute,
            state.HomeGoals,
            state.AwayGoals,
            _options.EmpiricalWeight);

        return new LiveTotalCalibrationDatasetRow
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
            StateTrigger = stateTrigger,
            TriggerEventMinute = triggerEventMinute,
            TriggerEventSide = triggerEventSide,
            Minute = minute,
            HomeGoals = state.HomeGoals,
            AwayGoals = state.AwayGoals,
            CurrentTotalGoals = currentTotal,
            GoalDifference = state.HomeGoals - state.AwayGoals,
            ScoreState = timing.ScoreState,
            DetailedScoreState = LiveTotalStateCorrectionResolver.DetailedScoreState(state.HomeGoals, state.AwayGoals),
            HomeRedCards = state.HomeRedCards,
            AwayRedCards = state.AwayRedCards,
            RedCardDifference = state.HomeRedCards - state.AwayRedCards,
            LastGoalMinute = state.LastGoalMinute,
            MinutesSinceLastGoal = minutesSinceLastGoal,
            HasRecentGoal = state.LastGoalMinute >= 0 && minutesSinceLastGoal <= 2,
            SelectedTimingGroup = timing.SelectedTimingGroup,
            TimingFallback = timing.TimingFallback,
            EmpiricalWeight = timing.EmpiricalWeight,
            WeibullRemainingShare = timing.WeibullRemainingShare,
            EmpiricalRemainingShare = timing.EmpiricalRemainingShare,
            TimingRemainingShare = timing.TimingRemainingShare,
            ActualFinalHomeGoals = finalHome,
            ActualFinalAwayGoals = finalAway,
            ActualFinalTotalGoals = finalTotal,
            ActualRemainingGoals = finalTotal - currentTotal,
            AnyFutureGoal = finalTotal > currentTotal,
            IsReliableMatch = reliable
        };
    }

    private static MatchState BuildState(IEnumerable<MatchEventEntity> events)
    {
        var state = new MatchState();
        foreach (MatchEventEntity e in events)
        {
            int minute = EventMinuteForModel(e);
            if (IsGoal(e))
            {
                if (e.IsHome)
                    state.HomeGoals++;
                else
                    state.AwayGoals++;
                state.LastGoalMinute = minute;
            }
            else if (IsRedCard(e))
            {
                if (e.IsHome)
                    state.HomeRedCards++;
                else
                    state.AwayRedCards++;
            }
        }
        return state;
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

    private static bool IsGoal(MatchEventEntity e) =>
        e.IncidentType.Equals("goal", StringComparison.OrdinalIgnoreCase);

    private static int EventMinuteForModel(MatchEventEntity e)
    {
        int minute = Math.Max(0, e.Minute);
        if (minute >= 90) return 90;
        if (minute >= 45 && e.AddedTime is > 0) return 45;
        return Math.Min(90, minute);
    }

    private static bool IsRedCard(MatchEventEntity e) =>
        e.IncidentType.Equals("card", StringComparison.OrdinalIgnoreCase) &&
        e.IncidentClass.Contains("red", StringComparison.OrdinalIgnoreCase);

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
        "StateTrigger", "TriggerEventMinute", "TriggerEventSide",
        "Minute", "HomeGoals", "AwayGoals", "CurrentTotalGoals", "GoalDifference", "ScoreState", "DetailedScoreState",
        "HomeRedCards", "AwayRedCards", "RedCardDifference", "LastGoalMinute", "MinutesSinceLastGoal", "HasRecentGoal",
        "SelectedTimingGroup", "TimingFallback", "EmpiricalWeight", "WeibullRemainingShare", "EmpiricalRemainingShare", "TimingRemainingShare",
        "ActualFinalHomeGoals", "ActualFinalAwayGoals", "ActualFinalTotalGoals", "ActualRemainingGoals", "AnyFutureGoal", "IsReliableMatch"
    ];

    private sealed class MatchState
    {
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }
        public int HomeRedCards { get; set; }
        public int AwayRedCards { get; set; }
        public int LastGoalMinute { get; set; } = -1;

        public MatchState Clone() => new()
        {
            HomeGoals = HomeGoals,
            AwayGoals = AwayGoals,
            HomeRedCards = HomeRedCards,
            AwayRedCards = AwayRedCards,
            LastGoalMinute = LastGoalMinute
        };
    }
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
    public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
    public int TriggerEventMinute { get; set; } = -1;
    public string TriggerEventSide { get; set; } = string.Empty;
    public int Minute { get; set; }
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    public int CurrentTotalGoals { get; set; }
    public int GoalDifference { get; set; }
    public string ScoreState { get; set; } = string.Empty;
    public string DetailedScoreState { get; set; } = string.Empty;
    public int HomeRedCards { get; set; }
    public int AwayRedCards { get; set; }
    public int RedCardDifference { get; set; }
    public int LastGoalMinute { get; set; }
    public int MinutesSinceLastGoal { get; set; }
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
            StateTrigger, TriggerEventMinute.ToString(CultureInfo.InvariantCulture), TriggerEventSide,
            Minute.ToString(CultureInfo.InvariantCulture), HomeGoals.ToString(CultureInfo.InvariantCulture), AwayGoals.ToString(CultureInfo.InvariantCulture), CurrentTotalGoals.ToString(CultureInfo.InvariantCulture), GoalDifference.ToString(CultureInfo.InvariantCulture), ScoreState, DetailedScoreState,
            HomeRedCards.ToString(CultureInfo.InvariantCulture), AwayRedCards.ToString(CultureInfo.InvariantCulture), RedCardDifference.ToString(CultureInfo.InvariantCulture), LastGoalMinute.ToString(CultureInfo.InvariantCulture), MinutesSinceLastGoal.ToString(CultureInfo.InvariantCulture), B(HasRecentGoal),
            SelectedTimingGroup, TimingFallback, D(EmpiricalWeight), D(WeibullRemainingShare), D(EmpiricalRemainingShare), D(TimingRemainingShare),
            ActualFinalHomeGoals.ToString(CultureInfo.InvariantCulture), ActualFinalAwayGoals.ToString(CultureInfo.InvariantCulture), ActualFinalTotalGoals.ToString(CultureInfo.InvariantCulture), ActualRemainingGoals.ToString(CultureInfo.InvariantCulture), B(AnyFutureGoal), B(IsReliableMatch)
        ];
    }
}
