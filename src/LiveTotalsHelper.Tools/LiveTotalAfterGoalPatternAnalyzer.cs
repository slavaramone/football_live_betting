using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Modeling;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalAfterGoalPatternAnalysisOptions
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public List<int> TrainingSeasonIds { get; } = [];
    public List<int> TestSeasonIds { get; } = [];
    public List<double> TargetLines { get; } = [2.5, 3.5];
    public int PatternMinRows { get; set; } = 20;
    public int PatternMinMatches { get; set; } = 10;
    public int EmpiricalSettlementMinBucketRows { get; set; } = 80;
    public int EmpiricalSettlementMinBucketMatches { get; set; } = 40;
    public int EmpiricalSettlementMaxRemainingGoals { get; set; } = 8;
    public double EmpiricalSettlementSmoothing { get; set; } = 0.25;
}

public sealed class LiveTotalAfterGoalPatternAnalysisResult
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public int RowsRead { get; set; }
    public int TestRows { get; set; }
    public int AfterGoalRows { get; set; }
    public int RowsSkippedMissingExpectedFinalGoals { get; set; }
    public int UnsupportedEmpiricalRows { get; set; }
    public List<int> TrainingSeasonIds { get; } = [];
    public List<int> TestSeasonIds { get; } = [];
    public List<LiveTotalAfterGoalPatternSummary> Summaries { get; } = [];
}

public sealed class LiveTotalAfterGoalPatternSummary
{
    public string MinuteBand { get; set; } = string.Empty;
    public int GoalNumber { get; set; }
    public string GoalEffect { get; set; } = string.Empty;
    public string ScoreBefore { get; set; } = string.Empty;
    public string ScoreAfter { get; set; } = string.Empty;
    public string ScoreStateAfter { get; set; } = string.Empty;
    public string GoalSide { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double AverageMinute { get; set; }
    public double AverageMarketExpectedRemainingGoals { get; set; }
    public double AverageActualRemainingGoals { get; set; }
    public double RemainingGoalsResidual { get; set; }
    public List<LiveTotalAfterGoalLinePatternSummary> Lines { get; } = [];
}

public sealed class LiveTotalAfterGoalLinePatternSummary
{
    public double Line { get; set; }
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double BaselineAverageProbability { get; set; }
    public double PatternAverageProbability { get; set; }
    public double ActualOverRate { get; set; }
    public double BaselineBrier { get; set; }
    public double PatternBrier { get; set; }
    public double BrierImprovementPct { get; set; }
    public double BaselineLogLoss { get; set; }
    public double PatternLogLoss { get; set; }
    public double LogLossImprovementPct { get; set; }
    public int PatternRows { get; set; }
    public int PatternMatches { get; set; }
    public string PatternSource { get; set; } = string.Empty;
}

public sealed class LiveTotalAfterGoalPatternAnalyzer
{
    private readonly LiveTotalAfterGoalPatternAnalysisOptions _options;

    public LiveTotalAfterGoalPatternAnalyzer(LiveTotalAfterGoalPatternAnalysisOptions options)
    {
        _options = options;
    }

    public async Task<LiveTotalAfterGoalPatternAnalysisResult> AnalyzeAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        List<InputRow> rows = await ReadRowsAsync(_options.InputPath, cancellationToken);
        LiveTotalEmpiricalSettlementFile settlementModel = await BuildEmpiricalSettlementAsync(cancellationToken);
        Dictionary<TrainingPatternKey, TrainingPatternStats> trainingPatterns = BuildTrainingPatterns(rows);

        var testRows = rows.Where(x => _options.TestSeasonIds.Contains(x.SeasonId)).ToList();
        int rowsSkippedMissingExpectedFinalGoals = 0;
        int unsupportedEmpiricalRows = 0;
        var afterGoalRows = new List<AfterGoalRow>();

