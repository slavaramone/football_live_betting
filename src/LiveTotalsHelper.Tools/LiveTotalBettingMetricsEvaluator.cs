using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Modeling;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalBettingMetricsEvaluationOptions
{
    public string InputPath { get; set; } = string.Empty;
    public string StateCorrectionPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string EdgeBucketOutputPath { get; set; } = string.Empty;
    public List<int> TestSeasonIds { get; } = [];
    public List<int> TrainingSeasonIds { get; } = [];
    public List<double> TargetLines { get; } = [0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0];
    public int EmpiricalSettlementMinBucketRows { get; set; } = 80;
    public int EmpiricalSettlementMinBucketMatches { get; set; } = 40;
    public int EmpiricalSettlementMaxRemainingGoals { get; set; } = 8;
    public double EmpiricalSettlementSmoothing { get; set; } = 0.25;
    public double EdgeBucketStep { get; set; } = 0.02;
    public string DecisionScope { get; set; } = LiveTotalDecisionScope.FullModel;
    public string StateCorrectionScope { get; set; } = LiveTotalStateCorrectionScope.FixedMinute;
    public string StateCorrectionDirectionGuard { get; set; } = LiveTotalStateCorrectionDirectionGuard.UpOnly;
    public LiveTotalLateGameCorrectionOptions LateGameCorrection { get; set; } = LiveTotalLateGameCorrectionOptions.Disabled();
    public bool CompareScopes { get; set; }
}

public sealed class LiveTotalBettingMetricsEvaluationResult
{
    public string InputPath { get; set; } = string.Empty;
    public string StateCorrectionPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string EdgeBucketOutputPath { get; set; } = string.Empty;
    public int RowsRead { get; set; }
    public int TestRows { get; set; }
    public int RowsSkippedMissingExpectedFinalGoals { get; set; }
    public int LineRows { get; set; }
    public int UnsupportedEmpiricalRows { get; set; }
    public int StateCorrectionAppliedRows { get; set; }
    public int StateCorrectionGatedRows { get; set; }
    public int LateGameBoostedRows { get; set; }
    public string StateCorrectionScope { get; set; } = LiveTotalStateCorrectionScope.FixedMinute;
    public string StateCorrectionDirectionGuard { get; set; } = LiveTotalStateCorrectionDirectionGuard.UpOnly;
    public string LateGameCorrectionSummary { get; set; } = string.Empty;
    public List<string> ScopesEvaluated { get; } = [];
    public List<LiveTotalBettingMetricSummary> Summaries { get; } = [];
    public List<LiveTotalBettingEdgeBucketSummary> EdgeBuckets { get; } = [];
}

public sealed class LiveTotalBettingMetricSummary
{
    public string Scope { get; set; } = LiveTotalDecisionScope.FullModel;
    public string StateTrigger { get; set; } = string.Empty;
    public double Line { get; set; }
    public int Rows { get; set; }
    public int Matches { get; set; }

    public double BaselineAverageProbability { get; set; }
    public double CorrectedAverageProbability { get; set; }
    public double ActualOverRate { get; set; }

    public double BaselineBrier { get; set; }
    public double CorrectedBrier { get; set; }
    public double BrierImprovementPct { get; set; }

    public double BaselineLogLoss { get; set; }
    public double CorrectedLogLoss { get; set; }
    public double LogLossImprovementPct { get; set; }

    public double BaselineDirectionAccuracy { get; set; }
    public double CorrectedDirectionAccuracy { get; set; }
    public double DirectionAccuracyDiffPctPoints { get; set; }

    public double AverageProbabilityMove { get; set; }
    public int CorrectedBetterRows { get; set; }
    public int CorrectedWorseRows { get; set; }
    public double CorrectedBetterRate { get; set; }
    public double CorrectedWorseRate { get; set; }
    public int StateCorrectionAppliedRows { get; set; }
    public int StateCorrectionGatedRows { get; set; }
    public int LateGameBoostedRows { get; set; }
}

