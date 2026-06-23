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
    public int FinishedMatchesWithMarketTotal { get; set; }
    public int FinishedMatchesMissingMarketTotal { get; set; }
    public int RowsWithMarketTotal { get; set; }
    public int RowsMissingMarketTotal { get; set; }
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

        Dictionary<int, SelectedMarketTotal> marketTotalsByMatch = await LoadSelectedMarketTotalsAsync(matchIds, cancellationToken);

        var rows = new List<LiveTotalCalibrationDatasetRow>();
        var unreliableExamples = new List<string>();

        foreach (MatchEntity match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsFinished(match))
                continue;

            result.FinishedMatches++;
            marketTotalsByMatch.TryGetValue(match.Id, out SelectedMarketTotal? marketTotal);
            if (marketTotal is null)
                result.FinishedMatchesMissingMarketTotal++;
            else
                result.FinishedMatchesWithMarketTotal++;

            List<MatchEventEntity> matchEvents = eventsByMatch.GetValueOrDefault(match.Id) ?? [];
            List<MatchEventEntity> orderedEvents = matchEvents
                .OrderBy(x => x.TimeSeconds ?? (x.Minute * 60))
                .ThenBy(x => x.Id)
                .ToList();
            List<MatchEventEntity> rawGoals = orderedEvents.Where(IsGoal).ToList();
            List<MatchEventEntity> redCards = orderedEvents.Where(IsRedCard).ToList();
            GoalEventReconstruction reconstructedGoals = GoalEventScoreReconstructor.Reconstruct(match, rawGoals);

            int finalHome = match.HomeScoreCurrent ?? 0;
            int finalAway = match.AwayScoreCurrent ?? 0;
            int finalTotal = finalHome + finalAway;
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

            foreach (int minute in _options.SnapshotMinutes.Distinct().OrderBy(x => x))
            {
                List<ReconstructedGoalEvent> goalsBeforeMinute = reconstructedGoals.Goals.Where(x => x.Minute < minute).ToList();
                List<MatchEventEntity> redCardsBeforeMinute = redCards.Where(x => EventMinuteForModel(x) < minute).ToList();
                MatchState state = BuildState(goalsBeforeMinute, redCardsBeforeMinute);
                rows.Add(CreateRow(
                    match,
                    model,
                    finalHome,
                    finalAway,
                    finalTotal,
                    reliable,
                    marketTotal,
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
                var timeline = BuildTriggerTimeline(reconstructedGoals.Goals, redCards);

                foreach (TriggerTimelineEvent e in timeline)
                {
                    if (e.Goal is not null)
                    {
                        if (e.Goal.IsHomeGoal)
                            state.HomeGoals++;
                        else
                            state.AwayGoals++;

                        state.LastGoalMinute = e.Minute;

                        rows.Add(CreateRow(
                            match,
                            model,
                            finalHome,
                            finalAway,
                            finalTotal,
                            reliable,
                            marketTotal,
                            e.Minute,
                            LiveTotalStateTrigger.AfterGoal,
                            e.Minute,
                            e.Goal.Side,
                            state.Clone()));
                        result.AfterGoalStatesWritten++;
                    }
                    else if (e.RedCard is not null)
                    {
                        if (e.RedCard.IsHome)
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
                            marketTotal,
                            e.Minute,
                            LiveTotalStateTrigger.AfterRedCard,
                            e.Minute,
                            e.RedCard.IsHome ? "Home" : "Away",
                            state.Clone()));
                        result.AfterRedCardStatesWritten++;
                    }
                }
            }

        }

        result.RowsWithMarketTotal = rows.Count(x => x.ExpectedFinalGoals.HasValue);
        result.RowsMissingMarketTotal = rows.Count - result.RowsWithMarketTotal;

        if (unreliableExamples.Count > 0)
            result.Warnings.Add($"Unreliable matches skipped: {result.UnreliableFinishedMatches}. Examples: {string.Join("; ", unreliableExamples)}");
        if (result.FinishedMatchesMissingMarketTotal > 0)
            result.Warnings.Add($"Market total missing for {result.FinishedMatchesMissingMarketTotal}/{result.FinishedMatches} finished matches. Model/evaluation commands skip rows without ExpectedFinalGoals.");

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
        SelectedMarketTotal? marketTotal,
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
            SeasonId = match.SeasonId,
            SeasonName = match.SeasonName,
            SeasonYear = match.SeasonYear,
            RoundNumber = match.RoundNumber,
            MatchId = match.Id,
            EventId = match.EventId,
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
            MarketTotalLine = marketTotal?.RepresentativeLine,
            MarketTotalSource = marketTotal?.Source ?? string.Empty,
            MarketExpectedFinalGoals = marketTotal?.ExpectedFinalGoals,
            ExpectedFinalGoals = marketTotal?.ExpectedFinalGoals,
            ExpectedFinalGoalsSource = marketTotal is null ? string.Empty : "MarketTotal",
            ActualFinalHomeGoals = finalHome,
            ActualFinalAwayGoals = finalAway,
            ActualFinalTotalGoals = finalTotal,
            ActualRemainingGoals = finalTotal - currentTotal,
            AnyFutureGoal = finalTotal > currentTotal,
            IsReliableMatch = reliable
        };
    }

    private static MatchState BuildState(IEnumerable<ReconstructedGoalEvent> goals, IEnumerable<MatchEventEntity> redCards)
    {
        var state = new MatchState();
        foreach (ReconstructedGoalEvent goal in goals.OrderBy(x => x.Minute).ThenBy(x => x.Sequence))
        {
            if (goal.IsHomeGoal)
                state.HomeGoals++;
            else
                state.AwayGoals++;
            state.LastGoalMinute = goal.Minute;
        }

        foreach (MatchEventEntity e in redCards.OrderBy(EventMinuteForModel).ThenBy(x => x.Id))
        {
            if (e.IsHome)
                state.HomeRedCards++;
            else
                state.AwayRedCards++;
        }

        return state;
    }

    private static List<TriggerTimelineEvent> BuildTriggerTimeline(IEnumerable<ReconstructedGoalEvent> goals, IEnumerable<MatchEventEntity> redCards)
    {
        var timeline = new List<TriggerTimelineEvent>();

        timeline.AddRange(goals.Select(x => new TriggerTimelineEvent
        {
            Minute = x.Minute,
            SortKey = GoalEventScoreReconstructor.EventSortKey(x.Source),
            Sequence = x.Sequence,
            Goal = x
        }));

        timeline.AddRange(redCards.Select(x => new TriggerTimelineEvent
        {
            Minute = EventMinuteForModel(x),
            SortKey = GoalEventScoreReconstructor.EventSortKey(x),
            Sequence = int.MaxValue,
            RedCard = x
        }));

        return timeline
            .OrderBy(x => x.SortKey)
            .ThenBy(x => x.Sequence)
            .ToList();
    }

    private async Task<Dictionary<int, SelectedMarketTotal>> LoadSelectedMarketTotalsAsync(HashSet<int> matchIds, CancellationToken cancellationToken)
    {
        List<FlashscoreOddsEntity> odds = await _db.FlashscoreOdds.AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId) && x.Line.HasValue && x.Odds > 1.0)
            .ToListAsync(cancellationToken);

        var candidates = new List<MarketTotalCandidate>();
        foreach (var group in odds
            .Where(x => IsTotalMarket(x.Market))
            .GroupBy(x => new
            {
                x.MatchId,
                Bookmaker = NormalizeBookmaker(x.Bookmaker),
                Line = Math.Round(x.Line!.Value, 2)
            }))
        {
            FlashscoreOddsEntity? over = group
                .Where(x => IsOverSelection(x.Selection))
                .OrderBy(OddsTimestamp)
                .ThenBy(x => x.Id)
                .LastOrDefault();
            FlashscoreOddsEntity? under = group
                .Where(x => IsUnderSelection(x.Selection))
                .OrderBy(OddsTimestamp)
                .ThenBy(x => x.Id)
                .LastOrDefault();

            if (over is null || under is null)
                continue;

            double overround = 1.0 / over.Odds + 1.0 / under.Odds;
            if (overround < 0.90 || overround > 1.25)
                continue;

            double fairOver = TotalGoalsPricingCalculator.RemoveTwoWayMargin(over.Odds, under.Odds);
            double expected = TotalGoalsPricingCalculator.EstimateTotalGoalsFromLine(group.Key.Line, fairOver);
            DateTimeOffset timestamp = OddsTimestamp(over) > OddsTimestamp(under) ? OddsTimestamp(over) : OddsTimestamp(under);

            candidates.Add(new MarketTotalCandidate(
                group.Key.MatchId,
                group.Key.Bookmaker,
                group.Key.Line,
                over.Odds,
                under.Odds,
                fairOver,
                expected,
                overround,
                timestamp));
        }

        return candidates
            .GroupBy(x => x.MatchId)
            .ToDictionary(
                x => x.Key,
                x => SelectMarketTotal(x.ToList()));
    }

    private static SelectedMarketTotal SelectMarketTotal(List<MarketTotalCandidate> matchCandidates)
    {
        List<LineMarketSummary> lineSummaries = matchCandidates
            .GroupBy(x => x.Line)
            .Select(g =>
            {
                List<MarketTotalCandidate> lineCandidates = g.ToList();
                MarketTotalCandidate representative = lineCandidates
                    .OrderBy(c => Math.Abs(c.FairOverProbability - 0.50))
                    .ThenBy(c => Math.Abs(c.Overround - 1.0))
                    .ThenByDescending(c => c.Timestamp)
                    .First();

                return new LineMarketSummary(
                    Line: g.Key,
                    PairCount: lineCandidates.Count,
                    MedianFairOverProbability: Median(lineCandidates.Select(c => c.FairOverProbability)),
                    MedianExpectedFinalGoals: Median(lineCandidates.Select(c => c.ExpectedFinalGoals)),
                    MedianOverround: Median(lineCandidates.Select(c => c.Overround)),
                    LatestTimestamp: lineCandidates.Max(c => c.Timestamp),
                    Representative: representative);
            })
            .ToList();

        LineMarketSummary selected = lineSummaries
            .OrderBy(x => Math.Abs(x.MedianFairOverProbability - 0.50))
            .ThenByDescending(x => x.PairCount)
            .ThenBy(x => Math.Abs(x.MedianOverround - 1.0))
            .ThenByDescending(x => x.LatestTimestamp)
            .First();

        MarketTotalCandidate representative = selected.Representative;
        int ignoredPairs = Math.Max(0, matchCandidates.Count - selected.PairCount);
        string source = $"balanced-line selector: selected line {selected.Line:0.##} from {selected.PairCount}/{matchCandidates.Count} clean O/U pair(s), median fairOver={selected.MedianFairOverProbability:0.###}, expected={selected.MedianExpectedFinalGoals:0.###}; representative {representative.Bookmaker} O {representative.OverOdds:0.###} U {representative.UnderOdds:0.###}; ignored alternative-line pairs={ignoredPairs}";

        return new SelectedMarketTotal(selected.Line, selected.MedianExpectedFinalGoals, source);
    }

    private static DateTimeOffset OddsTimestamp(FlashscoreOddsEntity row)
        => row.DownloadedAtUtc.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(row.DownloadedAtUtc.Value, DateTimeKind.Utc))
            : row.ImportedAtUtc;

    private static string NormalizeBookmaker(string bookmaker)
        => string.IsNullOrWhiteSpace(bookmaker) ? "unknown" : bookmaker.Trim();

    private static double Median(IEnumerable<double> values)
    {
        List<double> ordered = values.Where(x => x > 0 && !double.IsNaN(x) && !double.IsInfinity(x)).OrderBy(x => x).ToList();
        if (ordered.Count == 0)
            return 0.0;
        int mid = ordered.Count / 2;
        return ordered.Count % 2 == 1 ? ordered[mid] : (ordered[mid - 1] + ordered[mid]) / 2.0;
    }

    private static bool IsTotalMarket(string market)
    {
        string normalized = NormalizeToken(market);
        return normalized == "overunder" || normalized.Contains("overunder", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOverSelection(string selection)
    {
        string normalized = NormalizeToken(selection);
        return normalized == "over" || normalized.StartsWith("over", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderSelection(string selection)
    {
        string normalized = NormalizeToken(selection);
        return normalized == "under" || normalized.StartsWith("under", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToken(string value)
        => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

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
        => GoalEventScoreReconstructor.GoalMinuteForModel(e);

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
        "LeagueName", "LeagueSlug", "SeasonId", "SeasonName", "SeasonYear", "RoundNumber", "MatchId", "EventId", "StartTimeUtc",
        "HomeTeamName", "AwayTeamName",
        "StateTrigger", "TriggerEventMinute", "TriggerEventSide",
        "Minute", "HomeGoals", "AwayGoals", "CurrentTotalGoals", "GoalDifference", "ScoreState", "DetailedScoreState",
        "HomeRedCards", "AwayRedCards", "RedCardDifference", "LastGoalMinute", "MinutesSinceLastGoal", "HasRecentGoal",
        "SelectedTimingGroup", "TimingFallback", "EmpiricalWeight", "WeibullRemainingShare", "EmpiricalRemainingShare", "TimingRemainingShare",
        "MarketTotalLine", "MarketTotalSource", "MarketExpectedFinalGoals", "ExpectedFinalGoals", "ExpectedFinalGoalsSource",
        "ActualFinalHomeGoals", "ActualFinalAwayGoals", "ActualFinalTotalGoals", "ActualRemainingGoals", "AnyFutureGoal", "IsReliableMatch"
    ];

    private sealed record MarketTotalCandidate(int MatchId, string Bookmaker, double Line, double OverOdds, double UnderOdds, double FairOverProbability, double ExpectedFinalGoals, double Overround, DateTimeOffset Timestamp);

    private sealed record LineMarketSummary(double Line, int PairCount, double MedianFairOverProbability, double MedianExpectedFinalGoals, double MedianOverround, DateTimeOffset LatestTimestamp, MarketTotalCandidate Representative);

    private sealed record SelectedMarketTotal(double RepresentativeLine, double ExpectedFinalGoals, string Source);

    private sealed class TriggerTimelineEvent
{
    public int Minute { get; init; }
    public int SortKey { get; init; }
    public int Sequence { get; init; }
    public ReconstructedGoalEvent? Goal { get; init; }
    public MatchEventEntity? RedCard { get; init; }
}

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
    public int SeasonId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public string SeasonYear { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public int MatchId { get; set; }
    public string EventId { get; set; } = string.Empty;
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
    public double? MarketTotalLine { get; set; }
    public string MarketTotalSource { get; set; } = string.Empty;
    public double? MarketExpectedFinalGoals { get; set; }
    public double? ExpectedFinalGoals { get; set; }
    public string ExpectedFinalGoalsSource { get; set; } = string.Empty;
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
            LeagueName, LeagueSlug, SeasonId.ToString(CultureInfo.InvariantCulture), SeasonName, SeasonYear, RoundNumber.ToString(CultureInfo.InvariantCulture), MatchId.ToString(CultureInfo.InvariantCulture), EventId, StartTimeUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            HomeTeamName, AwayTeamName,
            StateTrigger, TriggerEventMinute.ToString(CultureInfo.InvariantCulture), TriggerEventSide,
            Minute.ToString(CultureInfo.InvariantCulture), HomeGoals.ToString(CultureInfo.InvariantCulture), AwayGoals.ToString(CultureInfo.InvariantCulture), CurrentTotalGoals.ToString(CultureInfo.InvariantCulture), GoalDifference.ToString(CultureInfo.InvariantCulture), ScoreState, DetailedScoreState,
            HomeRedCards.ToString(CultureInfo.InvariantCulture), AwayRedCards.ToString(CultureInfo.InvariantCulture), RedCardDifference.ToString(CultureInfo.InvariantCulture), LastGoalMinute.ToString(CultureInfo.InvariantCulture), MinutesSinceLastGoal.ToString(CultureInfo.InvariantCulture), B(HasRecentGoal),
            SelectedTimingGroup, TimingFallback, D(EmpiricalWeight), D(WeibullRemainingShare), D(EmpiricalRemainingShare), D(TimingRemainingShare),
            MarketTotalLine.HasValue ? D(MarketTotalLine.Value) : string.Empty, MarketTotalSource, MarketExpectedFinalGoals.HasValue ? D(MarketExpectedFinalGoals.Value) : string.Empty, ExpectedFinalGoals.HasValue ? D(ExpectedFinalGoals.Value) : string.Empty, ExpectedFinalGoalsSource,
            ActualFinalHomeGoals.ToString(CultureInfo.InvariantCulture), ActualFinalAwayGoals.ToString(CultureInfo.InvariantCulture), ActualFinalTotalGoals.ToString(CultureInfo.InvariantCulture), ActualRemainingGoals.ToString(CultureInfo.InvariantCulture), B(AnyFutureGoal), B(IsReliableMatch)
        ];
    }
}