        foreach (InputRow row in testRows.Where(x => x.StateTrigger.Equals(LiveTotalStateTrigger.AfterGoal, StringComparison.OrdinalIgnoreCase)))
        {
            if (!row.ExpectedFinalGoals.HasValue || row.ExpectedFinalGoals.Value <= 0.0)
            {
                rowsSkippedMissingExpectedFinalGoals++;
                continue;
            }

            string minuteBand = LiveTotalStateCorrectionResolver.MinuteBand(row.StateTrigger, row.Minute);
            if (string.IsNullOrWhiteSpace(minuteBand))
                continue;

            LiveTotalEmpiricalSettlementResolution settlement = LiveTotalEmpiricalSettlementResolver.Resolve(
                settlementModel,
                row.StateTrigger,
                row.Minute,
                row.HomeGoals,
                row.AwayGoals);
            if (!settlement.IsSupported)
            {
                unsupportedEmpiricalRows++;
                continue;
            }

            GoalContext goal = ResolveGoalContext(row);
            double expectedRemaining = row.ExpectedFinalGoals.Value * row.TimingRemainingShare;
            var lineResults = new List<AfterGoalLineRow>();

            foreach (double line in _options.TargetLines.Distinct().OrderBy(x => x))
            {
                bool? actualOver = TryActualOver(line, row.ActualFinalTotalGoals);
                if (!actualOver.HasValue)
                    continue;

                double? baselineProbability = TryNoPushOverProbability(line, row.CurrentTotalGoals, settlement.Probabilities, expectedRemaining);
                if (!baselineProbability.HasValue)
                    continue;

                PatternPrediction? patternPrediction = ResolvePatternPrediction(trainingPatterns, row, goal, line);
                lineResults.Add(new AfterGoalLineRow
                {
                    Line = line,
                    ActualOver = actualOver.Value,
                    BaselineProbability = baselineProbability.Value,
                    PatternProbability = patternPrediction?.Probability,
                    PatternRows = patternPrediction?.Rows ?? 0,
                    PatternMatches = patternPrediction?.Matches ?? 0,
                    PatternSource = patternPrediction?.Source ?? string.Empty
                });
            }

            if (lineResults.Count == 0)
                continue;

            afterGoalRows.Add(new AfterGoalRow
            {
                MatchId = row.MatchId,
                Minute = row.Minute,
                MinuteBand = minuteBand,
                GoalNumber = row.CurrentTotalGoals,
                GoalEffect = goal.Effect,
                ScoreBefore = goal.ScoreBefore,
                ScoreAfter = goal.ScoreAfter,
                ScoreStateAfter = LiveTotalStateCorrectionResolver.DetailedScoreState(row.HomeGoals, row.AwayGoals),
                GoalSide = goal.GoalSide,
                MarketExpectedRemainingGoals = expectedRemaining,
                ActualRemainingGoals = row.ActualRemainingGoals,
                Lines = lineResults
            });
        }