public sealed class LiveTotalBettingEdgeBucketSummary
{
    public string Scope { get; set; } = LiveTotalDecisionScope.FullModel;
    public string StateTrigger { get; set; } = string.Empty;
    public double Line { get; set; }
    public string EdgeBucket { get; set; } = string.Empty;
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double AverageBaselineProbability { get; set; }
    public double AverageCorrectedProbability { get; set; }
    public double AverageProbabilityMove { get; set; }
    public double ActualOverRate { get; set; }
    public double BaselineBrier { get; set; }
    public double CorrectedBrier { get; set; }
    public int StateCorrectionAppliedRows { get; set; }
    public int StateCorrectionGatedRows { get; set; }
    public int LateGameBoostedRows { get; set; }
}

public sealed class LiveTotalBettingMetricsEvaluator
{
    private readonly LiveTotalBettingMetricsEvaluationOptions _options;

    public LiveTotalBettingMetricsEvaluator(LiveTotalBettingMetricsEvaluationOptions options)
    {
        _options = options;
    }

    public async Task<LiveTotalBettingMetricsEvaluationResult> EvaluateAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        List<InputRow> rows = await ReadRowsAsync(_options.InputPath, cancellationToken);
        await using FileStream correctionStream = File.OpenRead(_options.StateCorrectionPath);
        LiveTotalStateCorrectionFile correction = await JsonSerializer.DeserializeAsync<LiveTotalStateCorrectionFile>(
            correctionStream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken) ?? throw new InvalidOperationException("Could not read state correction JSON.");

        LiveTotalEmpiricalSettlementFile empiricalSettlement = await BuildEmpiricalSettlementAsync(cancellationToken);

        List<InputRow> testRows = rows
            .Where(x => _options.TestSeasonIds.Contains(x.SeasonId))
            .ToList();

        string[] scopes = _options.CompareScopes
            ? LiveTotalDecisionScope.ComparisonScopes
            : [LiveTotalDecisionScope.Normalize(_options.DecisionScope)];

        var lineRows = new List<LineRow>();
        int unsupportedEmpiricalRows = 0;
        int rowsSkippedMissingExpectedFinalGoals = 0;

        foreach (InputRow row in testRows)
        {
            if (!row.ExpectedFinalGoals.HasValue || row.ExpectedFinalGoals.Value <= 0.0)
            {
                rowsSkippedMissingExpectedFinalGoals++;
                continue;
            }

            double baselineRemaining = row.ExpectedFinalGoals.Value * row.TimingRemainingShare;
            LiveTotalEmpiricalSettlementResolution settlementResolution = LiveTotalEmpiricalSettlementResolver.Resolve(
                empiricalSettlement,
                row.StateTrigger,
                row.Minute,
                row.HomeGoals,
                row.AwayGoals);
            if (!settlementResolution.IsSupported)
            {
                unsupportedEmpiricalRows++;
                continue;
            }

            foreach (double line in _options.TargetLines.Distinct().OrderBy(x => x))
            {
                bool? actualOver = TryActualOver(line, row.ActualFinalTotalGoals);
                if (!actualOver.HasValue)
                    continue;

                LiveTotalStateCorrectionResolution resolved = LiveTotalStateCorrectionGate.Resolve(
                    correction,
                    _options.StateCorrectionScope,
                    _options.StateCorrectionDirectionGuard,
                    _options.LateGameCorrection,
                    row.StateTrigger,
                    row.Minute,
                    row.HomeGoals,
                    row.AwayGoals,
                    targetLine: line);

                double correctedRemaining = baselineRemaining * resolved.Factor;

                double? baselineP = TryNoPushOverProbability(line, row.CurrentTotalGoals, settlementResolution.Probabilities, baselineRemaining);
                double? correctedP = TryNoPushOverProbability(line, row.CurrentTotalGoals, settlementResolution.Probabilities, correctedRemaining);

                if (!baselineP.HasValue || !correctedP.HasValue)
                    continue;

                foreach (string scope in scopes)
                {
                    if (!LiveTotalDecisionScope.IsEligible(scope, row.StateTrigger, row.Minute))
                        continue;

                    lineRows.Add(new LineRow
                    {
                        Scope = scope,
                        StateTrigger = row.StateTrigger,
                        MatchId = row.MatchId,
                        Minute = row.Minute,
                        Line = line,
                        ActualOver = actualOver.Value,
                        BaselineProbability = baselineP.Value,
                        CorrectedProbability = correctedP.Value,
                        StateCorrectionApplied = LiveTotalStateCorrectionGate.IsApplied(resolved),
                        StateCorrectionGated = LiveTotalStateCorrectionGate.IsGatedOut(resolved),
                        LateGameBoosted = LiveTotalStateCorrectionGate.IsLateGameBoosted(resolved)
                    });
                }
            }
        }

