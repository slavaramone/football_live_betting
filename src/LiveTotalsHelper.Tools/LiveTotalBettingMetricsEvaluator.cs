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
    public List<double> TargetLines { get; } = [0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0];
    public double EdgeBucketStep { get; set; } = 0.02;
    public string DecisionScope { get; set; } = LiveTotalDecisionScope.FullModel;
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
    public int LineRows { get; set; }
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

        List<InputRow> testRows = rows
            .Where(x => _options.TestSeasonIds.Contains(x.SofaScoreSeasonId))
            .ToList();

        string[] scopes = _options.CompareScopes
            ? LiveTotalDecisionScope.ComparisonScopes
            : [LiveTotalDecisionScope.Normalize(_options.DecisionScope)];

        var lineRows = new List<LineRow>();

        foreach (InputRow row in testRows)
        {
            double baselineRemaining = correction.LeagueAverageFinalGoals * row.TimingRemainingShare;
            LiveTotalStateCorrectionResolution resolved = LiveTotalStateCorrectionResolver.Resolve(
                correction,
                row.StateTrigger,
                row.Minute,
                row.HomeGoals,
                row.AwayGoals);

            double correctedRemaining = baselineRemaining * resolved.Factor;

            foreach (double line in _options.TargetLines.Distinct().OrderBy(x => x))
            {
                bool? actualOver = TryActualOver(line, row.ActualFinalTotalGoals);
                if (!actualOver.HasValue)
                    continue;

                double? baselineP = TryNoPushOverProbability(line, row.CurrentTotalGoals, baselineRemaining);
                double? correctedP = TryNoPushOverProbability(line, row.CurrentTotalGoals, correctedRemaining);

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
                        Line = line,
                        ActualOver = actualOver.Value,
                        BaselineProbability = baselineP.Value,
                        CorrectedProbability = correctedP.Value
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
            LineRows = lineRows.Count
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
            .SelectMany(x => new[]
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
            CorrectedWorseRate = correctedWorse / (double)n
        };
    }

    private List<LiveTotalBettingEdgeBucketSummary> BuildEdgeBuckets(IReadOnlyCollection<LineRow> lineRows)
    {
        var groups = lineRows
            .SelectMany(x => new[]
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
                CorrectedBrier = rows.Average(x => Squared(x.CorrectedProbability - BoolToDouble(x.ActualOver)))
            });
        }

        return result;
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
        if (_options.TargetLines.Count == 0)
            throw new ArgumentException("At least one target line is required.");
        _ = LiveTotalDecisionScope.Normalize(_options.DecisionScope);
    }

    private static int TriggerOrder(string stateTrigger)
    {
        if (stateTrigger.Equals("All", StringComparison.OrdinalIgnoreCase))
            return 0;

        return LiveTotalStateTrigger.Normalize(stateTrigger) switch
        {
            LiveTotalStateTrigger.FixedMinute => 1,
            LiveTotalStateTrigger.AfterGoal => 2,
            LiveTotalStateTrigger.AfterRedCard => 3,
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
            "SofaScoreSeasonId", "MatchId", "StateTrigger", "Minute", "HomeGoals", "AwayGoals",
            "CurrentTotalGoals", "TimingRemainingShare", "ActualFinalTotalGoals", "ActualRemainingGoals"
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

            if (!TryGetInt(record, index, "SofaScoreSeasonId", out int seasonId) ||
                !TryGetInt(record, index, "MatchId", out int matchId) ||
                !TryGetInt(record, index, "Minute", out int minute) ||
                !TryGetInt(record, index, "HomeGoals", out int homeGoals) ||
                !TryGetInt(record, index, "AwayGoals", out int awayGoals) ||
                !TryGetInt(record, index, "CurrentTotalGoals", out int currentTotalGoals) ||
                !TryGetDouble(record, index, "TimingRemainingShare", out double timingRemainingShare) ||
                !TryGetInt(record, index, "ActualFinalTotalGoals", out int actualFinalTotalGoals) ||
                !TryGetInt(record, index, "ActualRemainingGoals", out int actualRemainingGoals))
                continue;

            rows.Add(new InputRow
            {
                SofaScoreSeasonId = seasonId,
                MatchId = matchId,
                StateTrigger = LiveTotalStateTrigger.Normalize(GetString(record, index, "StateTrigger")),
                Minute = minute,
                HomeGoals = homeGoals,
                AwayGoals = awayGoals,
                CurrentTotalGoals = currentTotalGoals,
                TimingRemainingShare = timingRemainingShare,
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
        sb.AppendLine("Scope,StateTrigger,Line,Rows,Matches,BaselineAvgProb,CorrectedAvgProb,ActualOverRate,BaselineBrier,CorrectedBrier,BrierImprovementPct,BaselineLogLoss,CorrectedLogLoss,LogLossImprovementPct,BaselineDirectionAccuracy,CorrectedDirectionAccuracy,DirectionAccuracyDiffPctPoints,AverageProbabilityMove,CorrectedBetterRows,CorrectedWorseRows,CorrectedBetterRate,CorrectedWorseRate");

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
                D(row.CorrectedWorseRate)));
        }

        return sb.ToString();
    }

    private static string ToEdgeBucketCsv(IReadOnlyCollection<LiveTotalBettingEdgeBucketSummary> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Scope,StateTrigger,Line,EdgeBucket,Rows,Matches,AverageBaselineProbability,AverageCorrectedProbability,AverageProbabilityMove,ActualOverRate,BaselineBrier,CorrectedBrier");

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
                D(row.CorrectedBrier)));
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

    private sealed class InputRow
    {
        public int SofaScoreSeasonId { get; set; }
        public int MatchId { get; set; }
        public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
        public int Minute { get; set; }
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }
        public int CurrentTotalGoals { get; set; }
        public double TimingRemainingShare { get; set; }
        public int ActualFinalTotalGoals { get; set; }
        public int ActualRemainingGoals { get; set; }
    }

    private sealed class LineRow
    {
        public string Scope { get; set; } = LiveTotalDecisionScope.FullModel;
        public string StateTrigger { get; set; } = string.Empty;
        public int MatchId { get; set; }
        public double Line { get; set; }
        public bool ActualOver { get; set; }
        public double BaselineProbability { get; set; }
        public double CorrectedProbability { get; set; }
    }
}
