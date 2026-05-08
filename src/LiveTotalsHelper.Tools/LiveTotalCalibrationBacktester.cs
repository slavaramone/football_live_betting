using System.Globalization;
using System.Text;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using LiveTotalsHelper.Modeling;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalCalibrationBacktestOptions
{
    public string League { get; set; } = string.Empty;
    public List<int> TrainingSeasonIds { get; } = [];
    public List<int> BacktestSeasonIds { get; } = [];
    public List<int> Rounds { get; } = [];
    public List<int> SnapshotMinutes { get; } = [15, 25, 30, 35, 45, 60, 75];
    public List<double> TargetLines { get; } = [1.5, 2.0, 2.5, 3.0, 3.5, 4.0];
    public List<double> TestEmpiricalWeights { get; } = [];
    public int MaxModelMinute { get; set; } = 90;
    public int MinTrainingSnapshots { get; set; } = 20;
    public bool IncludeUnreliableMatches { get; set; }
    public bool WalkForward { get; set; }
    public bool UseCurrentSeasonVolumeCalibration { get; set; }
    public int PriorStrengthMatches { get; set; } = 100;
    public string OutputPath { get; set; } = string.Empty;
}

public sealed class LiveTotalCalibrationBacktestResult
{
    public int TrainingMatchesChecked { get; set; }
    public int TrainingReliableMatches { get; set; }
    public int BacktestMatchesChecked { get; set; }
    public int BacktestReliableMatches { get; set; }
    public int TrainingSnapshots { get; set; }
    public int BacktestSnapshots { get; set; }
    public bool WalkForward { get; set; }
    public bool UseCurrentSeasonVolumeCalibration { get; set; }
    public int PriorStrengthMatches { get; set; }
    public int WalkForwardTrainingSnapshotsAdded { get; set; }
    public List<int> TrainingSeasonIds { get; } = [];
    public List<int> BacktestSeasonIds { get; } = [];
    public List<double> TestedEmpiricalWeights { get; } = [];
    public List<LiveTotalCalibrationSummaryRow> OverallRows { get; } = [];
    public List<LiveTotalCalibrationSummaryRow> ByMinuteRows { get; } = [];
    public List<LiveTotalCalibrationSummaryRow> ByStateRows { get; } = [];
    public List<LiveTotalCalibrationSummaryRow> ByStateMinuteLineRows { get; } = [];
    public List<LiveTotalCalibrationPredictionRow> Predictions { get; } = [];
    public List<string> Warnings { get; } = [];
    public string OutputPath { get; set; } = string.Empty;
}

public sealed class LiveTotalCalibrationSummaryRow
{
    public string Group { get; set; } = string.Empty;
    public int Count { get; set; }
    public double AvgPredictedOverProbability { get; set; }
    public double AvgActualOverHitRate { get; set; }
    public double OverCalibrationError => AvgPredictedOverProbability - AvgActualOverHitRate;
    public double AvgPredictedUnderProbability { get; set; }
    public double AvgActualUnderHitRate { get; set; }
    public double UnderCalibrationError => AvgPredictedUnderProbability - AvgActualUnderHitRate;
    public double OverBrierScore { get; set; }
    public double UnderBrierScore { get; set; }
    public double AvgPredictedRemainingGoals { get; set; }
    public double AvgActualRemainingGoals { get; set; }
}

public sealed class LiveTotalCalibrationPredictionRow
{
    public long SofaScoreEventId { get; set; }
    public int SeasonId { get; set; }
    public int RoundNumber { get; set; }
    public string Match { get; set; } = string.Empty;
    public int Minute { get; set; }
    public int CurrentHomeGoals { get; set; }
    public int CurrentAwayGoals { get; set; }
    public int CurrentGoals => CurrentHomeGoals + CurrentAwayGoals;
    public int FinalGoals { get; set; }
    public string ScoreState { get; set; } = string.Empty;
    public int ActualRemainingGoals { get; set; }
    public double Line { get; set; }
    public double EmpiricalWeight { get; set; }
    public double PredictedRemainingGoals { get; set; }
    public double EmpiricalPrediction { get; set; }
    public double WeibullPrediction { get; set; }
    public double CurrentSeasonVolumeFactor { get; set; } = 1.0;
    public double PredictedOverProbability { get; set; }
    public double PredictedPushProbability { get; set; }
    public double PredictedUnderProbability { get; set; }
    public double ActualOverHit { get; set; }
    public double ActualPushHit { get; set; }
    public double ActualUnderHit { get; set; }
    public double OverBrier => Math.Pow(PredictedOverProbability - ActualOverHit, 2);
    public double UnderBrier => Math.Pow(PredictedUnderProbability - ActualUnderHit, 2);
    public string PredictionSource { get; set; } = string.Empty;
    public int TrainingSampleSize { get; set; }
}

