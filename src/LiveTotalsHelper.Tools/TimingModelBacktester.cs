using System.Globalization;
using System.Text;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Tools;

public sealed class TimingBacktestOptions
{
    public string League { get; set; } = string.Empty;
    public List<int> TrainingSeasonIds { get; } = [];
    public List<int> BacktestSeasonIds { get; } = [];
    public List<int> Rounds { get; } = [];
    public List<int> SnapshotMinutes { get; } = [15, 30, 45, 60, 75];
    public int MaxModelMinute { get; set; } = 90;
    public int MinTrainingSnapshots { get; set; } = 20;
    public bool IncludeUnreliableMatches { get; set; }
    public string OutputPath { get; set; } = string.Empty;
}

public sealed class TimingBacktestResult
{
    public int TrainingMatchesChecked { get; set; }
    public int TrainingReliableMatches { get; set; }
    public int BacktestMatchesChecked { get; set; }
    public int BacktestReliableMatches { get; set; }
    public int TrainingSnapshots { get; set; }
    public int BacktestSnapshots { get; set; }
    public List<int> TrainingSeasonIds { get; } = [];
    public List<int> BacktestSeasonIds { get; } = [];
    public List<TimingBacktestSummaryRow> OverallRows { get; } = [];
    public List<TimingBacktestSummaryRow> ByMinuteRows { get; } = [];
    public List<TimingBacktestSummaryRow> ByStateRows { get; } = [];
    public List<TimingBacktestSummaryRow> ByMinuteAndStateRows { get; } = [];
    public List<TimingBacktestPredictionRow> Predictions { get; } = [];
    public List<string> Warnings { get; } = [];
    public string OutputPath { get; set; } = string.Empty;
}

public sealed class TimingBacktestSummaryRow
{
    public string Group { get; set; } = string.Empty;
    public int Count { get; set; }
    public double AvgPredictedRemainingGoals { get; set; }
    public double AvgActualRemainingGoals { get; set; }
    public double Bias => AvgPredictedRemainingGoals - AvgActualRemainingGoals;
    public double MeanAbsoluteError { get; set; }
    public double RootMeanSquaredError { get; set; }
}

public sealed class TimingBacktestPredictionRow
{
    public long SofaScoreEventId { get; set; }
    public int SeasonId { get; set; }
    public int RoundNumber { get; set; }
    public string Match { get; set; } = string.Empty;
    public int Minute { get; set; }
    public int CurrentHomeGoals { get; set; }
    public int CurrentAwayGoals { get; set; }
    public string ScoreState { get; set; } = string.Empty;
    public int ActualRemainingGoals { get; set; }
    public double PredictedRemainingGoals { get; set; }
    public string PredictionSource { get; set; } = string.Empty;
    public int TrainingSampleSize { get; set; }
}

public sealed class TimingModelBacktester
{
    private readonly LiveTotalsDbContext _db;
    private readonly TimingBacktestOptions _options;

