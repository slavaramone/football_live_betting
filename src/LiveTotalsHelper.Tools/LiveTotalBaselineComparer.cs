using System.Globalization;
using System.Text;
using LiveTotalsHelper.Modeling;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalBaselineComparisonOptions
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string MinuteOutputPath { get; set; } = string.Empty;
    public string ScoreStateOutputPath { get; set; } = string.Empty;
    public string LineOutputPath { get; set; } = string.Empty;
    public string CalibrationOutputPath { get; set; } = string.Empty;
    public List<int> TrainingSeasonIds { get; } = [];
    public List<int> TestSeasonIds { get; } = [];
    public List<double> TargetLines { get; } = [0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0];
    public string DecisionScope { get; set; } = LiveTotalDecisionScope.FullModel;
    public bool CompareScopes { get; set; }
    public int MinBucketRows { get; set; } = 80;
    public int MinBucketMatches { get; set; } = 40;
    public int CorrectionShrinkRows { get; set; } = 100;
    public double MinCorrectionFactor { get; set; } = 0.50;
    public double MaxCorrectionFactor { get; set; } = 1.75;
    public int MaxRemainingGoals { get; set; } = 8;
    public double Smoothing { get; set; } = 0.25;
}

public sealed class LiveTotalBaselineComparisonResult
{
    public string InputPath { get; set; } = string.Empty;
    public string SummaryOutputPath { get; set; } = string.Empty;
    public string MinuteOutputPath { get; set; } = string.Empty;
    public string ScoreStateOutputPath { get; set; } = string.Empty;
    public string LineOutputPath { get; set; } = string.Empty;
    public string CalibrationOutputPath { get; set; } = string.Empty;
    public int RowsRead { get; set; }
    public int TrainingRows { get; set; }
    public int TestRows { get; set; }
    public int RemainingObservations { get; set; }
    public int LineObservations { get; set; }
    public double TrainingAverageFinalGoals { get; set; }
    public double EmpiricalStateVolumeGlobalFactor { get; set; }
    public int EmpiricalStateVolumeCorrectionBuckets { get; set; }
    public int EmpiricalStateVolumeUsableCorrectionBuckets { get; set; }
    public List<string> ScopesEvaluated { get; } = [];
    public List<LiveTotalBaselineSummaryRow> Summaries { get; } = [];
    public List<LiveTotalBaselineGroupedRow> MinuteRows { get; } = [];
    public List<LiveTotalBaselineGroupedRow> ScoreStateRows { get; } = [];
    public List<LiveTotalBaselineLineRow> LineRows { get; } = [];
    public List<LiveTotalBaselineCalibrationRow> CalibrationRows { get; } = [];
}