public sealed class LiveTotalCalibrationBacktester
{
    private readonly LiveTotalsDbContext _db;
    private readonly LiveTotalCalibrationBacktestOptions _options;

    public LiveTotalCalibrationBacktester(LiveTotalsDbContext db, LiveTotalCalibrationBacktestOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<LiveTotalCalibrationBacktestResult> RunAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        var result = new LiveTotalCalibrationBacktestResult { OutputPath = _options.OutputPath };
        result.TrainingSeasonIds.AddRange(_options.TrainingSeasonIds.Distinct().OrderBy(x => x));
        result.BacktestSeasonIds.AddRange(_options.BacktestSeasonIds.Distinct().OrderBy(x => x));
        result.TestedEmpiricalWeights.AddRange(GetEmpiricalWeights());
        result.WalkForward = _options.WalkForward;
        result.UseCurrentSeasonVolumeCalibration = _options.UseCurrentSeasonVolumeCalibration;
        result.PriorStrengthMatches = _options.PriorStrengthMatches;

        List<PreparedMatch> trainingMatches = await LoadPreparedMatchesAsync(_options.TrainingSeasonIds, applyRoundFilter: true, cancellationToken);
        result.TrainingMatchesChecked = trainingMatches.Count;
        trainingMatches = FilterReliable(trainingMatches, result.Warnings, "training");
        result.TrainingReliableMatches = trainingMatches.Count;

        List<PreparedMatch> allBacktestMatches = await LoadPreparedMatchesAsync(_options.BacktestSeasonIds, applyRoundFilter: !_options.WalkForward, cancellationToken);
        int backtestMatchesChecked = allBacktestMatches.Count;
        allBacktestMatches = FilterReliable(allBacktestMatches, result.Warnings, "backtest");

        List<PreparedMatch> selectedBacktestMatches = _options.WalkForward ? ApplyRoundFilter(allBacktestMatches) : allBacktestMatches;
        result.BacktestMatchesChecked = _options.WalkForward ? backtestMatchesChecked : selectedBacktestMatches.Count;
        result.BacktestReliableMatches = selectedBacktestMatches.Count;

        if (trainingMatches.Count == 0)
            throw new ArgumentException("No reliable finished training matches found for the requested filters.");
        if (selectedBacktestMatches.Count == 0)
            throw new ArgumentException("No reliable finished backtest matches found for the requested filters.");

        List<SnapshotRow> baseTrainingSnapshots = BuildSnapshots(trainingMatches);
        result.TrainingSnapshots = baseTrainingSnapshots.Count;

        if (_options.WalkForward)
            RunWalkForward(result, trainingMatches, baseTrainingSnapshots, allBacktestMatches, selectedBacktestMatches, cancellationToken);
        else
        {
            List<SnapshotRow> backtestSnapshots = BuildSnapshots(selectedBacktestMatches);
            result.BacktestSnapshots = backtestSnapshots.Count;
            TimingWeibullPredictor weibullPredictor = BuildWeibullPredictor(trainingMatches, baseTrainingSnapshots);
            PredictSnapshots(result, baseTrainingSnapshots, weibullPredictor, backtestSnapshots);
        }

        BuildSummaries(result);

        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_options.OutputPath)) ?? ".");
            await File.WriteAllTextAsync(_options.OutputPath, ToCsv(result.Predictions), Encoding.UTF8, cancellationToken);
        }

        return result;
    }

    private void ValidateOptions()
    {
        if (_options.TrainingSeasonIds.Count == 0)
            throw new ArgumentException("Provide --training-season-ids.");
        if (_options.BacktestSeasonIds.Count == 0)
            throw new ArgumentException("Provide --backtest-season-ids.");
        if (_options.SnapshotMinutes.Count == 0)
            throw new ArgumentException("At least one snapshot minute is required.");
        if (_options.TargetLines.Count == 0)
            throw new ArgumentException("At least one target line is required.");
        if (_options.MaxModelMinute <= 0)
            throw new ArgumentException("--max-model-minute must be greater than 0.");
        if (_options.MinTrainingSnapshots < 1)
            throw new ArgumentException("--min-training-snapshots must be at least 1.");
        if (_options.PriorStrengthMatches < 0)
            throw new ArgumentException("--prior-strength-matches must be zero or greater.");
        if (_options.UseCurrentSeasonVolumeCalibration && !_options.WalkForward)
            throw new ArgumentException("--use-current-season-volume-calibration requires --walk-forward true.");
        foreach (double weight in _options.TestEmpiricalWeights)
        {
            if (weight < 0.0 || weight > 1.0)
                throw new ArgumentException("--test-empirical-weights values must be between 0 and 1.");
        }
    }

    private List<double> GetEmpiricalWeights()
    {
        List<double> weights = _options.TestEmpiricalWeights.Count == 0 ? [0.8] : _options.TestEmpiricalWeights;
        return weights.Distinct().OrderBy(x => x).ToList();
    }

    private void RunWalkForward(
        LiveTotalCalibrationBacktestResult result,
        IReadOnlyCollection<PreparedMatch> baseTrainingMatches,
        IReadOnlyCollection<SnapshotRow> baseTrainingSnapshots,
        IReadOnlyCollection<PreparedMatch> allBacktestMatches,
        IReadOnlyCollection<PreparedMatch> selectedBacktestMatches,
        CancellationToken cancellationToken)
    {
        List<PreparedMatch> orderedTestMatches = selectedBacktestMatches
            .OrderBy(x => x.SeasonId)
            .ThenBy(x => x.RoundNumber)
            .ThenBy(x => x.SofaScoreEventId)
            .ToList();

        int baseTrainingGoals = baseTrainingMatches.Sum(x => x.FinalHomeGoals + x.FinalAwayGoals);
        int baseTrainingMatchCount = baseTrainingMatches.Count;

        foreach (var roundGroup in orderedTestMatches.GroupBy(x => new { x.SeasonId, x.RoundNumber }).OrderBy(x => x.Key.SeasonId).ThenBy(x => x.Key.RoundNumber))
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<PreparedMatch> priorSameSeasonMatches = allBacktestMatches
                .Where(x => x.SeasonId == roundGroup.Key.SeasonId && x.RoundNumber < roundGroup.Key.RoundNumber)
                .ToList();

            List<PreparedMatch> walkForwardTrainingMatches = new(baseTrainingMatches);
            walkForwardTrainingMatches.AddRange(priorSameSeasonMatches);

            List<SnapshotRow> walkForwardTrainingSnapshots = new(baseTrainingSnapshots);
            List<SnapshotRow> priorSnapshots = BuildSnapshots(priorSameSeasonMatches);
            walkForwardTrainingSnapshots.AddRange(priorSnapshots);
            result.WalkForwardTrainingSnapshotsAdded += priorSnapshots.Count;

            double currentSeasonVolumeFactor = _options.UseCurrentSeasonVolumeCalibration
                ? ComputeCurrentSeasonVolumeFactor(baseTrainingGoals, baseTrainingMatchCount, priorSameSeasonMatches)
                : 1.0;

            TimingWeibullPredictor weibullPredictor = BuildWeibullPredictor(walkForwardTrainingMatches, walkForwardTrainingSnapshots);
            List<SnapshotRow> testSnapshots = BuildSnapshots(roundGroup.ToList());
            result.BacktestSnapshots += testSnapshots.Count;
            PredictSnapshots(result, walkForwardTrainingSnapshots, weibullPredictor, testSnapshots, currentSeasonVolumeFactor);
        }
    }

    private void PredictSnapshots(
        LiveTotalCalibrationBacktestResult result,
        IReadOnlyCollection<SnapshotRow> trainingSnapshots,
        TimingWeibullPredictor weibullPredictor,
        IReadOnlyCollection<SnapshotRow> backtestSnapshots,
        double currentSeasonVolumeFactor = 1.0)
    {
        Dictionary<ModelKey, SnapshotAggregate> exact = BuildAggregates(trainingSnapshots, x => new ModelKey(x.Minute, x.ScoreState));
        Dictionary<ModelKey, SnapshotAggregate> minuteOnly = BuildAggregates(trainingSnapshots, x => new ModelKey(x.Minute, ScoreStateResolver.All));
        Dictionary<ModelKey, SnapshotAggregate> stateOnly = BuildAggregates(trainingSnapshots, x => new ModelKey(-1, x.ScoreState));
        SnapshotAggregate global = SnapshotAggregate.From(trainingSnapshots);

        foreach (SnapshotRow snapshot in backtestSnapshots)
        {
            Prediction empirical = ResolveEmpiricalPrediction(snapshot, exact, minuteOnly, stateOnly, global);
            Prediction weibull = weibullPredictor.Predict(snapshot);
            if (weibull.SampleSize == 0)
                weibull = empirical;

            foreach (double empiricalWeight in result.TestedEmpiricalWeights)
            {
                double blendedBase = empirical.ExpectedRemainingGoals * empiricalWeight + weibull.ExpectedRemainingGoals * (1.0 - empiricalWeight);
                double remainingGoals = Math.Max(0.0, blendedBase * currentSeasonVolumeFactor);
                string source = $"blend:wEmp={empiricalWeight.ToString("0.##", CultureInfo.InvariantCulture)}|emp={empirical.Source}|wei={weibull.Source}";
                if (Math.Abs(currentSeasonVolumeFactor - 1.0) > 0.000001)
                    source += "|season-volume:" + currentSeasonVolumeFactor.ToString("0.###", CultureInfo.InvariantCulture);

                foreach (double line in _options.TargetLines.Distinct().OrderBy(x => x))
                {
                    OverSettlementProbabilities model = TotalGoalsPricingCalculator.CalculateOverSettlementProbabilities(line, snapshot.CurrentGoals, remainingGoals);
                    ActualSettlement actual = CalculateActualSettlement(line, snapshot.FinalGoals);

                    result.Predictions.Add(new LiveTotalCalibrationPredictionRow
                    {
                        SofaScoreEventId = snapshot.SofaScoreEventId,
                        SeasonId = snapshot.SeasonId,
                        RoundNumber = snapshot.RoundNumber,
                        Match = snapshot.MatchName,
                        Minute = snapshot.Minute,
                        CurrentHomeGoals = snapshot.CurrentHomeGoals,
                        CurrentAwayGoals = snapshot.CurrentAwayGoals,
                        FinalGoals = snapshot.FinalGoals,
                        ScoreState = snapshot.ScoreState,
                        ActualRemainingGoals = snapshot.ActualRemainingGoals,
                        Line = line,
                        EmpiricalWeight = empiricalWeight,
                        PredictedRemainingGoals = remainingGoals,
                        EmpiricalPrediction = empirical.ExpectedRemainingGoals,
                        WeibullPrediction = weibull.ExpectedRemainingGoals,
                        CurrentSeasonVolumeFactor = currentSeasonVolumeFactor,
                        PredictedOverProbability = model.WinProbability,
                        PredictedPushProbability = model.PushProbability,
                        PredictedUnderProbability = model.LossProbability,
                        ActualOverHit = actual.OverWinShare,
                        ActualPushHit = actual.PushShare,
                        ActualUnderHit = actual.UnderWinShare,
                        PredictionSource = source,
                        TrainingSampleSize = empirical.SampleSize
                    });
                }
            }
        }
    }

    private Prediction ResolveEmpiricalPrediction(
        SnapshotRow snapshot,
        IReadOnlyDictionary<ModelKey, SnapshotAggregate> exact,
        IReadOnlyDictionary<ModelKey, SnapshotAggregate> minuteOnly,
        IReadOnlyDictionary<ModelKey, SnapshotAggregate> stateOnly,
        SnapshotAggregate global)
    {
        if (exact.TryGetValue(new ModelKey(snapshot.Minute, snapshot.ScoreState), out SnapshotAggregate? exactAgg) && exactAgg.Count >= _options.MinTrainingSnapshots)
            return new Prediction(exactAgg.AvgRemainingGoals, $"minute+state:{snapshot.Minute}/{snapshot.ScoreState}", exactAgg.Count);
        if (minuteOnly.TryGetValue(new ModelKey(snapshot.Minute, ScoreStateResolver.All), out SnapshotAggregate? minuteAgg) && minuteAgg.Count >= _options.MinTrainingSnapshots)
            return new Prediction(minuteAgg.AvgRemainingGoals, $"minute:{snapshot.Minute}", minuteAgg.Count);
        if (stateOnly.TryGetValue(new ModelKey(-1, snapshot.ScoreState), out SnapshotAggregate? stateAgg) && stateAgg.Count >= _options.MinTrainingSnapshots)
            return new Prediction(stateAgg.AvgRemainingGoals, $"state:{snapshot.ScoreState}", stateAgg.Count);
        return new Prediction(global.AvgRemainingGoals, "global", global.Count);
    }

    private void BuildSummaries(LiveTotalCalibrationBacktestResult result)
    {
        result.OverallRows.AddRange(result.Predictions
            .GroupBy(x => x.EmpiricalWeight)
            .OrderBy(x => x.Key)
            .Select(x => Summarize(WeightLabel(x.Key), x)));

        result.ByMinuteRows.AddRange(result.Predictions
            .GroupBy(x => new { x.EmpiricalWeight, x.Minute })
            .OrderBy(x => x.Key.EmpiricalWeight)
            .ThenBy(x => x.Key.Minute)
            .Select(x => Summarize($"{WeightLabel(x.Key.EmpiricalWeight)} / {x.Key.Minute}", x)));

        result.ByStateRows.AddRange(result.Predictions
            .GroupBy(x => new { x.EmpiricalWeight, x.ScoreState })
            .OrderBy(x => x.Key.EmpiricalWeight)
            .ThenBy(x => ScoreStateResolver.SortKey(x.Key.ScoreState))
            .Select(x => Summarize($"{WeightLabel(x.Key.EmpiricalWeight)} / {x.Key.ScoreState}", x)));

        result.ByStateMinuteLineRows.AddRange(result.Predictions
            .GroupBy(x => new { x.EmpiricalWeight, x.ScoreState, x.Minute, x.Line })
            .OrderBy(x => x.Key.EmpiricalWeight)
            .ThenBy(x => ScoreStateResolver.SortKey(x.Key.ScoreState))
            .ThenBy(x => x.Key.Minute)
            .ThenBy(x => x.Key.Line)
            .Select(x => Summarize($"{WeightLabel(x.Key.EmpiricalWeight)} / {x.Key.ScoreState} / {x.Key.Minute} / {x.Key.Line:0.##}", x)));
    }

    private static LiveTotalCalibrationSummaryRow Summarize(string group, IEnumerable<LiveTotalCalibrationPredictionRow> rows)
    {
        LiveTotalCalibrationPredictionRow[] array = rows.ToArray();
        if (array.Length == 0)
            return new LiveTotalCalibrationSummaryRow { Group = group };

        return new LiveTotalCalibrationSummaryRow
        {
            Group = group,
            Count = array.Length,
            AvgPredictedOverProbability = array.Average(x => x.PredictedOverProbability),
            AvgActualOverHitRate = array.Average(x => x.ActualOverHit),
            AvgPredictedUnderProbability = array.Average(x => x.PredictedUnderProbability),
            AvgActualUnderHitRate = array.Average(x => x.ActualUnderHit),
            OverBrierScore = array.Average(x => x.OverBrier),
            UnderBrierScore = array.Average(x => x.UnderBrier),
            AvgPredictedRemainingGoals = array.Average(x => x.PredictedRemainingGoals),
            AvgActualRemainingGoals = array.Average(x => x.ActualRemainingGoals)
        };
    }

    private TimingWeibullPredictor BuildWeibullPredictor(IReadOnlyCollection<PreparedMatch> trainingMatches, IReadOnlyCollection<SnapshotRow> trainingSnapshots)
    {
        var goalMinutesByState = new Dictionary<string, List<double>>(StringComparer.OrdinalIgnoreCase)
        {
            [ScoreStateResolver.All] = []
        };

        foreach (PreparedMatch match in trainingMatches)
        {
            int home = 0;
            int away = 0;
            foreach (GoalRow goal in match.Goals.OrderBy(x => x.ModelMinute).ThenBy(x => x.Id))
            {
                string state = ScoreStateResolver.FromScoreDetailed(home, away);
                if (!goalMinutesByState.TryGetValue(state, out List<double>? stateMinutes))
                {
                    stateMinutes = [];
                    goalMinutesByState[state] = stateMinutes;
                }

                stateMinutes.Add(goal.ModelMinute);
                goalMinutesByState[ScoreStateResolver.All].Add(goal.ModelMinute);

                if (goal.IsHome) home++; else away++;
            }
        }

        Dictionary<string, SnapshotRow[]> snapshotsByState = trainingSnapshots
            .GroupBy(x => x.ScoreState)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        snapshotsByState[ScoreStateResolver.All] = trainingSnapshots.ToArray();

        var models = new Dictionary<string, WeibullStateModel>(StringComparer.OrdinalIgnoreCase);
        foreach ((string state, List<double> minutes) in goalMinutesByState)
        {
            if (minutes.Count < Math.Max(5, _options.MinTrainingSnapshots / 2))
                continue;
            if (!snapshotsByState.TryGetValue(state, out SnapshotRow[]? snapshots) || snapshots.Length == 0)
                continue;

            WeibullEstimate estimate = EstimateWeibull(minutes);
            double cdfAtMax = TimingShareCalculator.WeibullCdf(_options.MaxModelMinute, estimate.ShapeK, estimate.ScaleLambda);
            if (cdfAtMax <= 0.0)
                continue;

            double numerator = 0.0;
            double denominator = 0.0;
            foreach (SnapshotRow snapshot in snapshots)
            {
                double survival = TimingShareCalculator.NormalizedWeibullSurvival(snapshot.Minute, estimate.ShapeK, estimate.ScaleLambda, cdfAtMax);
                numerator += snapshot.ActualRemainingGoals * survival;
                denominator += survival * survival;
            }
            if (denominator <= 0.0)
                continue;

            models[state] = new WeibullStateModel
            {
                ScoreState = state,
                ShapeK = estimate.ShapeK,
                ScaleLambda = estimate.ScaleLambda,
                CdfAtMaxMinute = cdfAtMax,
                VolumeScale = numerator / denominator,
                SampleSize = snapshots.Length
            };
        }

        return new TimingWeibullPredictor(models, _options.MaxModelMinute);
    }

    private double ComputeCurrentSeasonVolumeFactor(int baseTrainingGoals, int baseTrainingMatchCount, IReadOnlyCollection<PreparedMatch> priorSameSeasonMatches)
    {
        SeasonVolumeFactorMathResult result = SeasonVolumeFactorMath.Calculate(new SeasonVolumeFactorMathInput
        {
            BaseGoals = baseTrainingGoals,
            BaseMatches = baseTrainingMatchCount,
            CurrentGoals = priorSameSeasonMatches.Sum(x => x.FinalHomeGoals + x.FinalAwayGoals),
            CurrentMatches = priorSameSeasonMatches.Count,
            PriorStrengthMatches = _options.PriorStrengthMatches
        });
        return result.Factor;
    }

    private static double GoalsPerMatchFromSnapshots(IReadOnlyCollection<SnapshotRow> snapshots)
    {
        SnapshotRow[] firstRows = snapshots
            .GroupBy(x => x.SofaScoreEventId)
            .Select(x => x.OrderBy(r => r.Minute).First())
            .ToArray();
        return firstRows.Length == 0 ? 0.0 : firstRows.Average(x => x.ActualRemainingGoals + x.CurrentGoals);
    }

    private async Task<List<PreparedMatch>> LoadPreparedMatchesAsync(IReadOnlyCollection<int> seasonIds, bool applyRoundFilter, CancellationToken cancellationToken)
    {
        IQueryable<MatchEntity> query = _db.Matches.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(_options.League))
            query = query.Where(x => x.LeagueName == _options.League || x.LeagueSlug == _options.League);
        query = query.Where(x => seasonIds.Contains(x.SofaScoreSeasonId));
        if (applyRoundFilter && _options.Rounds.Count > 0)
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

        Dictionary<int, List<MatchEventEntity>> goalsByMatch = goals.GroupBy(x => x.MatchId).ToDictionary(x => x.Key, x => x.ToList());
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

    private List<PreparedMatch> ApplyRoundFilter(IEnumerable<PreparedMatch> matches)
    {
        if (_options.Rounds.Count == 0)
            return matches.ToList();
        return matches.Where(x => _options.Rounds.Contains(x.RoundNumber)).ToList();
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
                rows.Add(new SnapshotRow
                {
                    SofaScoreEventId = match.SofaScoreEventId,
                    SeasonId = match.SeasonId,
                    RoundNumber = match.RoundNumber,
                    MatchName = match.MatchName,
                    Minute = minute,
                    CurrentHomeGoals = currentHome,
                    CurrentAwayGoals = currentAway,
                    FinalGoals = finalTotal,
                    ScoreState = ScoreStateResolver.FromScoreDetailed(currentHome, currentAway),
                    ActualRemainingGoals = Math.Max(0, finalTotal - currentTotal)
                });
            }
        }
        return rows;
    }

    private GoalRow ToGoal(MatchEventEntity goal)
    {
        return new GoalRow { Id = goal.Id, IsHome = goal.IsHome, ModelMinute = ComputeModelMinute(goal) };
    }

    private int ComputeModelMinute(MatchEventEntity goal)
    {
        int minute = goal.TimeSeconds is > 0
            ? Math.Max(1, (int)Math.Ceiling(goal.TimeSeconds.Value / 60.0))
            : Math.Max(1, goal.Minute + Math.Max(0, goal.AddedTime ?? 0));
        return Math.Min(minute, _options.MaxModelMinute);
    }

    private static Dictionary<ModelKey, SnapshotAggregate> BuildAggregates(IEnumerable<SnapshotRow> snapshots, Func<SnapshotRow, ModelKey> keySelector)
    {
        return snapshots.GroupBy(keySelector).ToDictionary(x => x.Key, x => SnapshotAggregate.From(x));
    }

    private static ActualSettlement CalculateActualSettlement(double line, int finalGoals)
    {
        double frac = Math.Round(line - Math.Floor(line), 6);
        int floor = (int)Math.Floor(line);
        if (Math.Abs(frac - 0.5) < 1e-6)
            return finalGoals > line ? new ActualSettlement(1, 0, 0) : new ActualSettlement(0, 0, 1);
        if (Math.Abs(frac) < 1e-6)
        {
            if (finalGoals > floor) return new ActualSettlement(1, 0, 0);
            if (finalGoals == floor) return new ActualSettlement(0, 1, 0);
            return new ActualSettlement(0, 0, 1);
        }
        if (Math.Abs(frac - 0.25) < 1e-6)
        {
            ActualSettlement lower = CalculateActualSettlement(floor, finalGoals);
            ActualSettlement upper = CalculateActualSettlement(floor + 0.5, finalGoals);
            return Average(lower, upper);
        }
        if (Math.Abs(frac - 0.75) < 1e-6)
        {
            ActualSettlement lower = CalculateActualSettlement(floor + 0.5, finalGoals);
            ActualSettlement upper = CalculateActualSettlement(floor + 1.0, finalGoals);
            return Average(lower, upper);
        }
        throw new ArgumentException($"Unsupported total line {line}. Supported: .0, .25, .5, .75 lines.");
    }

    private static ActualSettlement Average(ActualSettlement a, ActualSettlement b)
    {
        return new ActualSettlement((a.OverWinShare + b.OverWinShare) / 2.0, (a.PushShare + b.PushShare) / 2.0, (a.UnderWinShare + b.UnderWinShare) / 2.0);
    }

    private static string ToCsv(IEnumerable<LiveTotalCalibrationPredictionRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SofaScoreEventId,SeasonId,RoundNumber,Match,Minute,CurrentHomeGoals,CurrentAwayGoals,FinalGoals,ScoreState,ActualRemainingGoals,Line,EmpiricalWeight,PredictedRemainingGoals,EmpiricalPrediction,WeibullPrediction,CurrentSeasonVolumeFactor,PredictedOverProbability,PredictedPushProbability,PredictedUnderProbability,ActualOverHit,ActualPushHit,ActualUnderHit,OverBrier,UnderBrier,PredictionSource,TrainingSampleSize");
        foreach (LiveTotalCalibrationPredictionRow row in rows)
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
                row.FinalGoals.ToString(CultureInfo.InvariantCulture),
                row.ScoreState,
                row.ActualRemainingGoals.ToString(CultureInfo.InvariantCulture),
                row.Line.ToString("0.##", CultureInfo.InvariantCulture),
                row.EmpiricalWeight.ToString("0.######", CultureInfo.InvariantCulture),
                row.PredictedRemainingGoals.ToString("0.######", CultureInfo.InvariantCulture),
                row.EmpiricalPrediction.ToString("0.######", CultureInfo.InvariantCulture),
                row.WeibullPrediction.ToString("0.######", CultureInfo.InvariantCulture),
                row.CurrentSeasonVolumeFactor.ToString("0.######", CultureInfo.InvariantCulture),
                row.PredictedOverProbability.ToString("0.######", CultureInfo.InvariantCulture),
                row.PredictedPushProbability.ToString("0.######", CultureInfo.InvariantCulture),
                row.PredictedUnderProbability.ToString("0.######", CultureInfo.InvariantCulture),
                row.ActualOverHit.ToString("0.######", CultureInfo.InvariantCulture),
                row.ActualPushHit.ToString("0.######", CultureInfo.InvariantCulture),
                row.ActualUnderHit.ToString("0.######", CultureInfo.InvariantCulture),
                row.OverBrier.ToString("0.######", CultureInfo.InvariantCulture),
                row.UnderBrier.ToString("0.######", CultureInfo.InvariantCulture),
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

    private static string WeightLabel(double weight) => "w=" + weight.ToString("0.##", CultureInfo.InvariantCulture);

    private static WeibullEstimate EstimateWeibull(IReadOnlyList<double> values)
    {
        double[] x = values.Where(v => v > 0).ToArray();
        if (x.Length == 0) return new WeibullEstimate(1.0, 1.0);
        double meanLog = x.Select(v => Math.Log(v)).Average();
        double varianceLog = x.Select(v => Math.Pow(Math.Log(v) - meanLog, 2)).Average();
        double k = varianceLog > 0 ? Math.PI / Math.Sqrt(6.0 * varianceLog) : 1.5;
        k = Math.Clamp(k, 0.15, 10.0);
        for (int i = 0; i < 100; i++)
        {
            double f = ShapeEquation(k, x, meanLog);
            double step = Math.Max(1e-5, k * 1e-5);
            double lowK = Math.Max(0.05, k - step);
            double derivative = (ShapeEquation(k + step, x, meanLog) - ShapeEquation(lowK, x, meanLog)) / ((k + step) - lowK);
            if (Math.Abs(derivative) < 1e-12) break;
            double next = k - f / derivative;
            if (double.IsNaN(next) || double.IsInfinity(next) || next <= 0) next = k / 2.0;
            next = Math.Clamp(next, 0.05, 25.0);
            if (Math.Abs(next - k) < 1e-9) { k = next; break; }
            k = next;
        }
        double lambda = Math.Pow(x.Select(v => Math.Pow(v, k)).Average(), 1.0 / k);
        return new WeibullEstimate(k, lambda);
    }

    private static double ShapeEquation(double k, IReadOnlyList<double> x, double meanLog)
    {
        double sumXk = 0.0;
        double sumXkLogX = 0.0;
        foreach (double value in x)
        {
            double xk = Math.Pow(value, k);
            sumXk += xk;
            sumXkLogX += xk * Math.Log(value);
        }
        return (1.0 / k) + meanLog - (sumXkLogX / sumXk);
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
        public int CurrentGoals => CurrentHomeGoals + CurrentAwayGoals;
        public int FinalGoals { get; set; }
        public string ScoreState { get; set; } = string.Empty;
        public int ActualRemainingGoals { get; set; }
    }

    private sealed class WeibullStateModel
    {
        public string ScoreState { get; set; } = string.Empty;
        public double ShapeK { get; set; }
        public double ScaleLambda { get; set; }
        public double CdfAtMaxMinute { get; set; }
        public double VolumeScale { get; set; }
        public int SampleSize { get; set; }
    }

    private sealed class TimingWeibullPredictor
    {
        private readonly IReadOnlyDictionary<string, WeibullStateModel> _models;
        private readonly int _maxModelMinute;
        public TimingWeibullPredictor(IReadOnlyDictionary<string, WeibullStateModel> models, int maxModelMinute)
        {
            _models = models;
            _maxModelMinute = maxModelMinute;
        }
        public Prediction Predict(SnapshotRow snapshot)
        {
            WeibullStateModel? model = null;
            foreach (string candidate in ScoreStateResolver.FallbackCandidates(snapshot.ScoreState))
            {
                if (_models.TryGetValue(candidate, out model)) break;
            }
            if (model is null)
                return new Prediction(0.0, "weibull:none", 0);
            double survival = TimingShareCalculator.NormalizedWeibullSurvival(Math.Min(snapshot.Minute, _maxModelMinute), model.ShapeK, model.ScaleLambda, model.CdfAtMaxMinute);
            return new Prediction(model.VolumeScale * survival, $"weibull:{model.ScoreState}:k={model.ShapeK.ToString("0.###", CultureInfo.InvariantCulture)}", model.SampleSize);
        }
    }

    private sealed class SnapshotAggregate
    {
        public int Count { get; set; }
        public double SumRemainingGoals { get; set; }
        public double AvgRemainingGoals => Count == 0 ? 0.0 : SumRemainingGoals / Count;
        public static SnapshotAggregate From(IEnumerable<SnapshotRow> snapshots)
        {
            SnapshotRow[] rows = snapshots.ToArray();
            return new SnapshotAggregate { Count = rows.Length, SumRemainingGoals = rows.Sum(x => x.ActualRemainingGoals) };
        }
    }

    private readonly record struct ModelKey(int Minute, string ScoreState);
    private readonly record struct Prediction(double ExpectedRemainingGoals, string Source, int SampleSize);
    private readonly record struct WeibullEstimate(double ShapeK, double ScaleLambda);
    private readonly record struct ActualSettlement(double OverWinShare, double PushShare, double UnderWinShare);
}