    public TimingModelBacktester(LiveTotalsDbContext db, TimingBacktestOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<TimingBacktestResult> RunAsync(CancellationToken cancellationToken)
    {
        if (_options.TrainingSeasonIds.Count == 0)
            throw new ArgumentException("Provide --training-season-ids, for example --training-season-ids 48254,57783.");

        if (_options.BacktestSeasonIds.Count == 0)
            throw new ArgumentException("Provide --backtest-season-ids, for example --backtest-season-ids 71036.");

        if (_options.SnapshotMinutes.Count == 0)
            throw new ArgumentException("At least one snapshot minute is required.");

        if (_options.MaxModelMinute <= 0)
            throw new ArgumentException("--max-model-minute must be greater than 0.");

        if (_options.MinTrainingSnapshots < 1)
            throw new ArgumentException("--min-training-snapshots must be at least 1.");

        var result = new TimingBacktestResult { OutputPath = _options.OutputPath };
        result.TrainingSeasonIds.AddRange(_options.TrainingSeasonIds.Distinct().OrderBy(x => x));
        result.BacktestSeasonIds.AddRange(_options.BacktestSeasonIds.Distinct().OrderBy(x => x));

        List<PreparedMatch> trainingMatches = await LoadPreparedMatchesAsync(_options.TrainingSeasonIds, cancellationToken);
        result.TrainingMatchesChecked = trainingMatches.Count;
        trainingMatches = FilterReliable(trainingMatches, result.Warnings, "training");
        result.TrainingReliableMatches = trainingMatches.Count;

        List<PreparedMatch> backtestMatches = await LoadPreparedMatchesAsync(_options.BacktestSeasonIds, cancellationToken);
        result.BacktestMatchesChecked = backtestMatches.Count;
        backtestMatches = FilterReliable(backtestMatches, result.Warnings, "backtest");
        result.BacktestReliableMatches = backtestMatches.Count;

        if (trainingMatches.Count == 0)
            throw new ArgumentException("No reliable finished training matches found for the requested filters.");

        if (backtestMatches.Count == 0)
            throw new ArgumentException("No reliable finished backtest matches found for the requested filters.");

        List<SnapshotRow> trainingSnapshots = BuildSnapshots(trainingMatches);
        List<SnapshotRow> backtestSnapshots = BuildSnapshots(backtestMatches);
        result.TrainingSnapshots = trainingSnapshots.Count;
        result.BacktestSnapshots = backtestSnapshots.Count;

        Dictionary<ModelKey, SnapshotAggregate> exact = BuildAggregates(trainingSnapshots, x => new ModelKey(x.Minute, x.ScoreState));
        Dictionary<ModelKey, SnapshotAggregate> minuteOnly = BuildAggregates(trainingSnapshots, x => new ModelKey(x.Minute, "All"));
        Dictionary<ModelKey, SnapshotAggregate> stateOnly = BuildAggregates(trainingSnapshots, x => new ModelKey(-1, x.ScoreState));
        SnapshotAggregate global = SnapshotAggregate.From(trainingSnapshots);

        foreach (SnapshotRow snapshot in backtestSnapshots)
        {
            Prediction prediction = ResolvePrediction(snapshot, exact, minuteOnly, stateOnly, global);
            result.Predictions.Add(new TimingBacktestPredictionRow
            {
                SofaScoreEventId = snapshot.SofaScoreEventId,
                SeasonId = snapshot.SeasonId,
                RoundNumber = snapshot.RoundNumber,
                Match = snapshot.MatchName,
                Minute = snapshot.Minute,
                CurrentHomeGoals = snapshot.CurrentHomeGoals,
                CurrentAwayGoals = snapshot.CurrentAwayGoals,
                ScoreState = snapshot.ScoreState,
                ActualRemainingGoals = snapshot.ActualRemainingGoals,
                PredictedRemainingGoals = prediction.ExpectedRemainingGoals,
                PredictionSource = prediction.Source,
                TrainingSampleSize = prediction.SampleSize
            });
        }

        result.OverallRows.Add(Summarize("All", result.Predictions));
        result.ByMinuteRows.AddRange(result.Predictions
            .GroupBy(x => x.Minute)
            .OrderBy(x => x.Key)
            .Select(x => Summarize(x.Key.ToString(CultureInfo.InvariantCulture), x)));

        result.ByStateRows.AddRange(result.Predictions
            .GroupBy(x => x.ScoreState)
            .OrderBy(x => ScoreStateSort(x.Key))
            .Select(x => Summarize(x.Key, x)));

        result.ByMinuteAndStateRows.AddRange(result.Predictions
            .GroupBy(x => new { x.Minute, x.ScoreState })
            .OrderBy(x => x.Key.Minute)
            .ThenBy(x => ScoreStateSort(x.Key.ScoreState))
            .Select(x => Summarize($"{x.Key.Minute} / {x.Key.ScoreState}", x)));

        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_options.OutputPath)) ?? ".");
            await File.WriteAllTextAsync(_options.OutputPath, ToCsv(result.Predictions), Encoding.UTF8, cancellationToken);
        }

        return result;
    }

    private Prediction ResolvePrediction(
        SnapshotRow snapshot,
        IReadOnlyDictionary<ModelKey, SnapshotAggregate> exact,
        IReadOnlyDictionary<ModelKey, SnapshotAggregate> minuteOnly,
        IReadOnlyDictionary<ModelKey, SnapshotAggregate> stateOnly,
        SnapshotAggregate global)
    {
        ModelKey exactKey = new(snapshot.Minute, snapshot.ScoreState);
        if (exact.TryGetValue(exactKey, out SnapshotAggregate? exactAgg) && exactAgg.Count >= _options.MinTrainingSnapshots)
            return new Prediction(exactAgg.AvgRemainingGoals, $"minute+state:{snapshot.Minute}/{snapshot.ScoreState}", exactAgg.Count);

        ModelKey minuteKey = new(snapshot.Minute, "All");
        if (minuteOnly.TryGetValue(minuteKey, out SnapshotAggregate? minuteAgg) && minuteAgg.Count >= _options.MinTrainingSnapshots)
            return new Prediction(minuteAgg.AvgRemainingGoals, $"minute:{snapshot.Minute}", minuteAgg.Count);

        ModelKey stateKey = new(-1, snapshot.ScoreState);
        if (stateOnly.TryGetValue(stateKey, out SnapshotAggregate? stateAgg) && stateAgg.Count >= _options.MinTrainingSnapshots)
            return new Prediction(stateAgg.AvgRemainingGoals, $"state:{snapshot.ScoreState}", stateAgg.Count);

        return new Prediction(global.AvgRemainingGoals, "global", global.Count);
    }

    private async Task<List<PreparedMatch>> LoadPreparedMatchesAsync(IReadOnlyCollection<int> seasonIds, CancellationToken cancellationToken)
    {
        IQueryable<MatchEntity> query = _db.Matches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(_options.League))
            query = query.Where(x => x.LeagueName == _options.League || x.LeagueSlug == _options.League);

        query = query.Where(x => seasonIds.Contains(x.SofaScoreSeasonId));

        if (_options.Rounds.Count > 0)
            query = query.Where(x => _options.Rounds.Contains(x.RoundNumber));

        List<MatchEntity> matches = await query
            .Where(x => x.StatusType == "finished" || x.StatusDescription == "Ended" || x.StatusDescription == "Finished")
            .OrderBy(x => x.SofaScoreSeasonId)
            .ThenBy(x => x.RoundNumber)
            .ThenBy(x => x.StartTimeUtc)
            .ThenBy(x => x.SofaScoreEventId)
            .ToListAsync(cancellationToken);

        HashSet<int> matchIds = matches.Select(x => x.Id).ToHashSet();
        List<MatchEventEntity> goals = await _db.MatchEvents.AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId) && x.IncidentType == "goal")
            .OrderBy(x => x.MatchId)
            .ThenBy(x => x.TimeSeconds ?? (x.Minute * 60))
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        Dictionary<int, List<MatchEventEntity>> goalsByMatch = goals
            .GroupBy(x => x.MatchId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var prepared = new List<PreparedMatch>();
        foreach (MatchEntity match in matches)
        {
            int finalHome = match.HomeScoreCurrent ?? 0;
            int finalAway = match.AwayScoreCurrent ?? 0;
            List<MatchEventEntity> matchGoals = goalsByMatch.GetValueOrDefault(match.Id) ?? [];
            bool reliable = finalHome == matchGoals.Count(x => x.IsHome) && finalAway == matchGoals.Count(x => !x.IsHome);

            prepared.Add(new PreparedMatch
            {
                MatchId = match.Id,
                SofaScoreEventId = match.SofaScoreEventId,
                SeasonId = match.SofaScoreSeasonId,
                RoundNumber = match.RoundNumber,
                MatchName = $"{match.HomeTeamName} vs {match.AwayTeamName}",
                FinalHomeGoals = finalHome,
                FinalAwayGoals = finalAway,
                IsReliable = reliable,
                Goals = matchGoals.Select(ToGoal).OrderBy(x => x.ModelMinute).ThenBy(x => x.Id).ToList()
            });
        }

        return prepared;
    }

    private List<PreparedMatch> FilterReliable(List<PreparedMatch> matches, List<string> warnings, string label)
    {
        if (_options.IncludeUnreliableMatches)
            return matches;

        int unreliable = matches.Count(x => !x.IsReliable);
        if (unreliable > 0)
            warnings.Add($"Excluded {unreliable} unreliable {label} matches where final score does not match goal events.");

        return matches.Where(x => x.IsReliable).ToList();
    }

    private List<SnapshotRow> BuildSnapshots(IReadOnlyCollection<PreparedMatch> matches)
    {
        var rows = new List<SnapshotRow>();

        foreach (PreparedMatch match in matches)
        {
            int finalTotal = match.FinalHomeGoals + match.FinalAwayGoals;
            foreach (int minute in _options.SnapshotMinutes.Distinct().OrderBy(x => x))
            {
                if (minute < 0 || minute >= _options.MaxModelMinute)
                    continue;

                int currentHome = match.Goals.Count(x => x.IsHome && x.ModelMinute <= minute);
                int currentAway = match.Goals.Count(x => !x.IsHome && x.ModelMinute <= minute);
                int currentTotal = currentHome + currentAway;
                int remaining = Math.Max(0, finalTotal - currentTotal);
                string state = ScoreState(Math.Abs(currentHome - currentAway));

                rows.Add(new SnapshotRow
                {
                    SofaScoreEventId = match.SofaScoreEventId,
                    SeasonId = match.SeasonId,
                    RoundNumber = match.RoundNumber,
                    MatchName = match.MatchName,
                    Minute = minute,
                    CurrentHomeGoals = currentHome,
                    CurrentAwayGoals = currentAway,
                    ScoreState = state,
                    ActualRemainingGoals = remaining
                });
            }
        }

        return rows;
    }

    private GoalRow ToGoal(MatchEventEntity goal)
    {
        return new GoalRow
        {
            Id = goal.Id,
            IsHome = goal.IsHome,
            ModelMinute = ComputeModelMinute(goal)
        };
    }

    private int ComputeModelMinute(MatchEventEntity goal)
    {
        int minute;
        if (goal.TimeSeconds is > 0)
            minute = Math.Max(1, (int)Math.Ceiling(goal.TimeSeconds.Value / 60.0));
        else
            minute = Math.Max(1, goal.Minute + Math.Max(0, goal.AddedTime ?? 0));

        return Math.Min(minute, _options.MaxModelMinute);
    }

    private static Dictionary<ModelKey, SnapshotAggregate> BuildAggregates(IEnumerable<SnapshotRow> snapshots, Func<SnapshotRow, ModelKey> keySelector)
    {
        return snapshots
            .GroupBy(keySelector)
            .ToDictionary(x => x.Key, x => SnapshotAggregate.From(x));
    }

    private static TimingBacktestSummaryRow Summarize(string group, IEnumerable<TimingBacktestPredictionRow> rows)
    {
        TimingBacktestPredictionRow[] array = rows.ToArray();
        if (array.Length == 0)
            return new TimingBacktestSummaryRow { Group = group };

        return new TimingBacktestSummaryRow
        {
            Group = group,
            Count = array.Length,
            AvgPredictedRemainingGoals = array.Average(x => x.PredictedRemainingGoals),
            AvgActualRemainingGoals = array.Average(x => x.ActualRemainingGoals),
            MeanAbsoluteError = array.Average(x => Math.Abs(x.PredictedRemainingGoals - x.ActualRemainingGoals)),
            RootMeanSquaredError = Math.Sqrt(array.Average(x => Math.Pow(x.PredictedRemainingGoals - x.ActualRemainingGoals, 2)))
        };
    }

    private static string ToCsv(IEnumerable<TimingBacktestPredictionRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SofaScoreEventId,SeasonId,RoundNumber,Match,Minute,CurrentHomeGoals,CurrentAwayGoals,ScoreState,ActualRemainingGoals,PredictedRemainingGoals,PredictionSource,TrainingSampleSize");
        foreach (TimingBacktestPredictionRow row in rows)
        {
            string[] values =
            [
                row.SofaScoreEventId.ToString(CultureInfo.InvariantCulture),
                row.SeasonId.ToString(CultureInfo.InvariantCulture),
                row.RoundNumber.ToString(CultureInfo.InvariantCulture),
                row.Match,
                row.Minute.ToString(CultureInfo.InvariantCulture),
                row.CurrentHomeGoals.ToString(CultureInfo.InvariantCulture),
                row.CurrentAwayGoals.ToString(CultureInfo.InvariantCulture),
                row.ScoreState,
                row.ActualRemainingGoals.ToString(CultureInfo.InvariantCulture),
                row.PredictedRemainingGoals.ToString("0.######", CultureInfo.InvariantCulture),
                row.PredictionSource,
                row.TrainingSampleSize.ToString(CultureInfo.InvariantCulture)
            ];
            sb.AppendLine(string.Join(',', values.Select(EscapeCsv)));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    private static string ScoreState(int absGoalDiff)
    {
        return absGoalDiff switch
        {
            0 => "Level",
            1 => "OneGoalMargin",
            2 => "TwoGoalMargin",
            _ => "ThreePlusGoalMargin"
        };
    }

    private static int ScoreStateSort(string state)
    {
        return state switch
        {
            "Level" => 0,
            "OneGoalMargin" => 1,
            "TwoGoalMargin" => 2,
            "ThreePlusGoalMargin" => 3,
            _ => 99
        };
    }

    private sealed class PreparedMatch
    {
        public int MatchId { get; set; }
        public long SofaScoreEventId { get; set; }
        public int SeasonId { get; set; }
        public int RoundNumber { get; set; }
        public string MatchName { get; set; } = string.Empty;
        public int FinalHomeGoals { get; set; }
        public int FinalAwayGoals { get; set; }
        public bool IsReliable { get; set; }
        public List<GoalRow> Goals { get; set; } = [];
    }

    private sealed class GoalRow
    {
        public int Id { get; set; }
        public bool IsHome { get; set; }
        public int ModelMinute { get; set; }
    }

    private sealed class SnapshotRow
    {
        public long SofaScoreEventId { get; set; }
        public int SeasonId { get; set; }
        public int RoundNumber { get; set; }
        public string MatchName { get; set; } = string.Empty;
        public int Minute { get; set; }
        public int CurrentHomeGoals { get; set; }
        public int CurrentAwayGoals { get; set; }
        public string ScoreState { get; set; } = string.Empty;
        public int ActualRemainingGoals { get; set; }
    }

    private readonly record struct ModelKey(int Minute, string ScoreState);
    private readonly record struct Prediction(double ExpectedRemainingGoals, string Source, int SampleSize);

    private sealed class SnapshotAggregate
    {
        public int Count { get; set; }
        public double SumRemainingGoals { get; set; }
        public double AvgRemainingGoals => Count == 0 ? 0.0 : SumRemainingGoals / Count;

        public static SnapshotAggregate From(IEnumerable<SnapshotRow> snapshots)
        {
            SnapshotRow[] rows = snapshots.ToArray();
            return new SnapshotAggregate
            {
                Count = rows.Length,
                SumRemainingGoals = rows.Sum(x => x.ActualRemainingGoals)
            };
        }
    }
}