        var result = new LiveTotalBettingMetricsEvaluationResult
        {
            InputPath = _options.InputPath,
            StateCorrectionPath = _options.StateCorrectionPath,
            OutputPath = ResolveOutputPath(),
            EdgeBucketOutputPath = ResolveEdgeBucketOutputPath(),
            RowsRead = rows.Count,
            TestRows = testRows.Count,
            RowsSkippedMissingExpectedFinalGoals = rowsSkippedMissingExpectedFinalGoals,
            LineRows = lineRows.Count,
            UnsupportedEmpiricalRows = unsupportedEmpiricalRows,
            StateCorrectionAppliedRows = lineRows.Count(x => x.StateCorrectionApplied),
            StateCorrectionGatedRows = lineRows.Count(x => x.StateCorrectionGated),
            LateGameBoostedRows = lineRows.Count(x => x.LateGameBoosted),
            StateCorrectionScope = LiveTotalStateCorrectionScope.Normalize(_options.StateCorrectionScope),
            StateCorrectionDirectionGuard = LiveTotalStateCorrectionDirectionGuard.Normalize(_options.StateCorrectionDirectionGuard),
            LateGameCorrectionSummary = _options.LateGameCorrection.Summary()
        };
        result.ScopesEvaluated.AddRange(scopes);