public class LiveTotalBaselineSummaryRow
{
    public string Method { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string StateTrigger { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double AveragePredictedRemaining { get; set; }
    public double AverageActualRemaining { get; set; }
    public double Mae { get; set; }
    public double Rmse { get; set; }
    public double Bias { get; set; }
    public int LineRows { get; set; }
    public int LineMatches { get; set; }
    public double AverageProbability { get; set; }
    public double ActualOverRate { get; set; }
    public double Brier { get; set; }
    public double LogLoss { get; set; }
    public double DirectionAccuracy { get; set; }
}

public sealed class LiveTotalBaselineGroupedRow : LiveTotalBaselineSummaryRow
{
    public string GroupKey { get; set; } = string.Empty;
}

public sealed class LiveTotalBaselineLineRow
{
    public string Method { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string StateTrigger { get; set; } = string.Empty;
    public double Line { get; set; }
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double AverageProbability { get; set; }
    public double ActualOverRate { get; set; }
    public double Brier { get; set; }
    public double LogLoss { get; set; }
    public double DirectionAccuracy { get; set; }
}

public sealed class LiveTotalBaselineCalibrationRow
{
    public string Method { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string StateTrigger { get; set; } = string.Empty;
    public double Line { get; set; }
    public string ProbabilityBucket { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double AverageProbability { get; set; }
    public double ActualOverRate { get; set; }
    public double Brier { get; set; }
}

public sealed class LiveTotalBaselineComparer
{
    private const string RawPoisson = "RawPoisson";
    private const string RawEmpirical = "RawEmpirical";
    private const string EmpiricalStateVolume = "EmpiricalStateVolume";

    private readonly LiveTotalBaselineComparisonOptions _options;

    public LiveTotalBaselineComparer(LiveTotalBaselineComparisonOptions options)
    {
        _options = options;
    }

    public async Task<LiveTotalBaselineComparisonResult> CompareAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        List<InputRow> rows = await ReadRowsAsync(_options.InputPath, cancellationToken);
        List<InputRow> trainingRows = rows.Where(x => _options.TrainingSeasonIds.Contains(x.SeasonId)).ToList();
        List<InputRow> testRows = rows.Where(x => _options.TestSeasonIds.Contains(x.SeasonId)).ToList();

        if (trainingRows.Count == 0)
            throw new ArgumentException("No rows matched --training-season-ids.");
        if (testRows.Count == 0)
            throw new ArgumentException("No rows matched --test-season-ids.");

        double trainingAverageFinalGoals = trainingRows
            .GroupBy(x => x.MatchId)
            .Select(g => g.First().ActualFinalTotalGoals)
            .Average();

        EmpiricalRemainingGoalsModel empiricalModel = BuildEmpiricalModel(trainingRows);
        EmpiricalStateVolumeCorrectionModel empiricalCorrectionModel = BuildEmpiricalStateVolumeCorrectionModel(trainingRows, empiricalModel);
        string[] scopes = _options.CompareScopes
            ? LiveTotalDecisionScope.ComparisonScopes
            : [LiveTotalDecisionScope.Normalize(_options.DecisionScope)];

        var remainingObservations = new List<RemainingObservation>();
        var lineObservations = new List<LineObservation>();

        foreach (InputRow row in testRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (string scope in scopes)
            {
                if (!LiveTotalDecisionScope.IsEligible(scope, row.StateTrigger, row.Minute))
                    continue;

                double poissonRemaining = Math.Max(0.0, trainingAverageFinalGoals * row.TimingRemainingShare);
                AddMethodObservations(
                    remainingObservations,
                    lineObservations,
                    row,
                    scope,
                    RawPoisson,
                    poissonRemaining,
                    distribution: null);

                EmpiricalResolution empirical = empiricalModel.Resolve(row);
                if (empirical.IsSupported)
                {
                    AddMethodObservations(
                        remainingObservations,
                        lineObservations,
                        row,
                        scope,
                        RawEmpirical,
                        empirical.AverageRemainingGoals,
                        empirical.Probabilities);

                    EmpiricalStateVolumeCorrectionResolution correction = empiricalCorrectionModel.Resolve(row);
                    double correctedRemaining = Math.Max(0.0, empirical.AverageRemainingGoals * correction.Factor);
                    AddMethodObservations(
                        remainingObservations,
                        lineObservations,
                        row,
                        scope,
                        EmpiricalStateVolume,
                        correctedRemaining,
                        empirical.Probabilities,
                        distributionTargetMean: correctedRemaining);
                }
            }
        }

        var result = new LiveTotalBaselineComparisonResult
        {
            InputPath = _options.InputPath,
            SummaryOutputPath = ResolveSummaryOutputPath(),
            MinuteOutputPath = ResolveCompanionOutputPath(_options.MinuteOutputPath, "by-minute"),
            ScoreStateOutputPath = ResolveCompanionOutputPath(_options.ScoreStateOutputPath, "by-score-state"),
            LineOutputPath = ResolveCompanionOutputPath(_options.LineOutputPath, "by-line"),
            CalibrationOutputPath = ResolveCompanionOutputPath(_options.CalibrationOutputPath, "calibration-buckets"),
            RowsRead = rows.Count,
            TrainingRows = trainingRows.Count,
            TestRows = testRows.Count,
            RemainingObservations = remainingObservations.Count,
            LineObservations = lineObservations.Count,
            TrainingAverageFinalGoals = trainingAverageFinalGoals,
            EmpiricalStateVolumeGlobalFactor = empiricalCorrectionModel.GlobalFactor,
            EmpiricalStateVolumeCorrectionBuckets = empiricalCorrectionModel.BucketCount,
            EmpiricalStateVolumeUsableCorrectionBuckets = empiricalCorrectionModel.UsableBucketCount
        };
        result.ScopesEvaluated.AddRange(scopes);

        result.Summaries.AddRange(BuildSummaryRows(remainingObservations, lineObservations));
        result.MinuteRows.AddRange(BuildGroupedRows(remainingObservations, lineObservations, x => x.MinuteBand));
        result.ScoreStateRows.AddRange(BuildGroupedRows(remainingObservations, lineObservations, x => x.DetailedScoreState));
        result.LineRows.AddRange(BuildLineRows(lineObservations));
        result.CalibrationRows.AddRange(BuildCalibrationRows(lineObservations));

        await WriteAsync(result.SummaryOutputPath, ToSummaryCsv(result.Summaries), cancellationToken);
        await WriteAsync(result.MinuteOutputPath, ToGroupedCsv(result.MinuteRows, "MinuteBand"), cancellationToken);
        await WriteAsync(result.ScoreStateOutputPath, ToGroupedCsv(result.ScoreStateRows, "DetailedScoreState"), cancellationToken);
        await WriteAsync(result.LineOutputPath, ToLineCsv(result.LineRows), cancellationToken);
        await WriteAsync(result.CalibrationOutputPath, ToCalibrationCsv(result.CalibrationRows), cancellationToken);

        return result;
    }

    private void AddMethodObservations(
        List<RemainingObservation> remainingObservations,
        List<LineObservation> lineObservations,
        InputRow row,
        string scope,
        string method,
        double predictedRemaining,
        IReadOnlyDictionary<int, double>? distribution,
        double? distributionTargetMean = null)
    {
        var remaining = new RemainingObservation
        {
            Method = method,
            Scope = scope,
            StateTrigger = row.StateTrigger,
            MinuteBand = MinuteBand(row.StateTrigger, row.Minute),
            DetailedScoreState = row.DetailedScoreState,
            MatchId = row.MatchId,
            PredictedRemainingGoals = predictedRemaining,
            ActualRemainingGoals = row.ActualRemainingGoals
        };
        remainingObservations.Add(remaining);

        foreach (double line in _options.TargetLines.Distinct().OrderBy(x => x))
        {
            bool? actualOver = TryActualOver(line, row.ActualFinalTotalGoals);
            if (!actualOver.HasValue)
                continue;

            double? probability = distribution is null
                ? TryNoPushOverProbability(line, row.CurrentTotalGoals, predictedRemaining)
                : TryNoPushOverProbability(line, row.CurrentTotalGoals, distribution, distributionTargetMean);

            if (!probability.HasValue)
                continue;

            lineObservations.Add(new LineObservation
            {
                Method = method,
                Scope = scope,
                StateTrigger = row.StateTrigger,
                MinuteBand = remaining.MinuteBand,
                DetailedScoreState = row.DetailedScoreState,
                MatchId = row.MatchId,
                Line = line,
                Probability = probability.Value,
                ActualOver = actualOver.Value
            });
        }
    }

    private EmpiricalRemainingGoalsModel BuildEmpiricalModel(IReadOnlyCollection<InputRow> trainingRows)
    {
        var model = new EmpiricalRemainingGoalsModel(_options.MaxRemainingGoals, _options.Smoothing);
        var bandedRows = trainingRows
            .Select(x => new { Row = x, MinuteBand = MinuteBand(x.StateTrigger, x.Minute) })
            .Where(x => !string.IsNullOrWhiteSpace(x.MinuteBand))
            .ToList();

        foreach (var group in bandedRows.GroupBy(x => new { x.Row.StateTrigger, x.MinuteBand, x.Row.DetailedScoreState, x.Row.CurrentTotalGoals }, x => x.Row))
            model.AddBucket("Exact", group.Key.StateTrigger, group.Key.MinuteBand, group.Key.DetailedScoreState, group.Key.CurrentTotalGoals, group.ToList(), _options.MinBucketRows, _options.MinBucketMatches, forceUsable: false);

        foreach (var group in bandedRows.GroupBy(x => new { x.Row.StateTrigger, x.MinuteBand, x.Row.DetailedScoreState }, x => x.Row))
            model.AddBucket("ScoreState", group.Key.StateTrigger, group.Key.MinuteBand, group.Key.DetailedScoreState, null, group.ToList(), _options.MinBucketRows, _options.MinBucketMatches, forceUsable: false);

        foreach (var group in bandedRows.GroupBy(x => new { x.Row.StateTrigger, x.MinuteBand }, x => x.Row))
            model.AddBucket("TriggerBand", group.Key.StateTrigger, group.Key.MinuteBand, string.Empty, null, group.ToList(), _options.MinBucketRows, _options.MinBucketMatches, forceUsable: false);

        foreach (IGrouping<string, InputRow> group in bandedRows.GroupBy(x => x.Row.StateTrigger, x => x.Row))
            model.AddBucket("Trigger", group.Key, string.Empty, string.Empty, null, group.ToList(), _options.MinBucketRows, _options.MinBucketMatches, forceUsable: false);

        model.AddBucket("Global", string.Empty, string.Empty, string.Empty, null, bandedRows.Select(x => x.Row).ToList(), _options.MinBucketRows, _options.MinBucketMatches, forceUsable: true);
        return model;
    }

    private EmpiricalStateVolumeCorrectionModel BuildEmpiricalStateVolumeCorrectionModel(IReadOnlyCollection<InputRow> trainingRows, EmpiricalRemainingGoalsModel empiricalModel)
    {
        var correctionRows = trainingRows
            .Select(row => new { Row = row, Empirical = empiricalModel.Resolve(row) })
            .Where(x => x.Empirical.IsSupported && x.Empirical.AverageRemainingGoals > 1e-9)
            .Select(x => new EmpiricalStateVolumeCorrectionTrainingRow
            {
                Row = x.Row,
                MinuteBand = MinuteBand(x.Row.StateTrigger, x.Row.Minute),
                PredictedRemainingGoals = x.Empirical.AverageRemainingGoals,
                ActualRemainingGoals = x.Row.ActualRemainingGoals
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.MinuteBand))
            .ToList();

        var model = new EmpiricalStateVolumeCorrectionModel(_options.MinCorrectionFactor, _options.MaxCorrectionFactor);

        foreach (var group in correctionRows.GroupBy(x => new { x.Row.StateTrigger, x.MinuteBand, x.Row.DetailedScoreState, x.Row.CurrentTotalGoals }))
            model.AddBucket("Exact", group.Key.StateTrigger, group.Key.MinuteBand, group.Key.DetailedScoreState, group.Key.CurrentTotalGoals, group.ToList(), _options.MinBucketRows, _options.MinBucketMatches, _options.CorrectionShrinkRows, forceUsable: false);

        foreach (var group in correctionRows.GroupBy(x => new { x.Row.StateTrigger, x.MinuteBand, x.Row.DetailedScoreState }))
            model.AddBucket("ScoreState", group.Key.StateTrigger, group.Key.MinuteBand, group.Key.DetailedScoreState, null, group.ToList(), _options.MinBucketRows, _options.MinBucketMatches, _options.CorrectionShrinkRows, forceUsable: false);

        foreach (var group in correctionRows.GroupBy(x => new { x.Row.StateTrigger, x.MinuteBand }))
            model.AddBucket("TriggerBand", group.Key.StateTrigger, group.Key.MinuteBand, string.Empty, null, group.ToList(), _options.MinBucketRows, _options.MinBucketMatches, _options.CorrectionShrinkRows, forceUsable: false);

        foreach (IGrouping<string, EmpiricalStateVolumeCorrectionTrainingRow> group in correctionRows.GroupBy(x => x.Row.StateTrigger))
            model.AddBucket("Trigger", group.Key, string.Empty, string.Empty, null, group.ToList(), _options.MinBucketRows, _options.MinBucketMatches, _options.CorrectionShrinkRows, forceUsable: false);

        model.AddBucket("Global", string.Empty, string.Empty, string.Empty, null, correctionRows, _options.MinBucketRows, _options.MinBucketMatches, _options.CorrectionShrinkRows, forceUsable: true);
        model.FinalizeGlobalFactor();
        return model;
    }

    private static List<LiveTotalBaselineSummaryRow> BuildSummaryRows(
        IReadOnlyCollection<RemainingObservation> remainingObservations,
        IReadOnlyCollection<LineObservation> lineObservations)
    {
        var result = new List<LiveTotalBaselineSummaryRow>();
        foreach (var group in remainingObservations
            .SelectMany(x => new[]
            {
                new { Key = new SummaryKey(x.Method, x.Scope, "All"), Row = x },
                new { Key = new SummaryKey(x.Method, x.Scope, x.StateTrigger), Row = x }
            })
            .GroupBy(x => x.Key)
            .OrderBy(x => MethodOrder(x.Key.Method))
            .ThenBy(x => LiveTotalDecisionScope.Order(x.Key.Scope))
            .ThenBy(x => TriggerOrder(x.Key.StateTrigger)))
        {
            List<RemainingObservation> remRows = group.Select(x => x.Row).ToList();
            List<LineObservation> lineRows = lineObservations
                .Where(x => x.Method == group.Key.Method && x.Scope == group.Key.Scope && (group.Key.StateTrigger == "All" || x.StateTrigger == group.Key.StateTrigger))
                .ToList();
            result.Add(BuildSummary(group.Key.Method, group.Key.Scope, group.Key.StateTrigger, remRows, lineRows));
        }

        return result;
    }

    private static List<LiveTotalBaselineGroupedRow> BuildGroupedRows(
        IReadOnlyCollection<RemainingObservation> remainingObservations,
        IReadOnlyCollection<LineObservation> lineObservations,
        Func<RemainingObservation, string> groupSelector)
    {
        var result = new List<LiveTotalBaselineGroupedRow>();
        foreach (var group in remainingObservations
            .Select(x => new { Key = new GroupedKey(x.Method, x.Scope, x.StateTrigger, groupSelector(x)), Row = x })
            .GroupBy(x => x.Key)
            .OrderBy(x => MethodOrder(x.Key.Method))
            .ThenBy(x => LiveTotalDecisionScope.Order(x.Key.Scope))
            .ThenBy(x => TriggerOrder(x.Key.StateTrigger))
            .ThenBy(x => x.Key.GroupKey))
        {
            List<RemainingObservation> remRows = group.Select(x => x.Row).ToList();
            List<LineObservation> lineRows = lineObservations
                .Where(x => x.Method == group.Key.Method && x.Scope == group.Key.Scope && x.StateTrigger == group.Key.StateTrigger && groupSelector(new RemainingObservation { MinuteBand = x.MinuteBand, DetailedScoreState = x.DetailedScoreState }) == group.Key.GroupKey)
                .ToList();
            LiveTotalBaselineSummaryRow summary = BuildSummary(group.Key.Method, group.Key.Scope, group.Key.StateTrigger, remRows, lineRows);
            result.Add(new LiveTotalBaselineGroupedRow
            {
                GroupKey = group.Key.GroupKey,
                Method = summary.Method,
                Scope = summary.Scope,
                StateTrigger = summary.StateTrigger,
                Rows = summary.Rows,
                Matches = summary.Matches,
                AveragePredictedRemaining = summary.AveragePredictedRemaining,
                AverageActualRemaining = summary.AverageActualRemaining,
                Mae = summary.Mae,
                Rmse = summary.Rmse,
                Bias = summary.Bias,
                LineRows = summary.LineRows,
                LineMatches = summary.LineMatches,
                AverageProbability = summary.AverageProbability,
                ActualOverRate = summary.ActualOverRate,
                Brier = summary.Brier,
                LogLoss = summary.LogLoss,
                DirectionAccuracy = summary.DirectionAccuracy
            });
        }

        return result;
    }

    private static List<LiveTotalBaselineLineRow> BuildLineRows(IReadOnlyCollection<LineObservation> lineObservations)
    {
        return lineObservations
            .SelectMany(x => new[]
            {
                new { Key = new LineKey(x.Method, x.Scope, "All", x.Line), Row = x },
                new { Key = new LineKey(x.Method, x.Scope, x.StateTrigger, x.Line), Row = x }
            })
            .GroupBy(x => x.Key)
            .OrderBy(x => MethodOrder(x.Key.Method))
            .ThenBy(x => LiveTotalDecisionScope.Order(x.Key.Scope))
            .ThenBy(x => TriggerOrder(x.Key.StateTrigger))
            .ThenBy(x => x.Key.Line)
            .Select(x => BuildLineRow(x.Key.Method, x.Key.Scope, x.Key.StateTrigger, x.Key.Line, x.Select(y => y.Row).ToList()))
            .ToList();
    }

    private static List<LiveTotalBaselineCalibrationRow> BuildCalibrationRows(IReadOnlyCollection<LineObservation> lineObservations)
    {
        return lineObservations
            .Select(x => new { Key = new CalibrationKey(x.Method, x.Scope, x.StateTrigger, x.Line, ProbabilityBucket(x.Probability)), Row = x })
            .GroupBy(x => x.Key)
            .OrderBy(x => MethodOrder(x.Key.Method))
            .ThenBy(x => LiveTotalDecisionScope.Order(x.Key.Scope))
            .ThenBy(x => TriggerOrder(x.Key.StateTrigger))
            .ThenBy(x => x.Key.Line)
            .ThenBy(x => ProbabilityBucketOrder(x.Key.ProbabilityBucket))
            .Select(x => BuildCalibrationRow(x.Key.Method, x.Key.Scope, x.Key.StateTrigger, x.Key.Line, x.Key.ProbabilityBucket, x.Select(y => y.Row).ToList()))
            .ToList();
    }

    private static LiveTotalBaselineSummaryRow BuildSummary(string method, string scope, string trigger, IReadOnlyCollection<RemainingObservation> remRows, IReadOnlyCollection<LineObservation> lineRows)
    {
        LiveTotalBaselineSummaryRow summary = new()
        {
            Method = method,
            Scope = scope,
            StateTrigger = trigger,
            Rows = remRows.Count,
            Matches = remRows.Select(x => x.MatchId).Distinct().Count(),
            AveragePredictedRemaining = remRows.Count == 0 ? 0.0 : remRows.Average(x => x.PredictedRemainingGoals),
            AverageActualRemaining = remRows.Count == 0 ? 0.0 : remRows.Average(x => x.ActualRemainingGoals),
            Mae = remRows.Count == 0 ? 0.0 : remRows.Average(x => Math.Abs(x.PredictedRemainingGoals - x.ActualRemainingGoals)),
            Rmse = remRows.Count == 0 ? 0.0 : Math.Sqrt(remRows.Average(x => Squared(x.PredictedRemainingGoals - x.ActualRemainingGoals))),
            Bias = remRows.Count == 0 ? 0.0 : remRows.Average(x => x.PredictedRemainingGoals - x.ActualRemainingGoals),
            LineRows = lineRows.Count,
            LineMatches = lineRows.Select(x => x.MatchId).Distinct().Count(),
            AverageProbability = lineRows.Count == 0 ? 0.0 : lineRows.Average(x => x.Probability),
            ActualOverRate = lineRows.Count == 0 ? 0.0 : lineRows.Average(x => BoolToDouble(x.ActualOver)),
            Brier = lineRows.Count == 0 ? 0.0 : lineRows.Average(x => Squared(x.Probability - BoolToDouble(x.ActualOver))),
            LogLoss = lineRows.Count == 0 ? 0.0 : lineRows.Average(x => LogLoss(x.Probability, x.ActualOver)),
            DirectionAccuracy = lineRows.Count == 0 ? 0.0 : lineRows.Average(x => (x.Probability >= 0.5) == x.ActualOver ? 1.0 : 0.0)
        };
        return summary;
    }

    private static LiveTotalBaselineLineRow BuildLineRow(string method, string scope, string trigger, double line, IReadOnlyCollection<LineObservation> rows)
    {
        return new LiveTotalBaselineLineRow
        {
            Method = method,
            Scope = scope,
            StateTrigger = trigger,
            Line = line,
            Rows = rows.Count,
            Matches = rows.Select(x => x.MatchId).Distinct().Count(),
            AverageProbability = rows.Count == 0 ? 0.0 : rows.Average(x => x.Probability),
            ActualOverRate = rows.Count == 0 ? 0.0 : rows.Average(x => BoolToDouble(x.ActualOver)),
            Brier = rows.Count == 0 ? 0.0 : rows.Average(x => Squared(x.Probability - BoolToDouble(x.ActualOver))),
            LogLoss = rows.Count == 0 ? 0.0 : rows.Average(x => LogLoss(x.Probability, x.ActualOver)),
            DirectionAccuracy = rows.Count == 0 ? 0.0 : rows.Average(x => (x.Probability >= 0.5) == x.ActualOver ? 1.0 : 0.0)
        };
    }

    private static LiveTotalBaselineCalibrationRow BuildCalibrationRow(string method, string scope, string trigger, double line, string bucket, IReadOnlyCollection<LineObservation> rows)
    {
        return new LiveTotalBaselineCalibrationRow
        {
            Method = method,
            Scope = scope,
            StateTrigger = trigger,
            Line = line,
            ProbabilityBucket = bucket,
            Rows = rows.Count,
            Matches = rows.Select(x => x.MatchId).Distinct().Count(),
            AverageProbability = rows.Count == 0 ? 0.0 : rows.Average(x => x.Probability),
            ActualOverRate = rows.Count == 0 ? 0.0 : rows.Average(x => BoolToDouble(x.ActualOver)),
            Brier = rows.Count == 0 ? 0.0 : rows.Average(x => Squared(x.Probability - BoolToDouble(x.ActualOver)))
        };
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.InputPath))
            throw new ArgumentException("Missing required argument --input.");
        if (!File.Exists(_options.InputPath))
            throw new FileNotFoundException("Live total calibration dataset CSV was not found.", _options.InputPath);
        if (_options.TrainingSeasonIds.Count == 0)
            throw new ArgumentException("Missing required argument --training-season-ids.");
        if (_options.TestSeasonIds.Count == 0)
            throw new ArgumentException("Missing required argument --test-season-ids.");
        if (_options.TargetLines.Count == 0)
            throw new ArgumentException("At least one target line is required.");
        if (_options.MinBucketRows < 1)
            throw new ArgumentException("--min-bucket-rows must be >= 1.");
        if (_options.MinBucketMatches < 1)
            throw new ArgumentException("--min-bucket-matches must be >= 1.");
        if (_options.CorrectionShrinkRows < 0)
            throw new ArgumentException("--correction-shrink-rows must be >= 0.");
        if (_options.MinCorrectionFactor <= 0 || _options.MaxCorrectionFactor <= 0 || _options.MinCorrectionFactor > _options.MaxCorrectionFactor)
            throw new ArgumentException("--min-correction-factor and --max-correction-factor must be positive and min <= max.");
        if (_options.MaxRemainingGoals < 1)
            throw new ArgumentException("--max-remaining-goals must be >= 1.");
        if (_options.Smoothing < 0)
            throw new ArgumentException("--smoothing must be >= 0.");
        _ = LiveTotalDecisionScope.Normalize(_options.DecisionScope);
    }

    private string ResolveSummaryOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
            return _options.OutputPath;

        string directory = Path.GetDirectoryName(_options.InputPath) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(_options.InputPath);
        return Path.Combine(directory, $"{fileName}-baseline-comparison-summary.csv");
    }

    private string ResolveCompanionOutputPath(string configured, string suffix)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        string summary = ResolveSummaryOutputPath();
        string directory = Path.GetDirectoryName(summary) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(summary);
        const string summarySuffix = "-summary";
        if (fileName.EndsWith(summarySuffix, StringComparison.OrdinalIgnoreCase))
            fileName = fileName[..^summarySuffix.Length];

        return Path.Combine(directory, $"{fileName}-{suffix}.csv");
    }