        var result = new LiveTotalAfterGoalPatternAnalysisResult
        {
            InputPath = _options.InputPath,
            OutputPath = ResolveOutputPath(),
            RowsRead = rows.Count,
            TestRows = testRows.Count,
            AfterGoalRows = afterGoalRows.Count,
            RowsSkippedMissingExpectedFinalGoals = rowsSkippedMissingExpectedFinalGoals,
            UnsupportedEmpiricalRows = unsupportedEmpiricalRows
        };
        result.TrainingSeasonIds.AddRange(_options.TrainingSeasonIds.OrderBy(x => x));
        result.TestSeasonIds.AddRange(_options.TestSeasonIds.OrderBy(x => x));
        result.Summaries.AddRange(BuildSummaries(afterGoalRows));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(result.OutputPath)) ?? ".");
        await File.WriteAllTextAsync(result.OutputPath, ToCsv(result.Summaries), Encoding.UTF8, cancellationToken);

        return result;
    }

    private List<LiveTotalAfterGoalPatternSummary> BuildSummaries(IReadOnlyCollection<AfterGoalRow> rows)
    {
        var result = new List<LiveTotalAfterGoalPatternSummary>();

        foreach (var group in rows
            .GroupBy(x => new { x.MinuteBand, x.GoalNumber, x.GoalEffect, x.ScoreBefore, x.ScoreAfter, x.ScoreStateAfter, x.GoalSide })
            .OrderBy(x => MinuteBandOrder(x.Key.MinuteBand))
            .ThenBy(x => x.Key.GoalNumber)
            .ThenBy(x => GoalEffectOrder(x.Key.GoalEffect))
            .ThenBy(x => x.Key.ScoreBefore)
            .ThenBy(x => x.Key.ScoreAfter))
        {
            List<AfterGoalRow> bucketRows = group.ToList();
            var summary = new LiveTotalAfterGoalPatternSummary
            {
                MinuteBand = group.Key.MinuteBand,
                GoalNumber = group.Key.GoalNumber,
                GoalEffect = group.Key.GoalEffect,
                ScoreBefore = group.Key.ScoreBefore,
                ScoreAfter = group.Key.ScoreAfter,
                ScoreStateAfter = group.Key.ScoreStateAfter,
                GoalSide = group.Key.GoalSide,
                Rows = bucketRows.Count,
                Matches = bucketRows.Select(x => x.MatchId).Distinct().Count(),
                AverageMinute = bucketRows.Average(x => x.Minute),
                AverageMarketExpectedRemainingGoals = bucketRows.Average(x => x.MarketExpectedRemainingGoals),
                AverageActualRemainingGoals = bucketRows.Average(x => x.ActualRemainingGoals),
                RemainingGoalsResidual = bucketRows.Average(x => x.ActualRemainingGoals - x.MarketExpectedRemainingGoals)
            };

            foreach (double line in _options.TargetLines.Distinct().OrderBy(x => x))
            {
                List<AfterGoalLineRow> lineRows = bucketRows
                    .SelectMany(x => x.Lines.Where(y => Math.Abs(y.Line - line) < 1e-9))
                    .ToList();
                if (lineRows.Count == 0)
                    continue;

                List<AfterGoalLineRow> patternRows = lineRows.Where(x => x.PatternProbability.HasValue).ToList();
                double baselineBrier = lineRows.Average(x => Squared(x.BaselineProbability - BoolToDouble(x.ActualOver)));
                double baselineLogLoss = lineRows.Average(x => LogLoss(x.BaselineProbability, x.ActualOver));
                double patternBrier = patternRows.Count > 0
                    ? patternRows.Average(x => Squared(x.PatternProbability!.Value - BoolToDouble(x.ActualOver)))
                    : double.NaN;
                double patternLogLoss = patternRows.Count > 0
                    ? patternRows.Average(x => LogLoss(x.PatternProbability!.Value, x.ActualOver))
                    : double.NaN;

                summary.Lines.Add(new LiveTotalAfterGoalLinePatternSummary
                {
                    Line = line,
                    Rows = lineRows.Count,
                    Matches = bucketRows.Select(x => x.MatchId).Distinct().Count(),
                    BaselineAverageProbability = lineRows.Average(x => x.BaselineProbability),
                    PatternAverageProbability = patternRows.Count > 0 ? patternRows.Average(x => x.PatternProbability!.Value) : double.NaN,
                    ActualOverRate = lineRows.Average(x => BoolToDouble(x.ActualOver)),
                    BaselineBrier = baselineBrier,
                    PatternBrier = patternBrier,
                    BrierImprovementPct = patternRows.Count > 0 ? ImprovementPct(baselineBrier, patternBrier) : double.NaN,
                    BaselineLogLoss = baselineLogLoss,
                    PatternLogLoss = patternLogLoss,
                    LogLossImprovementPct = patternRows.Count > 0 ? ImprovementPct(baselineLogLoss, patternLogLoss) : double.NaN,
                    PatternRows = patternRows.Count > 0 ? MedianInt(patternRows.Select(x => x.PatternRows)) : 0,
                    PatternMatches = patternRows.Count > 0 ? MedianInt(patternRows.Select(x => x.PatternMatches)) : 0,
                    PatternSource = MostCommon(patternRows.Select(x => x.PatternSource))
                });
            }

            result.Add(summary);
        }

        return result;
    }

    private Dictionary<TrainingPatternKey, TrainingPatternStats> BuildTrainingPatterns(IReadOnlyCollection<InputRow> rows)
    {
        var observations = new List<TrainingObservation>();

        foreach (InputRow row in rows.Where(x => _options.TrainingSeasonIds.Contains(x.SeasonId) && x.StateTrigger.Equals(LiveTotalStateTrigger.AfterGoal, StringComparison.OrdinalIgnoreCase)))
        {
            string minuteBand = LiveTotalStateCorrectionResolver.MinuteBand(row.StateTrigger, row.Minute);
            if (string.IsNullOrWhiteSpace(minuteBand))
                continue;

            GoalContext goal = ResolveGoalContext(row);
            foreach (double line in _options.TargetLines.Distinct().OrderBy(x => x))
            {
                bool? actualOver = TryActualOver(line, row.ActualFinalTotalGoals);
                if (!actualOver.HasValue)
                    continue;

                observations.Add(new TrainingObservation
                {
                    MatchId = row.MatchId,
                    Line = line,
                    MinuteBand = minuteBand,
                    GoalNumber = row.CurrentTotalGoals,
                    GoalEffect = goal.Effect,
                    ScoreBefore = goal.ScoreBefore,
                    ScoreAfter = goal.ScoreAfter,
                    ScoreStateAfter = LiveTotalStateCorrectionResolver.DetailedScoreState(row.HomeGoals, row.AwayGoals),
                    ActualOver = actualOver.Value
                });
            }
        }

        var result = new Dictionary<TrainingPatternKey, TrainingPatternStats>();
        foreach (var group in observations.SelectMany(ToTrainingKeys).GroupBy(x => x.Key))
        {
            List<TrainingObservation> bucketRows = group.Select(x => x.Observation).ToList();
            int matches = bucketRows.Select(x => x.MatchId).Distinct().Count();
            if (bucketRows.Count < _options.PatternMinRows || matches < _options.PatternMinMatches)
                continue;

            result[group.Key] = new TrainingPatternStats
            {
                Probability = bucketRows.Average(x => BoolToDouble(x.ActualOver)),
                Rows = bucketRows.Count,
                Matches = matches,
                Source = group.Key.Level
            };
        }

        return result;
    }

    private static IEnumerable<(TrainingPatternKey Key, TrainingObservation Observation)> ToTrainingKeys(TrainingObservation row)
    {
        yield return (new TrainingPatternKey("ExactScore", row.Line, row.MinuteBand, row.GoalNumber, row.GoalEffect, row.ScoreStateAfter, row.ScoreBefore, row.ScoreAfter), row);
        yield return (new TrainingPatternKey("StateAfter", row.Line, row.MinuteBand, row.GoalNumber, row.GoalEffect, row.ScoreStateAfter, string.Empty, string.Empty), row);
        yield return (new TrainingPatternKey("GoalEffect", row.Line, row.MinuteBand, 0, row.GoalEffect, string.Empty, string.Empty, string.Empty), row);
        yield return (new TrainingPatternKey("AfterGoalAll", row.Line, string.Empty, 0, string.Empty, string.Empty, string.Empty, string.Empty), row);
    }

    private PatternPrediction? ResolvePatternPrediction(Dictionary<TrainingPatternKey, TrainingPatternStats> model, InputRow row, GoalContext goal, double line)
    {
        string minuteBand = LiveTotalStateCorrectionResolver.MinuteBand(row.StateTrigger, row.Minute);
        string scoreStateAfter = LiveTotalStateCorrectionResolver.DetailedScoreState(row.HomeGoals, row.AwayGoals);

        var keys = new[]
        {
            new TrainingPatternKey("ExactScore", line, minuteBand, row.CurrentTotalGoals, goal.Effect, scoreStateAfter, goal.ScoreBefore, goal.ScoreAfter),
            new TrainingPatternKey("StateAfter", line, minuteBand, row.CurrentTotalGoals, goal.Effect, scoreStateAfter, string.Empty, string.Empty),
            new TrainingPatternKey("GoalEffect", line, minuteBand, 0, goal.Effect, string.Empty, string.Empty, string.Empty),
            new TrainingPatternKey("AfterGoalAll", line, string.Empty, 0, string.Empty, string.Empty, string.Empty, string.Empty)
        };

        foreach (TrainingPatternKey key in keys)
        {
            if (model.TryGetValue(key, out TrainingPatternStats? stats))
            {
                return new PatternPrediction
                {
                    Probability = stats.Probability,
                    Rows = stats.Rows,
                    Matches = stats.Matches,
                    Source = stats.Source
                };
            }
        }

        return null;
    }

    private static GoalContext ResolveGoalContext(InputRow row)
    {
        string side = NormalizeSide(row.TriggerEventSide);
        int beforeHome = row.HomeGoals;
        int beforeAway = row.AwayGoals;
        if (side == "Home")
            beforeHome = Math.Max(0, beforeHome - 1);
        else if (side == "Away")
            beforeAway = Math.Max(0, beforeAway - 1);
        else
        {
            // Fallback for old rows without side: infer by which side can be decremented into a plausible previous score.
            if (row.HomeGoals >= row.AwayGoals && row.HomeGoals > 0)
            {
                side = "Home";
                beforeHome--;
            }
            else if (row.AwayGoals > 0)
            {
                side = "Away";
                beforeAway--;
            }
            else
            {
                side = "Unknown";
            }
        }

        beforeHome = Math.Max(0, beforeHome);
        beforeAway = Math.Max(0, beforeAway);
        int beforeTotal = beforeHome + beforeAway;
        int beforeAbs = Math.Abs(beforeHome - beforeAway);
        int afterAbs = Math.Abs(row.HomeGoals - row.AwayGoals);
        string effect;

        if (beforeTotal == 0)
            effect = "FirstGoal";
        else if (afterAbs == 0 && beforeAbs == 1)
            effect = "Equalizer";
        else if (beforeAbs == 0 && afterAbs == 1)
            effect = "CreatesOneGoalLead";
        else if (afterAbs > beforeAbs && afterAbs == 2)
            effect = "ExtendsToTwoGoalLead";
        else if (afterAbs > beforeAbs && afterAbs >= 3)
            effect = "ExtendsToThreePlusLead";
        else if (afterAbs < beforeAbs)
            effect = afterAbs == 0 ? "Equalizer" : "CutsDeficit";
        else
            effect = "Other";

        return new GoalContext
        {
            GoalSide = side,
            ScoreBefore = $"{beforeHome}-{beforeAway}",
            ScoreAfter = $"{row.HomeGoals}-{row.AwayGoals}",
            Effect = effect
        };
    }

    private static string NormalizeSide(string side)
    {
        if (side.Equals("home", StringComparison.OrdinalIgnoreCase) || side.Equals("h", StringComparison.OrdinalIgnoreCase))
            return "Home";
        if (side.Equals("away", StringComparison.OrdinalIgnoreCase) || side.Equals("a", StringComparison.OrdinalIgnoreCase))
            return "Away";
        return string.Empty;
    }

    private async Task<LiveTotalEmpiricalSettlementFile> BuildEmpiricalSettlementAsync(CancellationToken cancellationToken)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"after-goal-empirical-settlement-{Guid.NewGuid():N}.json");
        try
        {
            var fitOptions = new LiveTotalEmpiricalSettlementFitOptions
            {
                InputPath = _options.InputPath,
                OutputPath = tempPath,
                MinBucketRows = _options.EmpiricalSettlementMinBucketRows,
                MinBucketMatches = _options.EmpiricalSettlementMinBucketMatches,
                MaxRemainingGoals = _options.EmpiricalSettlementMaxRemainingGoals,
                Smoothing = _options.EmpiricalSettlementSmoothing
            };
            foreach (int seasonId in _options.TrainingSeasonIds)
                fitOptions.TrainingSeasonIds.Add(seasonId);

            var fitter = new LiveTotalEmpiricalSettlementFitter(fitOptions);
            await fitter.FitAsync(cancellationToken);

            await using FileStream stream = File.OpenRead(tempPath);
            return await JsonSerializer.DeserializeAsync<LiveTotalEmpiricalSettlementFile>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken) ?? throw new InvalidOperationException("Could not read empirical settlement model.");
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Non-critical temp cleanup failure.
            }
        }
    }

    private static double? TryNoPushOverProbability(double line, int currentGoals, IReadOnlyDictionary<int, double> remainingGoalProbabilities, double targetMean)
    {
        try
        {
            OverSettlementProbabilities p = TotalGoalsPricingCalculator.CalculateOverSettlementProbabilities(line, currentGoals, remainingGoalProbabilities, targetMean);
            double decisive = p.WinProbability + p.LossProbability;
            return decisive <= 1e-12 ? null : Math.Clamp(p.WinProbability / decisive, 0.0, 1.0);
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
            return finalTotal == floor ? null : finalTotal > floor;
        if (Math.Abs(frac - 0.25) < 1e-6)
            return finalTotal == floor ? null : finalTotal > floor;
        if (Math.Abs(frac - 0.75) < 1e-6)
            return finalTotal == floor + 1 ? null : finalTotal > floor + 1;
        return null;
    }

    private string ResolveOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
            return _options.OutputPath;

        string directory = Path.GetDirectoryName(_options.InputPath) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(_options.InputPath);
        return Path.Combine(directory, $"{fileName}-after-goal-patterns.csv");
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.InputPath))
            throw new ArgumentException("Missing required argument --input.");
        if (!File.Exists(_options.InputPath))
            throw new FileNotFoundException("Live total calibration dataset CSV was not found.", _options.InputPath);
        if (_options.TrainingSeasonIds.Count == 0)
            throw new ArgumentException("Missing required argument --training-season-ids, or use --validation true with a profile validation split.");
        if (_options.TestSeasonIds.Count == 0)
            throw new ArgumentException("Missing required argument --test-season-ids, or use --validation true with a profile validation split.");
        if (_options.TargetLines.Count == 0)
            throw new ArgumentException("At least one target line is required.");
        if (_options.PatternMinRows < 1)
            throw new ArgumentException("--pattern-min-rows must be >= 1.");
        if (_options.PatternMinMatches < 1)
            throw new ArgumentException("--pattern-min-matches must be >= 1.");
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
            "SeasonId", "MatchId", "StateTrigger", "TriggerEventSide", "Minute", "HomeGoals", "AwayGoals",
            "CurrentTotalGoals", "TimingRemainingShare", "ExpectedFinalGoals", "ActualFinalTotalGoals", "ActualRemainingGoals"
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
                !TryGetOptionalDouble(record, index, "ExpectedFinalGoals", out double? expectedFinalGoals) ||
                !TryGetInt(record, index, "ActualFinalTotalGoals", out int actualFinalTotalGoals) ||
                !TryGetInt(record, index, "ActualRemainingGoals", out int actualRemainingGoals))
                continue;

            rows.Add(new InputRow
            {
                SeasonId = seasonId,
                MatchId = matchId,
                StateTrigger = LiveTotalStateTrigger.Normalize(GetString(record, index, "StateTrigger")),
                TriggerEventSide = GetString(record, index, "TriggerEventSide"),
                Minute = minute,
                HomeGoals = homeGoals,
                AwayGoals = awayGoals,
                CurrentTotalGoals = currentTotalGoals,
                TimingRemainingShare = timingRemainingShare,
                ExpectedFinalGoals = expectedFinalGoals,
                ActualFinalTotalGoals = actualFinalTotalGoals,
                ActualRemainingGoals = actualRemainingGoals
            });
        }

        return rows;
    }

    private string ToCsv(IReadOnlyCollection<LiveTotalAfterGoalPatternSummary> rows)
    {
        var sb = new StringBuilder();
        var lineColumns = _options.TargetLines.Distinct().OrderBy(x => x).ToList();

        sb.Append("MinuteBand,GoalNumber,GoalEffect,ScoreBefore,ScoreAfter,ScoreStateAfter,GoalSide,Rows,Matches,AvgMinute,AvgMarketExpectedRemainingGoals,AvgActualRemainingGoals,RemainingGoalsResidual");
        foreach (double line in lineColumns)
        {
            string prefix = LinePrefix(line);
            sb.Append($",{prefix}_Rows,{prefix}_Matches,{prefix}_BaselineAvgProb,{prefix}_PatternAvgProb,{prefix}_ActualOverRate,{prefix}_BaselineBrier,{prefix}_PatternBrier,{prefix}_BrierImprovementPct,{prefix}_BaselineLogLoss,{prefix}_PatternLogLoss,{prefix}_LogLossImprovementPct,{prefix}_PatternRows,{prefix}_PatternMatches,{prefix}_PatternSource");
        }
        sb.AppendLine();

        foreach (LiveTotalAfterGoalPatternSummary row in rows)
        {
            sb.Append(string.Join(',',
                EscapeCsv(row.MinuteBand),
                row.GoalNumber.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(row.GoalEffect),
                EscapeCsv(row.ScoreBefore),
                EscapeCsv(row.ScoreAfter),
                EscapeCsv(row.ScoreStateAfter),
                EscapeCsv(row.GoalSide),
                row.Rows.ToString(CultureInfo.InvariantCulture),
                row.Matches.ToString(CultureInfo.InvariantCulture),
                D(row.AverageMinute),
                D(row.AverageMarketExpectedRemainingGoals),
                D(row.AverageActualRemainingGoals),
                D(row.RemainingGoalsResidual)));

            foreach (double line in lineColumns)
            {
                LiveTotalAfterGoalLinePatternSummary? lineRow = row.Lines.FirstOrDefault(x => Math.Abs(x.Line - line) < 1e-9);
                if (lineRow is null)
                {
                    sb.Append(",,,,,,,,,,,,,,");
                    continue;
                }

                sb.Append(',');
                sb.Append(string.Join(',',
                    lineRow.Rows.ToString(CultureInfo.InvariantCulture),
                    lineRow.Matches.ToString(CultureInfo.InvariantCulture),
                    D(lineRow.BaselineAverageProbability),
                    D(lineRow.PatternAverageProbability),
                    D(lineRow.ActualOverRate),
                    D(lineRow.BaselineBrier),
                    D(lineRow.PatternBrier),
                    D(lineRow.BrierImprovementPct),
                    D(lineRow.BaselineLogLoss),
                    D(lineRow.PatternLogLoss),
                    D(lineRow.LogLossImprovementPct),
                    lineRow.PatternRows.ToString(CultureInfo.InvariantCulture),
                    lineRow.PatternMatches.ToString(CultureInfo.InvariantCulture),
                    EscapeCsv(lineRow.PatternSource)));
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string LinePrefix(double line) => $"Over{line.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', '_')}";

    private static bool TryGetInt(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column, out int value)
    {
        value = 0;
        return index.TryGetValue(column, out int position) &&
               position < record.Count &&
               int.TryParse(record[position], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetOptionalDouble(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column, out double? value)
    {
        value = null;
        if (!index.TryGetValue(column, out int position) || position >= record.Count)
            return false;
        if (string.IsNullOrWhiteSpace(record[position]))
            return true;
        if (double.TryParse(record[position], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    private static bool TryGetDouble(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column, out double value)
    {
        value = 0;
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

    private static double Squared(double value) => value * value;
    private static double BoolToDouble(bool value) => value ? 1.0 : 0.0;

    private static double LogLoss(double probability, bool actual)
    {
        probability = Math.Clamp(probability, 1e-6, 1.0 - 1e-6);
        return actual ? -Math.Log(probability) : -Math.Log(1.0 - probability);
    }

    private static double ImprovementPct(double baseline, double corrected)
    {
        if (baseline <= 0 || double.IsNaN(corrected))
            return double.NaN;
        return (baseline - corrected) / baseline * 100.0;
    }

    private static string D(double value) => double.IsNaN(value) || double.IsInfinity(value)
        ? string.Empty
        : value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string MostCommon(IEnumerable<string> values) => values
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .GroupBy(x => x)
        .OrderByDescending(x => x.Count())
        .ThenBy(x => x.Key, StringComparer.Ordinal)
        .Select(x => x.Key)
        .FirstOrDefault() ?? string.Empty;

    private static int MedianInt(IEnumerable<int> values)
    {
        List<int> sorted = values.OrderBy(x => x).ToList();
        if (sorted.Count == 0)
            return 0;
        return sorted[sorted.Count / 2];
    }

    private static int MinuteBandOrder(string band) => band switch
    {
        "1-20" => 1,
        "21-35" => 2,
        "36-50" => 3,
        "51-65" => 4,
        "66-90" => 5,
        _ => 99
    };

    private static int GoalEffectOrder(string effect) => effect switch
    {
        "FirstGoal" => 1,
        "Equalizer" => 2,
        "CreatesOneGoalLead" => 3,
        "CutsDeficit" => 4,
        "ExtendsToTwoGoalLead" => 5,
        "ExtendsToThreePlusLead" => 6,
        _ => 99
    };

    private sealed class InputRow
    {
        public int SeasonId { get; set; }
        public int MatchId { get; set; }
        public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
        public string TriggerEventSide { get; set; } = string.Empty;
        public int Minute { get; set; }
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }
        public int CurrentTotalGoals { get; set; }
        public double TimingRemainingShare { get; set; }
        public double? ExpectedFinalGoals { get; set; }
        public int ActualFinalTotalGoals { get; set; }
        public int ActualRemainingGoals { get; set; }
    }

    private sealed class GoalContext
    {
        public string GoalSide { get; set; } = string.Empty;
        public string ScoreBefore { get; set; } = string.Empty;
        public string ScoreAfter { get; set; } = string.Empty;
        public string Effect { get; set; } = string.Empty;
    }

    private sealed class AfterGoalRow
    {
        public int MatchId { get; set; }
        public int Minute { get; set; }
        public string MinuteBand { get; set; } = string.Empty;
        public int GoalNumber { get; set; }
        public string GoalEffect { get; set; } = string.Empty;
        public string ScoreBefore { get; set; } = string.Empty;
        public string ScoreAfter { get; set; } = string.Empty;
        public string ScoreStateAfter { get; set; } = string.Empty;
        public string GoalSide { get; set; } = string.Empty;
        public double MarketExpectedRemainingGoals { get; set; }
        public int ActualRemainingGoals { get; set; }
        public List<AfterGoalLineRow> Lines { get; set; } = [];
    }

    private sealed class AfterGoalLineRow
    {
        public double Line { get; set; }
        public bool ActualOver { get; set; }
        public double BaselineProbability { get; set; }
        public double? PatternProbability { get; set; }
        public int PatternRows { get; set; }
        public int PatternMatches { get; set; }
        public string PatternSource { get; set; } = string.Empty;
    }

    private sealed class TrainingObservation
    {
        public int MatchId { get; set; }
        public double Line { get; set; }
        public string MinuteBand { get; set; } = string.Empty;
        public int GoalNumber { get; set; }
        public string GoalEffect { get; set; } = string.Empty;
        public string ScoreBefore { get; set; } = string.Empty;
        public string ScoreAfter { get; set; } = string.Empty;
        public string ScoreStateAfter { get; set; } = string.Empty;
        public bool ActualOver { get; set; }
    }

    private readonly record struct TrainingPatternKey(
        string Level,
        double Line,
        string MinuteBand,
        int GoalNumber,
        string GoalEffect,
        string ScoreStateAfter,
        string ScoreBefore,
        string ScoreAfter);

    private sealed class TrainingPatternStats
    {
        public double Probability { get; set; }
        public int Rows { get; set; }
        public int Matches { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    private sealed class PatternPrediction
    {
        public double Probability { get; set; }
        public int Rows { get; set; }
        public int Matches { get; set; }
        public string Source { get; set; } = string.Empty;
    }
}