        result.Summaries.AddRange(BuildSummaries(lineRows));
        result.EdgeBuckets.AddRange(BuildEdgeBuckets(lineRows));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(result.OutputPath)) ?? ".");
        await File.WriteAllTextAsync(result.OutputPath, ToSummaryCsv(result.Summaries), Encoding.UTF8, cancellationToken);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(result.EdgeBucketOutputPath)) ?? ".");
        await File.WriteAllTextAsync(result.EdgeBucketOutputPath, ToEdgeBucketCsv(result.EdgeBuckets), Encoding.UTF8, cancellationToken);

        return result;
    }

    private static List<LiveTotalBettingMetricSummary> BuildSummaries(IReadOnlyCollection<LineRow> lineRows)
    {
        var groups = lineRows
            .SelectMany(x => IsLateFixedMinute(x)
                ? new[]
                {
                    new { x.Scope, Key = "All", Row = x },
                    new { x.Scope, Key = x.StateTrigger, Row = x },
                    new { x.Scope, Key = "FixedMinuteLateGame", Row = x }
                }
                : new[]
                {
                    new { x.Scope, Key = "All", Row = x },
                    new { x.Scope, Key = x.StateTrigger, Row = x }
                })
            .GroupBy(x => new { x.Scope, x.Key, x.Row.Line })
            .OrderBy(x => LiveTotalDecisionScope.Order(x.Key.Scope))
            .ThenBy(x => TriggerOrder(x.Key.Key))
            .ThenBy(x => x.Key.Line);

        var result = new List<LiveTotalBettingMetricSummary>();

        foreach (var group in groups)
        {
            List<LineRow> rows = group.Select(x => x.Row).ToList();
            result.Add(BuildSummary(group.Key.Scope, group.Key.Key, group.Key.Line, rows));
        }

        return result;
    }

    private static LiveTotalBettingMetricSummary BuildSummary(string scope, string trigger, double line, IReadOnlyCollection<LineRow> rows)
    {
        int n = rows.Count;
        double baseBrier = rows.Average(x => Squared(x.BaselineProbability - BoolToDouble(x.ActualOver)));
        double corrBrier = rows.Average(x => Squared(x.CorrectedProbability - BoolToDouble(x.ActualOver)));
        double baseLogLoss = rows.Average(x => LogLoss(x.BaselineProbability, x.ActualOver));
        double corrLogLoss = rows.Average(x => LogLoss(x.CorrectedProbability, x.ActualOver));

        int correctedBetter = rows.Count(x =>
            Squared(x.CorrectedProbability - BoolToDouble(x.ActualOver)) <
            Squared(x.BaselineProbability - BoolToDouble(x.ActualOver)));

        int correctedWorse = rows.Count(x =>
            Squared(x.CorrectedProbability - BoolToDouble(x.ActualOver)) >
            Squared(x.BaselineProbability - BoolToDouble(x.ActualOver)));

        double baseAcc = rows.Average(x => (x.BaselineProbability >= 0.5) == x.ActualOver ? 1.0 : 0.0);
        double corrAcc = rows.Average(x => (x.CorrectedProbability >= 0.5) == x.ActualOver ? 1.0 : 0.0);

        return new LiveTotalBettingMetricSummary
        {
            Scope = scope,
            StateTrigger = trigger,
            Line = line,
            Rows = n,
            Matches = rows.Select(x => x.MatchId).Distinct().Count(),

            BaselineAverageProbability = rows.Average(x => x.BaselineProbability),
            CorrectedAverageProbability = rows.Average(x => x.CorrectedProbability),
            ActualOverRate = rows.Average(x => BoolToDouble(x.ActualOver)),

            BaselineBrier = baseBrier,
            CorrectedBrier = corrBrier,
            BrierImprovementPct = ImprovementPct(baseBrier, corrBrier),

            BaselineLogLoss = baseLogLoss,
            CorrectedLogLoss = corrLogLoss,
            LogLossImprovementPct = ImprovementPct(baseLogLoss, corrLogLoss),

            BaselineDirectionAccuracy = baseAcc,
            CorrectedDirectionAccuracy = corrAcc,
            DirectionAccuracyDiffPctPoints = (corrAcc - baseAcc) * 100.0,

            AverageProbabilityMove = rows.Average(x => x.CorrectedProbability - x.BaselineProbability),
            CorrectedBetterRows = correctedBetter,
            CorrectedWorseRows = correctedWorse,
            CorrectedBetterRate = correctedBetter / (double)n,
            CorrectedWorseRate = correctedWorse / (double)n,
            StateCorrectionAppliedRows = rows.Count(x => x.StateCorrectionApplied),
            StateCorrectionGatedRows = rows.Count(x => x.StateCorrectionGated),
            LateGameBoostedRows = rows.Count(x => x.LateGameBoosted)
        };
    }

    private List<LiveTotalBettingEdgeBucketSummary> BuildEdgeBuckets(IReadOnlyCollection<LineRow> lineRows)
    {
        var groups = lineRows
            .SelectMany(x => IsLateFixedMinute(x)
                ? new[]
                {
                    new { x.Scope, Key = "All", Row = x },
                    new { x.Scope, Key = x.StateTrigger, Row = x },
                    new { x.Scope, Key = "FixedMinuteLateGame", Row = x }
                }
                : new[]
                {
                    new { x.Scope, Key = "All", Row = x },
                    new { x.Scope, Key = x.StateTrigger, Row = x }
                })
            .GroupBy(x => new { x.Scope, x.Key, x.Row.Line, Bucket = ProbabilityMoveBucket(x.Row.CorrectedProbability - x.Row.BaselineProbability) })
            .OrderBy(x => LiveTotalDecisionScope.Order(x.Key.Scope))
            .ThenBy(x => TriggerOrder(x.Key.Key))
            .ThenBy(x => x.Key.Line)
            .ThenBy(x => EdgeBucketOrder(x.Key.Bucket));

        var result = new List<LiveTotalBettingEdgeBucketSummary>();

        foreach (var group in groups)
        {
            List<LineRow> rows = group.Select(x => x.Row).ToList();
            result.Add(new LiveTotalBettingEdgeBucketSummary
            {
                Scope = group.Key.Scope,
                StateTrigger = group.Key.Key,
                Line = group.Key.Line,
                EdgeBucket = group.Key.Bucket,
                Rows = rows.Count,
                Matches = rows.Select(x => x.MatchId).Distinct().Count(),
                AverageBaselineProbability = rows.Average(x => x.BaselineProbability),
                AverageCorrectedProbability = rows.Average(x => x.CorrectedProbability),
                AverageProbabilityMove = rows.Average(x => x.CorrectedProbability - x.BaselineProbability),
                ActualOverRate = rows.Average(x => BoolToDouble(x.ActualOver)),
                BaselineBrier = rows.Average(x => Squared(x.BaselineProbability - BoolToDouble(x.ActualOver))),
                CorrectedBrier = rows.Average(x => Squared(x.CorrectedProbability - BoolToDouble(x.ActualOver))),
                StateCorrectionAppliedRows = rows.Count(x => x.StateCorrectionApplied),
                StateCorrectionGatedRows = rows.Count(x => x.StateCorrectionGated),
                LateGameBoostedRows = rows.Count(x => x.LateGameBoosted)
            });
        }

        return result;
    }

    private static double? TryNoPushOverProbability(double line, int currentGoals, IReadOnlyDictionary<int, double> remainingGoalProbabilities, double targetMean)
    {
        try
        {
            OverSettlementProbabilities p = TotalGoalsPricingCalculator.CalculateOverSettlementProbabilities(line, currentGoals, remainingGoalProbabilities, targetMean);
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

    private async Task<LiveTotalEmpiricalSettlementFile> BuildEmpiricalSettlementAsync(CancellationToken cancellationToken)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"live-total-empirical-settlement-{Guid.NewGuid():N}.json");
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
                // Temp cleanup failure should not fail evaluation.
            }
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

    private string ProbabilityMoveBucket(double move)
    {
        double step = Math.Max(0.005, _options.EdgeBucketStep);
        if (move <= -5 * step) return $"<=-{5 * step:P0}";
        if (move <= -2.5 * step) return $"-{5 * step:P0}..-{2.5 * step:P0}";
        if (move <= -step) return $"-{2.5 * step:P0}..-{step:P0}";
        if (move < step) return $"-{step:P0}..+{step:P0}";
        if (move < 2.5 * step) return $"+{step:P0}..+{2.5 * step:P0}";
        if (move < 5 * step) return $"+{2.5 * step:P0}..+{5 * step:P0}";
        return $">=+{5 * step:P0}";
    }

    private static int EdgeBucketOrder(string bucket)
    {
        if (bucket.StartsWith("<=", StringComparison.Ordinal)) return 1;
        if (bucket.StartsWith("-", StringComparison.Ordinal) && bucket.Contains("..-")) return bucket.Contains("5") ? 2 : 3;
        if (bucket.StartsWith("-", StringComparison.Ordinal) && bucket.Contains("..+")) return 4;
        if (bucket.StartsWith("+", StringComparison.Ordinal)) return bucket.Contains("2") ? 5 : 6;
        if (bucket.StartsWith(">=", StringComparison.Ordinal)) return 7;
        return 99;
    }

    private static double Squared(double value) => value * value;

    private static double BoolToDouble(bool value) => value ? 1.0 : 0.0;

    private static double LogLoss(double probability, bool actual)
    {
        probability = Math.Clamp(probability, 1e-6, 1.0 - 1e-6);
        return actual
            ? -Math.Log(probability)
            : -Math.Log(1.0 - probability);
    }

    private static double ImprovementPct(double baseline, double corrected)
    {
        if (baseline <= 0)
            return 0.0;

        return (baseline - corrected) / baseline * 100.0;
    }

    private string ResolveOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
            return _options.OutputPath;

        string directory = Path.GetDirectoryName(_options.InputPath) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(_options.InputPath);
        return Path.Combine(directory, $"{fileName}-betting-metrics.csv");
    }

    private string ResolveEdgeBucketOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.EdgeBucketOutputPath))
            return _options.EdgeBucketOutputPath;

        string directory = Path.GetDirectoryName(ResolveOutputPath()) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(ResolveOutputPath());
        return Path.Combine(directory, $"{fileName}-edge-buckets.csv");
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.InputPath))
            throw new ArgumentException("Missing required argument --input.");
        if (!File.Exists(_options.InputPath))
            throw new FileNotFoundException("Live total calibration dataset CSV was not found.", _options.InputPath);
        if (string.IsNullOrWhiteSpace(_options.StateCorrectionPath))
            throw new ArgumentException("Missing required argument --state-correction.");
        if (!File.Exists(_options.StateCorrectionPath))
            throw new FileNotFoundException("State correction JSON was not found.", _options.StateCorrectionPath);
        if (_options.TestSeasonIds.Count == 0)
            throw new ArgumentException("Missing required argument --test-season-ids, or use --validation true with a profile validation split.");
        if (_options.TrainingSeasonIds.Count == 0)
            throw new ArgumentException("Missing required argument --training-season-ids, or use --validation true with a profile validation split.");
        if (_options.TargetLines.Count == 0)
            throw new ArgumentException("At least one target line is required.");
        _ = LiveTotalDecisionScope.Normalize(_options.DecisionScope);
        _ = LiveTotalStateCorrectionScope.Normalize(_options.StateCorrectionScope);
        _ = LiveTotalStateCorrectionDirectionGuard.Normalize(_options.StateCorrectionDirectionGuard);
        _ = _options.LateGameCorrection.Normalized();
    }

    private static int TriggerOrder(string stateTrigger)
    {
        if (stateTrigger.Equals("All", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (stateTrigger.Equals("FixedMinuteLateGame", StringComparison.OrdinalIgnoreCase))
            return 2;

        return LiveTotalStateTrigger.Normalize(stateTrigger) switch
        {
            LiveTotalStateTrigger.FixedMinute => 1,
            LiveTotalStateTrigger.AfterGoal => 3,
            LiveTotalStateTrigger.AfterRedCard => 4,
            _ => 99
        };
    }

    private static async Task<List<InputRow>> ReadRowsAsync(string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        List<List<string>> records = ParseCsv(text);
        if (records.Count == 0)
            return [];

        string[] headers = records[0].Select(x => x.Trim()).ToArray();
        var index = headers
            .Select((name, position) => new { name, position })
            .ToDictionary(x => x.name, x => x.position, StringComparer.OrdinalIgnoreCase);

        foreach (string required in new[]
        {
            "SeasonId", "MatchId", "StateTrigger", "Minute", "HomeGoals", "AwayGoals",
            "CurrentTotalGoals", "TimingRemainingShare", "ExpectedFinalGoals", "ActualFinalTotalGoals", "ActualRemainingGoals"
        })
        {
            if (!index.ContainsKey(required))
                throw new ArgumentException($"Input CSV is missing required column '{required}'.");
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

    private static string ToSummaryCsv(IReadOnlyCollection<LiveTotalBettingMetricSummary> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Scope,StateTrigger,Line,Rows,Matches,BaselineAvgProb,CorrectedAvgProb,ActualOverRate,BaselineBrier,CorrectedBrier,BrierImprovementPct,BaselineLogLoss,CorrectedLogLoss,LogLossImprovementPct,BaselineDirectionAccuracy,CorrectedDirectionAccuracy,DirectionAccuracyDiffPctPoints,AverageProbabilityMove,CorrectedBetterRows,CorrectedWorseRows,CorrectedBetterRate,CorrectedWorseRate,StateCorrectionAppliedRows,StateCorrectionGatedRows,LateGameBoostedRows");

        foreach (LiveTotalBettingMetricSummary row in rows)
        {
            sb.AppendLine(string.Join(',',
                EscapeCsv(row.Scope),
                EscapeCsv(row.StateTrigger),
                D(row.Line),
                row.Rows.ToString(CultureInfo.InvariantCulture),
                row.Matches.ToString(CultureInfo.InvariantCulture),
                D(row.BaselineAverageProbability),
                D(row.CorrectedAverageProbability),
                D(row.ActualOverRate),
                D(row.BaselineBrier),
                D(row.CorrectedBrier),
                D(row.BrierImprovementPct),
                D(row.BaselineLogLoss),
                D(row.CorrectedLogLoss),
                D(row.LogLossImprovementPct),
                D(row.BaselineDirectionAccuracy),
                D(row.CorrectedDirectionAccuracy),
                D(row.DirectionAccuracyDiffPctPoints),
                D(row.AverageProbabilityMove),
                row.CorrectedBetterRows.ToString(CultureInfo.InvariantCulture),
                row.CorrectedWorseRows.ToString(CultureInfo.InvariantCulture),
                D(row.CorrectedBetterRate),
                D(row.CorrectedWorseRate),
                row.StateCorrectionAppliedRows.ToString(CultureInfo.InvariantCulture),
                row.StateCorrectionGatedRows.ToString(CultureInfo.InvariantCulture),
                row.LateGameBoostedRows.ToString(CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    private static string ToEdgeBucketCsv(IReadOnlyCollection<LiveTotalBettingEdgeBucketSummary> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Scope,StateTrigger,Line,EdgeBucket,Rows,Matches,AverageBaselineProbability,AverageCorrectedProbability,AverageProbabilityMove,ActualOverRate,BaselineBrier,CorrectedBrier,StateCorrectionAppliedRows,StateCorrectionGatedRows,LateGameBoostedRows");

        foreach (LiveTotalBettingEdgeBucketSummary row in rows)
        {
            sb.AppendLine(string.Join(',',
                EscapeCsv(row.Scope),
                EscapeCsv(row.StateTrigger),
                D(row.Line),
                EscapeCsv(row.EdgeBucket),
                row.Rows.ToString(CultureInfo.InvariantCulture),
                row.Matches.ToString(CultureInfo.InvariantCulture),
                D(row.AverageBaselineProbability),
                D(row.AverageCorrectedProbability),
                D(row.AverageProbabilityMove),
                D(row.ActualOverRate),
                D(row.BaselineBrier),
                D(row.CorrectedBrier),
                row.StateCorrectionAppliedRows.ToString(CultureInfo.InvariantCulture),
                row.StateCorrectionGatedRows.ToString(CultureInfo.InvariantCulture),
                row.LateGameBoostedRows.ToString(CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    private static string D(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    private static bool IsLateFixedMinute(LineRow row) =>
        row.StateTrigger.Equals(LiveTotalStateTrigger.FixedMinute, StringComparison.OrdinalIgnoreCase) &&
        row.Minute >= _LateGameSummaryStartMinute;

    private const int _LateGameSummaryStartMinute = 70;

    private sealed class InputRow
    {
        public int SeasonId { get; set; }
        public int MatchId { get; set; }
        public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
        public int Minute { get; set; }
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }
        public int CurrentTotalGoals { get; set; }
        public double TimingRemainingShare { get; set; }
        public double? ExpectedFinalGoals { get; set; }
        public int ActualFinalTotalGoals { get; set; }
        public int ActualRemainingGoals { get; set; }
    }

    private sealed class LineRow
    {
        public string Scope { get; set; } = LiveTotalDecisionScope.FullModel;
        public string StateTrigger { get; set; } = string.Empty;
        public int MatchId { get; set; }
        public int Minute { get; set; }
        public double Line { get; set; }
        public bool ActualOver { get; set; }
        public double BaselineProbability { get; set; }
        public double CorrectedProbability { get; set; }
        public bool StateCorrectionApplied { get; set; }
        public bool StateCorrectionGated { get; set; }
        public bool LateGameBoosted { get; set; }
    }
}