    private static async Task WriteAsync(string path, string text, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        await File.WriteAllTextAsync(path, text, Encoding.UTF8, cancellationToken);
    }

    private static async Task<List<InputRow>> ReadRowsAsync(string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        List<List<string>> records = ParseCsv(text);
        if (records.Count == 0)
            return [];

        string[] headers = records[0].Select(x => x.Trim()).ToArray();
        var index = headers.Select((name, position) => new { name, position })
            .ToDictionary(x => x.name, x => x.position, StringComparer.OrdinalIgnoreCase);

        foreach (string required in new[]
        {
            "SeasonId", "MatchId", "StateTrigger", "Minute", "HomeGoals", "AwayGoals", "CurrentTotalGoals",
            "ScoreState", "DetailedScoreState", "TimingRemainingShare", "ActualFinalTotalGoals", "ActualRemainingGoals"
        })
        {
            if (!index.ContainsKey(required))
                throw new ArgumentException($"Input CSV is missing required column '{required}'. Rebuild the calibration dataset with the latest builder.");
        }

        var rows = new List<InputRow>();
        foreach (List<string> record in records.Skip(1))
        {
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0]))
                continue;

            if (!TryGetInt(record, index, "SeasonId", out int seasonId) ||
                !TryGetInt(record, index, "MatchId", out int matchId) ||
                !TryGetInt(record, index, "Minute", out int minute) ||
                !TryGetInt(record, index, "HomeGoals", out int homeGoals) ||
                !TryGetInt(record, index, "AwayGoals", out int awayGoals) ||
                !TryGetInt(record, index, "CurrentTotalGoals", out int currentTotalGoals) ||
                !TryGetDouble(record, index, "TimingRemainingShare", out double timingRemainingShare) ||
                !TryGetInt(record, index, "ActualFinalTotalGoals", out int actualFinalTotalGoals) ||
                !TryGetInt(record, index, "ActualRemainingGoals", out int actualRemainingGoals))
                continue;

            bool isReliable = !index.ContainsKey("IsReliableMatch") || GetString(record, index, "IsReliableMatch") is not "0";
            if (!isReliable)
                continue;

            rows.Add(new InputRow
            {
                SeasonId = seasonId,
                MatchId = matchId,
                StateTrigger = LiveTotalStateTrigger.Normalize(GetString(record, index, "StateTrigger")),
                Minute = minute,
                HomeGoals = homeGoals,
                AwayGoals = awayGoals,
                CurrentTotalGoals = currentTotalGoals,
                ScoreState = GetString(record, index, "ScoreState"),
                DetailedScoreState = GetString(record, index, "DetailedScoreState"),
                TimingRemainingShare = timingRemainingShare,
                ActualFinalTotalGoals = actualFinalTotalGoals,
                ActualRemainingGoals = actualRemainingGoals
            });
        }

        return rows;
    }

    private static string ToSummaryCsv(IReadOnlyCollection<LiveTotalBaselineSummaryRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Method,Scope,StateTrigger,Rows,Matches,AveragePredictedRemaining,AverageActualRemaining,Mae,Rmse,Bias,LineRows,LineMatches,AverageProbability,ActualOverRate,Brier,LogLoss,DirectionAccuracy");
        foreach (LiveTotalBaselineSummaryRow row in rows)
        {
            sb.AppendLine(string.Join(',', EscapeCsv(row.Method), EscapeCsv(row.Scope), EscapeCsv(row.StateTrigger), I(row.Rows), I(row.Matches), D(row.AveragePredictedRemaining), D(row.AverageActualRemaining), D(row.Mae), D(row.Rmse), D(row.Bias), I(row.LineRows), I(row.LineMatches), D(row.AverageProbability), D(row.ActualOverRate), D(row.Brier), D(row.LogLoss), D(row.DirectionAccuracy)));
        }
        return sb.ToString();
    }

    private static string ToGroupedCsv(IReadOnlyCollection<LiveTotalBaselineGroupedRow> rows, string groupColumnName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Method,Scope,StateTrigger,{groupColumnName},Rows,Matches,AveragePredictedRemaining,AverageActualRemaining,Mae,Rmse,Bias,LineRows,LineMatches,AverageProbability,ActualOverRate,Brier,LogLoss,DirectionAccuracy");
        foreach (LiveTotalBaselineGroupedRow row in rows)
        {
            sb.AppendLine(string.Join(',', EscapeCsv(row.Method), EscapeCsv(row.Scope), EscapeCsv(row.StateTrigger), EscapeCsv(row.GroupKey), I(row.Rows), I(row.Matches), D(row.AveragePredictedRemaining), D(row.AverageActualRemaining), D(row.Mae), D(row.Rmse), D(row.Bias), I(row.LineRows), I(row.LineMatches), D(row.AverageProbability), D(row.ActualOverRate), D(row.Brier), D(row.LogLoss), D(row.DirectionAccuracy)));
        }
        return sb.ToString();
    }

    private static string ToLineCsv(IReadOnlyCollection<LiveTotalBaselineLineRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Method,Scope,StateTrigger,Line,Rows,Matches,AverageProbability,ActualOverRate,Brier,LogLoss,DirectionAccuracy");
        foreach (LiveTotalBaselineLineRow row in rows)
            sb.AppendLine(string.Join(',', EscapeCsv(row.Method), EscapeCsv(row.Scope), EscapeCsv(row.StateTrigger), D(row.Line), I(row.Rows), I(row.Matches), D(row.AverageProbability), D(row.ActualOverRate), D(row.Brier), D(row.LogLoss), D(row.DirectionAccuracy)));
        return sb.ToString();
    }

    private static string ToCalibrationCsv(IReadOnlyCollection<LiveTotalBaselineCalibrationRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Method,Scope,StateTrigger,Line,ProbabilityBucket,Rows,Matches,AverageProbability,ActualOverRate,Brier");
        foreach (LiveTotalBaselineCalibrationRow row in rows)
            sb.AppendLine(string.Join(',', EscapeCsv(row.Method), EscapeCsv(row.Scope), EscapeCsv(row.StateTrigger), D(row.Line), EscapeCsv(row.ProbabilityBucket), I(row.Rows), I(row.Matches), D(row.AverageProbability), D(row.ActualOverRate), D(row.Brier)));
        return sb.ToString();
    }

    private static double? TryNoPushOverProbability(double line, int currentGoals, double remainingGoals)
    {
        try
        {
            OverSettlementProbabilities p = TotalGoalsPricingCalculator.CalculateOverSettlementProbabilities(line, currentGoals, remainingGoals);
            double decisive = p.WinProbability + p.LossProbability;
            if (decisive <= 1e-12)
                return null;

            return Math.Clamp(p.WinProbability / decisive, 0.0, 1.0);
        }
        catch
        {
            return null;
        }
    }

    private static double? TryNoPushOverProbability(double line, int currentGoals, IReadOnlyDictionary<int, double> distribution, double? targetMean = null)
    {
        try
        {
            OverSettlementProbabilities p = TotalGoalsPricingCalculator.CalculateOverSettlementProbabilities(line, currentGoals, distribution, targetMean);
            double decisive = p.WinProbability + p.LossProbability;
            if (decisive <= 1e-12)
                return null;

            return Math.Clamp(p.WinProbability / decisive, 0.0, 1.0);
        }
        catch
        {
            return null;
        }
    }

    private static bool? TryActualOver(double line, int finalTotal)
    {
        double frac = Math.Round(line - Math.Floor(line), 6);
        int floor = (int)Math.Floor(line);

        if (Math.Abs(frac - 0.5) < 1e-6)
            return finalTotal > line;

        if (Math.Abs(frac) < 1e-6)
        {
            if (finalTotal == floor)
                return null;
            return finalTotal > floor;
        }

        if (Math.Abs(frac - 0.25) < 1e-6)
        {
            if (finalTotal == floor)
                return null;
            return finalTotal > floor;
        }

        if (Math.Abs(frac - 0.75) < 1e-6)
        {
            if (finalTotal == floor + 1)
                return null;
            return finalTotal > floor + 1;
        }

        return null;
    }

    private static string MinuteBand(string stateTrigger, int minute)
    {
        string band = LiveTotalStateCorrectionResolver.MinuteBand(stateTrigger, minute);
        if (!string.IsNullOrWhiteSpace(band))
            return band;

        int start = Math.Clamp((minute / 5) * 5, 0, 85);
        int end = Math.Min(90, start + 4);
        return $"{start:00}-{end:00}";
    }

    private static string ProbabilityBucket(double probability)
    {
        if (probability < 0.05) return "00-05";
        if (probability < 0.10) return "05-10";
        if (probability < 0.20) return "10-20";
        if (probability < 0.30) return "20-30";
        if (probability < 0.40) return "30-40";
        if (probability < 0.50) return "40-50";
        if (probability < 0.60) return "50-60";
        if (probability < 0.70) return "60-70";
        if (probability < 0.80) return "70-80";
        if (probability < 0.90) return "80-90";
        if (probability < 0.95) return "90-95";
        return "95-100";
    }

    private static int ProbabilityBucketOrder(string bucket) => bucket switch
    {
        "00-05" => 0,
        "05-10" => 1,
        "10-20" => 2,
        "20-30" => 3,
        "30-40" => 4,
        "40-50" => 5,
        "50-60" => 6,
        "60-70" => 7,
        "70-80" => 8,
        "80-90" => 9,
        "90-95" => 10,
        "95-100" => 11,
        _ => 99
    };

    private static int MethodOrder(string method) => method switch
    {
        var value when value.Equals(RawPoisson, StringComparison.OrdinalIgnoreCase) => 0,
        var value when value.Equals(RawEmpirical, StringComparison.OrdinalIgnoreCase) => 1,
        var value when value.Equals(EmpiricalStateVolume, StringComparison.OrdinalIgnoreCase) => 2,
        _ => 9
    };

    private static int TriggerOrder(string trigger) => trigger switch
    {
        "All" => 0,
        LiveTotalStateTrigger.FixedMinute => 1,
        LiveTotalStateTrigger.AfterGoal => 2,
        LiveTotalStateTrigger.AfterRedCard => 3,
        _ => 9
    };

    private static double Squared(double value) => value * value;
    private static double BoolToDouble(bool value) => value ? 1.0 : 0.0;

    private static double LogLoss(double probability, bool actual)
    {
        probability = Math.Clamp(probability, 1e-6, 1.0 - 1e-6);
        return actual ? -Math.Log(probability) : -Math.Log(1.0 - probability);
    }

    private static string D(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static bool TryGetInt(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column, out int value)
    {
        value = 0;
        return index.TryGetValue(column, out int position) &&
               position < record.Count &&
               int.TryParse(record[position], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetDouble(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column, out double value)
    {
        value = 0.0;
        return index.TryGetValue(column, out int position) &&
               position < record.Count &&
               double.TryParse(record[position], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string GetString(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column) =>
        index.TryGetValue(column, out int position) && position < record.Count ? record[position] : string.Empty;

    private static List<List<string>> ParseCsv(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }

    private sealed class InputRow
    {
        public int SeasonId { get; set; }
        public int MatchId { get; set; }
        public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
        public int Minute { get; set; }
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }
        public int CurrentTotalGoals { get; set; }
        public string ScoreState { get; set; } = string.Empty;
        public string DetailedScoreState { get; set; } = string.Empty;
        public double TimingRemainingShare { get; set; }
        public int ActualFinalTotalGoals { get; set; }
        public int ActualRemainingGoals { get; set; }
    }

    private sealed class RemainingObservation
    {
        public string Method { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string StateTrigger { get; set; } = string.Empty;
        public string MinuteBand { get; set; } = string.Empty;
        public string DetailedScoreState { get; set; } = string.Empty;
        public int MatchId { get; set; }
        public double PredictedRemainingGoals { get; set; }
        public double ActualRemainingGoals { get; set; }
    }

    private sealed class LineObservation
    {
        public string Method { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string StateTrigger { get; set; } = string.Empty;
        public string MinuteBand { get; set; } = string.Empty;
        public string DetailedScoreState { get; set; } = string.Empty;
        public int MatchId { get; set; }
        public double Line { get; set; }
        public double Probability { get; set; }
        public bool ActualOver { get; set; }
    }

    private readonly record struct SummaryKey(string Method, string Scope, string StateTrigger);
    private readonly record struct GroupedKey(string Method, string Scope, string StateTrigger, string GroupKey);
    private readonly record struct LineKey(string Method, string Scope, string StateTrigger, double Line);
    private readonly record struct CalibrationKey(string Method, string Scope, string StateTrigger, double Line, string ProbabilityBucket);

    private sealed class EmpiricalRemainingGoalsModel
    {
        private readonly int _maxRemainingGoals;
        private readonly double _smoothing;
        private readonly List<EmpiricalBucket> _buckets = [];

        public EmpiricalRemainingGoalsModel(int maxRemainingGoals, double smoothing)
        {
            _maxRemainingGoals = maxRemainingGoals;
            _smoothing = smoothing;
        }

        public void AddBucket(string level, string stateTrigger, string minuteBand, string detailedScoreState, int? currentTotalGoals, IReadOnlyCollection<InputRow> rows, int minRows, int minMatches, bool forceUsable)
        {
            if (rows.Count == 0)
                return;

            int matches = rows.Select(x => x.MatchId).Distinct().Count();
            bool usable = forceUsable || (rows.Count >= minRows && matches >= minMatches);
            var counts = new Dictionary<int, int>();
            foreach (InputRow row in rows)
            {
                int goals = Math.Clamp(row.ActualRemainingGoals, 0, _maxRemainingGoals);
                counts[goals] = counts.GetValueOrDefault(goals) + 1;
            }

            double denominator = counts.Values.Sum() + (_maxRemainingGoals + 1) * _smoothing;
            var probabilities = new Dictionary<int, double>();
            for (int goals = 0; goals <= _maxRemainingGoals; goals++)
            {
                counts.TryGetValue(goals, out int count);
                probabilities[goals] = denominator > 0 ? (count + _smoothing) / denominator : 0.0;
            }

            _buckets.Add(new EmpiricalBucket
            {
                Level = level,
                StateTrigger = stateTrigger,
                MinuteBand = minuteBand,
                DetailedScoreState = detailedScoreState,
                CurrentTotalGoals = currentTotalGoals,
                Rows = rows.Count,
                Matches = matches,
                IsUsable = usable,
                Probabilities = probabilities,
                AverageRemainingGoals = probabilities.Sum(x => x.Key * x.Value)
            });
        }

        public EmpiricalResolution Resolve(InputRow row)
        {
            string stateTrigger = LiveTotalStateTrigger.Normalize(row.StateTrigger);
            string minuteBand = MinuteBand(stateTrigger, row.Minute);
            EmpiricalBucket? bucket =
                Find("Exact", stateTrigger, minuteBand, row.DetailedScoreState, row.CurrentTotalGoals) ??
                Find("ScoreState", stateTrigger, minuteBand, row.DetailedScoreState, null) ??
                Find("TriggerBand", stateTrigger, minuteBand, string.Empty, null) ??
                Find("Trigger", stateTrigger, string.Empty, string.Empty, null) ??
                Find("Global", string.Empty, string.Empty, string.Empty, null);

            if (bucket is null)
                return new EmpiricalResolution();

            return new EmpiricalResolution
            {
                IsSupported = true,
                AverageRemainingGoals = bucket.AverageRemainingGoals,
                Probabilities = bucket.Probabilities
            };
        }

        private EmpiricalBucket? Find(string level, string stateTrigger, string minuteBand, string detailedScoreState, int? currentTotalGoals)
        {
            return _buckets.FirstOrDefault(x =>
                x.IsUsable &&
                x.Level.Equals(level, StringComparison.OrdinalIgnoreCase) &&
                Matches(x.StateTrigger, stateTrigger) &&
                x.MinuteBand.Equals(minuteBand, StringComparison.OrdinalIgnoreCase) &&
                x.DetailedScoreState.Equals(detailedScoreState, StringComparison.OrdinalIgnoreCase) &&
                x.CurrentTotalGoals == currentTotalGoals);
        }

        private static bool Matches(string bucketValue, string requestedValue)
        {
            if (string.IsNullOrWhiteSpace(bucketValue))
                return string.IsNullOrWhiteSpace(requestedValue);

            return LiveTotalStateTrigger.Normalize(bucketValue).Equals(requestedValue, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class EmpiricalBucket
    {
        public string Level { get; set; } = string.Empty;
        public string StateTrigger { get; set; } = string.Empty;
        public string MinuteBand { get; set; } = string.Empty;
        public string DetailedScoreState { get; set; } = string.Empty;
        public int? CurrentTotalGoals { get; set; }
        public int Rows { get; set; }
        public int Matches { get; set; }
        public bool IsUsable { get; set; }
        public double AverageRemainingGoals { get; set; }
        public IReadOnlyDictionary<int, double> Probabilities { get; set; } = new Dictionary<int, double>();
    }

    private sealed class EmpiricalResolution
    {
        public bool IsSupported { get; set; }
        public double AverageRemainingGoals { get; set; }
        public IReadOnlyDictionary<int, double> Probabilities { get; set; } = new Dictionary<int, double>();
    }

    private sealed class EmpiricalStateVolumeCorrectionModel
    {
        private readonly double _minFactor;
        private readonly double _maxFactor;
        private readonly List<EmpiricalStateVolumeCorrectionBucket> _buckets = [];

        public EmpiricalStateVolumeCorrectionModel(double minFactor, double maxFactor)
        {
            _minFactor = minFactor;
            _maxFactor = maxFactor;
        }

        public double GlobalFactor { get; private set; } = 1.0;
        public int BucketCount => _buckets.Count;
        public int UsableBucketCount => _buckets.Count(x => x.IsUsable);

        public void AddBucket(
            string level,
            string stateTrigger,
            string minuteBand,
            string detailedScoreState,
            int? currentTotalGoals,
            IReadOnlyCollection<EmpiricalStateVolumeCorrectionTrainingRow> rows,
            int minRows,
            int minMatches,
            int shrinkRows,
            bool forceUsable)
        {
            if (rows.Count == 0)
                return;

            double predicted = rows.Sum(x => x.PredictedRemainingGoals);
            if (predicted <= 1e-9)
                return;

            int matches = rows.Select(x => x.Row.MatchId).Distinct().Count();
            double actual = rows.Sum(x => x.ActualRemainingGoals);
            double rawFactor = actual / predicted;
            _buckets.Add(new EmpiricalStateVolumeCorrectionBucket
            {
                Level = level,
                StateTrigger = stateTrigger,
                MinuteBand = minuteBand,
                DetailedScoreState = detailedScoreState,
                CurrentTotalGoals = currentTotalGoals,
                Rows = rows.Count,
                Matches = matches,
                PredictedRemainingGoals = predicted,
                ActualRemainingGoals = actual,
                RawFactor = rawFactor,
                ShrinkRows = shrinkRows,
                IsUsable = forceUsable || (rows.Count >= minRows && matches >= minMatches)
            });
        }

        public void FinalizeGlobalFactor()
        {
            EmpiricalStateVolumeCorrectionBucket? global = _buckets.FirstOrDefault(x =>
                x.Level.Equals("Global", StringComparison.OrdinalIgnoreCase) && x.IsUsable);

            GlobalFactor = global is null ? 1.0 : Clamp(global.RawFactor);

            foreach (EmpiricalStateVolumeCorrectionBucket bucket in _buckets)
            {
                if (bucket.Level.Equals("Global", StringComparison.OrdinalIgnoreCase))
                {
                    bucket.Factor = GlobalFactor;
                    continue;
                }

                double shrunk = bucket.ShrinkRows <= 0
                    ? bucket.RawFactor
                    : (bucket.RawFactor * bucket.Rows + GlobalFactor * bucket.ShrinkRows) / (bucket.Rows + bucket.ShrinkRows);
                bucket.Factor = Clamp(shrunk);
            }
        }

        public EmpiricalStateVolumeCorrectionResolution Resolve(InputRow row)
        {
            string stateTrigger = LiveTotalStateTrigger.Normalize(row.StateTrigger);
            string minuteBand = MinuteBand(stateTrigger, row.Minute);
            EmpiricalStateVolumeCorrectionBucket? bucket =
                Find("Exact", stateTrigger, minuteBand, row.DetailedScoreState, row.CurrentTotalGoals) ??
                Find("ScoreState", stateTrigger, minuteBand, row.DetailedScoreState, null) ??
                Find("TriggerBand", stateTrigger, minuteBand, string.Empty, null) ??
                Find("Trigger", stateTrigger, string.Empty, string.Empty, null) ??
                Find("Global", string.Empty, string.Empty, string.Empty, null);

            if (bucket is null)
            {
                return new EmpiricalStateVolumeCorrectionResolution
                {
                    IsSupported = false,
                    Factor = GlobalFactor,
                    Source = "global fallback"
                };
            }

            return new EmpiricalStateVolumeCorrectionResolution
            {
                IsSupported = true,
                Factor = bucket.Factor,
                Source = bucket.Level
            };
        }

        private EmpiricalStateVolumeCorrectionBucket? Find(string level, string stateTrigger, string minuteBand, string detailedScoreState, int? currentTotalGoals)
        {
            return _buckets.FirstOrDefault(x =>
                x.IsUsable &&
                x.Level.Equals(level, StringComparison.OrdinalIgnoreCase) &&
                Matches(x.StateTrigger, stateTrigger) &&
                x.MinuteBand.Equals(minuteBand, StringComparison.OrdinalIgnoreCase) &&
                x.DetailedScoreState.Equals(detailedScoreState, StringComparison.OrdinalIgnoreCase) &&
                x.CurrentTotalGoals == currentTotalGoals);
        }

        private double Clamp(double factor) => Math.Clamp(factor, _minFactor, _maxFactor);

        private static bool Matches(string bucketValue, string requestedValue)
        {
            if (string.IsNullOrWhiteSpace(bucketValue))
                return string.IsNullOrWhiteSpace(requestedValue);

            return LiveTotalStateTrigger.Normalize(bucketValue).Equals(requestedValue, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class EmpiricalStateVolumeCorrectionTrainingRow
    {
        public InputRow Row { get; set; } = new();
        public string MinuteBand { get; set; } = string.Empty;
        public double PredictedRemainingGoals { get; set; }
        public double ActualRemainingGoals { get; set; }
    }

    private sealed class EmpiricalStateVolumeCorrectionBucket
    {
        public string Level { get; set; } = string.Empty;
        public string StateTrigger { get; set; } = string.Empty;
        public string MinuteBand { get; set; } = string.Empty;
        public string DetailedScoreState { get; set; } = string.Empty;
        public int? CurrentTotalGoals { get; set; }
        public int Rows { get; set; }
        public int Matches { get; set; }
        public double PredictedRemainingGoals { get; set; }
        public double ActualRemainingGoals { get; set; }
        public double RawFactor { get; set; } = 1.0;
        public double Factor { get; set; } = 1.0;
        public int ShrinkRows { get; set; }
        public bool IsUsable { get; set; }
    }

    private sealed class EmpiricalStateVolumeCorrectionResolution
    {
        public bool IsSupported { get; set; }
        public double Factor { get; set; } = 1.0;
        public string Source { get; set; } = string.Empty;
    }
}
