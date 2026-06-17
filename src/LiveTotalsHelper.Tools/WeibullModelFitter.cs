using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiveTotalsHelper.Tools;

public sealed class WeibullFitOptions
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public int MaxMinute { get; set; } = 90;
    public string MinuteColumn { get; set; } = "GoalMinuteForModel";
    public string GroupByColumn { get; set; } = string.Empty;
    public int MinGroupGoals { get; set; } = 30;
    public int MaxIterations { get; set; } = 100;
    public double Tolerance { get; set; } = 1e-9;
    public double BlendWeibullWeight { get; set; } = 0.30;
    public int[] BucketEdges { get; set; } = [0, 15, 30, 45, 60, 75, 90];
}

public sealed class WeibullFitResult
{
    public string InputSource { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public List<int> SeasonIds { get; } = [];
    public string GroupByColumn { get; set; } = string.Empty;
    public int GoalCount { get; set; }
    public int MatchCount { get; set; }
    public int MaxMinute { get; set; }
    public double ShapeK { get; set; }
    public double ScaleLambda { get; set; }
    public double LogLikelihood { get; set; }
    public double MeanGoalMinute { get; set; }
    public double MedianGoalMinute { get; set; }
    public double CdfAtMaxMinute { get; set; }
    public double BlendWeibullWeight { get; set; }
    public double BlendEmpiricalWeight => 1.0 - BlendWeibullWeight;
    public List<TimingMinuteCheckpoint> Checkpoints { get; } = [];
    public List<TimingBucketComparison> Buckets { get; } = [];
    public List<EmpiricalTimingBucket> EmpiricalBuckets { get; } = [];
    public List<TimingModelFitScore> FitScores { get; } = [];
    public List<TimingModelGroupResult> Groups { get; } = [];
    public List<string> Warnings { get; } = [];
}

public sealed class TimingModelGroupResult
{
    public string GroupName { get; set; } = string.Empty;
    public int GoalCount { get; set; }
    public int MatchCount { get; set; }
    public double ShapeK { get; set; }
    public double ScaleLambda { get; set; }
    public double CdfAtMaxMinute { get; set; }
    public double MeanGoalMinute { get; set; }
    public double MedianGoalMinute { get; set; }
    public double LogLikelihood { get; set; }
    public List<TimingMinuteCheckpoint> Checkpoints { get; set; } = [];
    public List<TimingBucketComparison> BucketComparison { get; set; } = [];
    public List<EmpiricalTimingBucket> EmpiricalBuckets { get; set; } = [];
    public List<TimingModelFitScore> FitScores { get; set; } = [];
}

public sealed class TimingMinuteCheckpoint
{
    public int Minute { get; set; }
    public double WeibullCdf { get; set; }
    public double WeibullRemainingShare { get; set; }
    public double EmpiricalCdf { get; set; }
    public double EmpiricalRemainingShare { get; set; }
    public double BlendedCdf { get; set; }
    public double BlendedRemainingShare { get; set; }
}

public sealed class EmpiricalTimingBucket
{
    public int FromMinuteExclusive { get; set; }
    public int ToMinuteInclusive { get; set; }
    public string Label { get; set; } = string.Empty;
    public int GoalCount { get; set; }
    public double GoalShare { get; set; }
    public double CumulativeShareBefore { get; set; }
    public double CumulativeShareAfter { get; set; }
}

public sealed class TimingBucketComparison
{
    public string Bucket { get; set; } = string.Empty;
    public int ActualGoals { get; set; }
    public double ActualPct { get; set; }
    public double WeibullExpectedPct { get; set; }
    public double EmpiricalExpectedPct { get; set; }
    public double BlendedExpectedPct { get; set; }
}

public sealed class TimingModelFitScore
{
    public string Model { get; set; } = string.Empty;
    public double MeanAbsoluteBucketError { get; set; }
    public double RootMeanSquaredBucketError { get; set; }
    public double MaxAbsoluteBucketError { get; set; }
}

public sealed class WeibullModelFile
{
    public string ModelType { get; set; } = "goal-timing-weibull-empirical-blended-score-state-aware";
    public string League { get; set; } = string.Empty;
    public List<int> SeasonIds { get; set; } = [];
    public int GoalCount { get; set; }
    public int MatchCount { get; set; }
    public int MaxMinute { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string GroupByColumn { get; set; } = string.Empty;
    public string RecommendedTimingModel { get; set; } = "empirical";
    public string Usage { get; set; } = "Use empirical or blended remaining share for live totals. If GroupByColumn is set, select the matching group by current live score state and fall back to All when sample is too small.";
    public WeibullTimingModel Weibull { get; set; } = new();
    public EmpiricalTimingModel Empirical { get; set; } = new();
    public BlendedTimingModel Blended { get; set; } = new();
    public List<TimingMinuteCheckpoint> Checkpoints { get; set; } = [];
    public List<TimingBucketComparison> BucketComparison { get; set; } = [];
    public List<TimingModelFitScore> FitScores { get; set; } = [];
    public List<TimingModelGroupResult> Groups { get; set; } = [];
}

public sealed class WeibullTimingModel
{
    public double ShapeK { get; set; }
    public double ScaleLambda { get; set; }
    public double CdfAtMaxMinute { get; set; }
    public string CdfUsage { get; set; } = "elapsedShare = WeibullCDF(minute) / WeibullCDF(maxMinute); remainingShare = 1 - elapsedShare.";
}

public sealed class EmpiricalTimingModel
{
    public string CdfUsage { get; set; } = "Use piecewise-linear interpolation inside the listed buckets; remainingShare = 1 - empiricalElapsedShare.";
    public List<EmpiricalTimingBucket> Buckets { get; set; } = [];
}

public sealed class BlendedTimingModel
{
    public double WeibullWeight { get; set; }
    public double EmpiricalWeight { get; set; }
    public string CdfUsage { get; set; } = "elapsedShare = WeibullWeight * WeibullElapsedShare + EmpiricalWeight * EmpiricalElapsedShare.";
}

public sealed class WeibullGoalTimingRow
{
    public double Minute { get; set; }
    public int SeasonId { get; set; }
    public string MatchId { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public string GroupValue { get; set; } = "All";
}

public sealed class WeibullModelFitter
{
    private readonly WeibullFitOptions _options;

    public WeibullModelFitter(WeibullFitOptions options)
    {
        _options = options;
    }

    public async Task<WeibullFitResult> FitAsync(IReadOnlyList<WeibullGoalTimingRow> parsedRows, string inputSource, CancellationToken cancellationToken)
    {
        if (_options.MaxMinute <= 0)
            throw new ArgumentException("--max-minute must be greater than 0.");

        if (_options.BlendWeibullWeight < 0 || _options.BlendWeibullWeight > 1)
            throw new ArgumentException("--blend-weibull-weight must be between 0 and 1.");

        if (parsedRows.Count < 5)
            throw new ArgumentException($"Not enough goal minutes to fit timing models. Found {parsedRows.Count}, need at least 5.");

        int[] edges = ResolveBucketEdges(_options.BucketEdges, _options.MaxMinute);
        TimingModelGroupResult all = FitGroup("All", parsedRows, edges);

        string detectedLeague = parsedRows.Select(x => x.League).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
        List<int> seasonIds = parsedRows.Select(x => x.SeasonId).Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        string outputPath = ResolveOutputPath(_options.OutputPath, _options.League, seasonIds);

        var result = new WeibullFitResult
        {
            InputSource = inputSource,
            OutputPath = outputPath,
            League = string.IsNullOrWhiteSpace(_options.League) ? detectedLeague : _options.League,
            GoalCount = all.GoalCount,
            MatchCount = all.MatchCount,
            MaxMinute = _options.MaxMinute,
            ShapeK = all.ShapeK,
            ScaleLambda = all.ScaleLambda,
            LogLikelihood = all.LogLikelihood,
            MeanGoalMinute = all.MeanGoalMinute,
            MedianGoalMinute = all.MedianGoalMinute,
            CdfAtMaxMinute = all.CdfAtMaxMinute,
            BlendWeibullWeight = _options.BlendWeibullWeight,
            GroupByColumn = _options.GroupByColumn
        };

        result.SeasonIds.AddRange(seasonIds);
        result.Checkpoints.AddRange(all.Checkpoints);
        result.Buckets.AddRange(all.BucketComparison);
        result.EmpiricalBuckets.AddRange(all.EmpiricalBuckets);
        result.FitScores.AddRange(all.FitScores);

        if (all.ShapeK <= 1.0)
            result.Warnings.Add($"Fitted shapeK={all.ShapeK.ToString("0.####", CultureInfo.InvariantCulture)}. k <= 1 means goal intensity is flat/decreasing over time; check if the goal minutes are correct.");

        if (all.CdfAtMaxMinute < 0.85)
            result.Warnings.Add($"Weibull CDF at max minute is only {all.CdfAtMaxMinute:P1}. Live model will normalize by this value, but inspect the fit.");

        if (!string.IsNullOrWhiteSpace(_options.GroupByColumn))
        {
            foreach (IGrouping<string, WeibullGoalTimingRow> group in parsedRows.GroupBy(x => x.GroupValue).OrderBy(x => x.Key))
            {
                if (group.Count() < _options.MinGroupGoals)
                {
                    result.Warnings.Add($"Skipped group '{group.Key}' because it has only {group.Count()} goals; minimum is {_options.MinGroupGoals}.");
                    continue;
                }

                result.Groups.Add(FitGroup(group.Key, group.ToList(), edges));
            }
        }

        string recommended = ChooseRecommendedModel(result.FitScores);

        var modelFile = new WeibullModelFile
        {
            League = result.League,
            SeasonIds = result.SeasonIds.ToList(),
            GoalCount = result.GoalCount,
            MatchCount = result.MatchCount,
            MaxMinute = result.MaxMinute,
            CreatedAtUtc = DateTime.UtcNow,
            GroupByColumn = result.GroupByColumn,
            RecommendedTimingModel = recommended,
            Weibull = new WeibullTimingModel
            {
                ShapeK = result.ShapeK,
                ScaleLambda = result.ScaleLambda,
                CdfAtMaxMinute = result.CdfAtMaxMinute
            },
            Empirical = new EmpiricalTimingModel
            {
                Buckets = result.EmpiricalBuckets.ToList()
            },
            Blended = new BlendedTimingModel
            {
                WeibullWeight = result.BlendWeibullWeight,
                EmpiricalWeight = result.BlendEmpiricalWeight
            },
            Checkpoints = result.Checkpoints.ToList(),
            BucketComparison = result.Buckets.ToList(),
            FitScores = result.FitScores.ToList(),
            Groups = result.Groups.ToList()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(modelFile, jsonOptions), Encoding.UTF8, cancellationToken);

        return result;
    }

    private List<WeibullGoalTimingRow> ParseRows(List<Dictionary<string, string>> rows)
    {
        var parsed = new List<WeibullGoalTimingRow>();

        foreach (Dictionary<string, string> row in rows)
        {
            if (!row.TryGetValue(_options.MinuteColumn, out string? minuteRaw) || string.IsNullOrWhiteSpace(minuteRaw))
                continue;

            if (!double.TryParse(minuteRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out double minute))
                continue;

            if (minute <= 0)
                continue;

            minute = Math.Min(minute, _options.MaxMinute);
            string groupValue = "All";
            if (!string.IsNullOrWhiteSpace(_options.GroupByColumn) && row.TryGetValue(_options.GroupByColumn, out string? groupRaw) && !string.IsNullOrWhiteSpace(groupRaw))
                groupValue = groupRaw.Trim();

            parsed.Add(new WeibullGoalTimingRow
            {
                Minute = minute,
                SeasonId = GetInt(row, "SeasonId"),
                MatchId = GetString(row, "EventId", GetString(row, "MatchId", string.Empty)),
                League = GetString(row, "LeagueName", string.Empty),
                GroupValue = groupValue
            });
        }

        return parsed;
    }

    private TimingModelGroupResult FitGroup(string groupName, IReadOnlyList<WeibullGoalTimingRow> rows, IReadOnlyList<int> edges)
    {
        List<double> minutes = rows.Select(x => x.Minute).ToList();
        List<EmpiricalTimingBucket> empiricalBuckets = BuildEmpiricalBuckets(minutes, edges);
        WeibullEstimate estimate = EstimateWeibull(minutes, _options.MaxIterations, _options.Tolerance);
        double cdfAtMaxMinute = WeibullCdf(_options.MaxMinute, estimate.ShapeK, estimate.ScaleLambda);
        if (cdfAtMaxMinute <= 0)
            throw new InvalidOperationException("Fitted Weibull CDF at max minute is zero. Cannot normalize remaining share.");

        var group = new TimingModelGroupResult
        {
            GroupName = groupName,
            GoalCount = minutes.Count,
            MatchCount = rows.Select(x => x.MatchId).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Count(),
            ShapeK = estimate.ShapeK,
            ScaleLambda = estimate.ScaleLambda,
            CdfAtMaxMinute = cdfAtMaxMinute,
            MeanGoalMinute = minutes.Average(),
            MedianGoalMinute = Median(minutes),
            LogLikelihood = estimate.LogLikelihood,
            EmpiricalBuckets = empiricalBuckets
        };

        AddCheckpoints(group, empiricalBuckets, estimate.ShapeK, estimate.ScaleLambda, cdfAtMaxMinute);
        AddBucketComparisons(group, minutes, empiricalBuckets, estimate.ShapeK, estimate.ScaleLambda, cdfAtMaxMinute);
        AddFitScores(group);
        return group;
    }

    private void AddCheckpoints(TimingModelGroupResult group, IReadOnlyList<EmpiricalTimingBucket> empiricalBuckets, double shapeK, double scaleLambda, double cdfAtMaxMinute)
    {
        foreach (int minute in new[] { 15, 30, 45, 60, 75, _options.MaxMinute }.Distinct().OrderBy(x => x))
        {
            double weibullCdf = NormalizedWeibullCdf(minute, shapeK, scaleLambda, cdfAtMaxMinute);
            double empiricalCdf = EmpiricalCdf(minute, empiricalBuckets);
            double blendedCdf = Blend(weibullCdf, empiricalCdf, _options.BlendWeibullWeight);

            group.Checkpoints.Add(new TimingMinuteCheckpoint
            {
                Minute = minute,
                WeibullCdf = weibullCdf,
                WeibullRemainingShare = Math.Max(0.0, 1.0 - weibullCdf),
                EmpiricalCdf = empiricalCdf,
                EmpiricalRemainingShare = Math.Max(0.0, 1.0 - empiricalCdf),
                BlendedCdf = blendedCdf,
                BlendedRemainingShare = Math.Max(0.0, 1.0 - blendedCdf)
            });
        }
    }

    private void AddBucketComparisons(TimingModelGroupResult group, IReadOnlyList<double> minutes, IReadOnlyList<EmpiricalTimingBucket> empiricalBuckets, double shapeK, double scaleLambda, double cdfAtMaxMinute)
    {
        foreach (EmpiricalTimingBucket bucket in empiricalBuckets)
        {
            double weibullExpectedPct = (WeibullCdf(bucket.ToMinuteInclusive, shapeK, scaleLambda) - WeibullCdf(bucket.FromMinuteExclusive, shapeK, scaleLambda)) / cdfAtMaxMinute;
            weibullExpectedPct = Clamp01(weibullExpectedPct);
            double empiricalExpectedPct = bucket.GoalShare;
            double blendedExpectedPct = Blend(weibullExpectedPct, empiricalExpectedPct, _options.BlendWeibullWeight);

            group.BucketComparison.Add(new TimingBucketComparison
            {
                Bucket = bucket.Label,
                ActualGoals = bucket.GoalCount,
                ActualPct = (double)bucket.GoalCount / minutes.Count,
                WeibullExpectedPct = weibullExpectedPct,
                EmpiricalExpectedPct = empiricalExpectedPct,
                BlendedExpectedPct = blendedExpectedPct
            });
        }
    }

    private static void AddFitScores(TimingModelGroupResult group)
    {
        group.FitScores.Add(ScoreModel("Weibull", group.BucketComparison.Select(b => b.WeibullExpectedPct - b.ActualPct)));
        group.FitScores.Add(ScoreModel("Empirical", group.BucketComparison.Select(b => b.EmpiricalExpectedPct - b.ActualPct)));
        group.FitScores.Add(ScoreModel("Blended", group.BucketComparison.Select(b => b.BlendedExpectedPct - b.ActualPct)));
    }

    private static TimingModelFitScore ScoreModel(string name, IEnumerable<double> errors)
    {
        double[] e = errors.ToArray();
        return new TimingModelFitScore
        {
            Model = name,
            MeanAbsoluteBucketError = e.Select(x => Math.Abs(x)).Average(),
            RootMeanSquaredBucketError = Math.Sqrt(e.Select(x => x * x).Average()),
            MaxAbsoluteBucketError = e.Select(x => Math.Abs(x)).Max()
        };
    }

    private static string ChooseRecommendedModel(IReadOnlyList<TimingModelFitScore> fitScores)
    {
        TimingModelFitScore? best = fitScores.OrderBy(x => x.MeanAbsoluteBucketError).FirstOrDefault();
        if (best?.Model.Equals("Weibull", StringComparison.OrdinalIgnoreCase) == true)
            return "weibull";
        if (best?.Model.Equals("Blended", StringComparison.OrdinalIgnoreCase) == true)
            return "blended";
        return "empirical";
    }

    private static List<EmpiricalTimingBucket> BuildEmpiricalBuckets(IReadOnlyList<double> minutes, IReadOnlyList<int> edges)
    {
        var buckets = new List<EmpiricalTimingBucket>();
        double cumulative = 0.0;

        for (int i = 0; i < edges.Count - 1; i++)
        {
            int left = edges[i];
            int right = edges[i + 1];
            int count = minutes.Count(x => x > left && x <= right);
            double share = (double)count / minutes.Count;

            buckets.Add(new EmpiricalTimingBucket
            {
                FromMinuteExclusive = left,
                ToMinuteInclusive = right,
                Label = $"{left + 1}-{right}",
                GoalCount = count,
                GoalShare = share,
                CumulativeShareBefore = cumulative,
                CumulativeShareAfter = cumulative + share
            });

            cumulative += share;
        }

        if (buckets.Count > 0)
            buckets[^1].CumulativeShareAfter = 1.0;

        return buckets;
    }

    private static double EmpiricalCdf(double minute, IReadOnlyList<EmpiricalTimingBucket> buckets)
    {
        if (minute <= 0 || buckets.Count == 0)
            return 0.0;

        foreach (EmpiricalTimingBucket bucket in buckets)
        {
            if (minute <= bucket.FromMinuteExclusive)
                return bucket.CumulativeShareBefore;

            if (minute <= bucket.ToMinuteInclusive)
            {
                double width = bucket.ToMinuteInclusive - bucket.FromMinuteExclusive;
                if (width <= 0)
                    return bucket.CumulativeShareAfter;

                double progress = (minute - bucket.FromMinuteExclusive) / width;
                return Clamp01(bucket.CumulativeShareBefore + bucket.GoalShare * progress);
            }
        }

        return 1.0;
    }

    private static int[] ResolveBucketEdges(IReadOnlyList<int> sourceEdges, int maxMinute)
    {
        int[] edges = sourceEdges
            .Where(x => x >= 0 && x <= maxMinute)
            .Append(0)
            .Append(maxMinute)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (edges.Length < 2)
            return [0, maxMinute];

        return edges;
    }

    private static WeibullEstimate EstimateWeibull(IReadOnlyList<double> values, int maxIterations, double tolerance)
    {
        double[] x = values.Where(v => v > 0).ToArray();
        double meanLog = x.Select(v => Math.Log(v)).Average();
        double varianceLog = x.Select(v => Math.Pow(Math.Log(v) - meanLog, 2)).Average();
        double k = varianceLog > 0 ? Math.PI / Math.Sqrt(6.0 * varianceLog) : 1.5;
        k = Math.Clamp(k, 0.15, 10.0);

        for (int i = 0; i < maxIterations; i++)
        {
            double f = ShapeEquation(k, x, meanLog);
            double step = Math.Max(1e-5, k * 1e-5);
            double fp = ShapeEquation(k + step, x, meanLog);
            double fm = ShapeEquation(Math.Max(0.05, k - step), x, meanLog);
            double derivative = (fp - fm) / ((k + step) - Math.Max(0.05, k - step));

            if (Math.Abs(derivative) < 1e-12)
                break;

            double next = k - f / derivative;
            if (double.IsNaN(next) || double.IsInfinity(next) || next <= 0)
                next = k / 2.0;

            next = Math.Clamp(next, 0.05, 25.0);
            if (Math.Abs(next - k) < tolerance)
            {
                k = next;
                break;
            }

            k = next;
        }

        double lambda = Math.Pow(x.Select(v => Math.Pow(v, k)).Average(), 1.0 / k);
        double logLikelihood = x.Sum(v => Math.Log(k) - k * Math.Log(lambda) + (k - 1.0) * Math.Log(v) - Math.Pow(v / lambda, k));

        return new WeibullEstimate(k, lambda, logLikelihood);
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

    private static double WeibullCdf(double minute, double shapeK, double scaleLambda)
    {
        if (minute <= 0)
            return 0.0;

        return 1.0 - Math.Exp(-Math.Pow(minute / scaleLambda, shapeK));
    }

    private static double NormalizedWeibullCdf(double minute, double shapeK, double scaleLambda, double cdfAtMaxMinute)
    {
        if (cdfAtMaxMinute <= 0)
            return 0.0;

        return Clamp01(WeibullCdf(minute, shapeK, scaleLambda) / cdfAtMaxMinute);
    }

    private static double Blend(double weibullValue, double empiricalValue, double weibullWeight)
    {
        return Clamp01(weibullWeight * weibullValue + (1.0 - weibullWeight) * empiricalValue);
    }

    private static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

    private static double Median(IReadOnlyList<double> values)
    {
        double[] sorted = values.OrderBy(x => x).ToArray();
        int mid = sorted.Length / 2;
        if (sorted.Length % 2 == 1)
            return sorted[mid];

        return (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    private static string ResolveOutputPath(string outputPath, string league, IReadOnlyCollection<int> seasonIds)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
            return outputPath;

        string leaguePart = string.IsNullOrWhiteSpace(league) ? "league" : SlugifySimple(league);
        string seasonPart = seasonIds.Count switch
        {
            0 => "all-seasons",
            1 => $"season-{seasonIds.First()}",
            _ => $"seasons-{seasonIds.Count}"
        };

        return Path.Combine("data", "models", "weibull", $"{leaguePart}-{seasonPart}.json");
    }

    private static string SlugifySimple(string value)
    {
        var sb = new StringBuilder();
        foreach (char ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }

        return sb.ToString().Trim('-');
    }

    private static int GetInt(Dictionary<string, string> row, string column)
    {
        return row.TryGetValue(column, out string? raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }

    private static string GetString(Dictionary<string, string> row, string column, string defaultValue)
    {
        return row.TryGetValue(column, out string? value) ? value : defaultValue;
    }

    private static async Task<List<Dictionary<string, string>>> ReadCsvAsync(string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        List<List<string>> records = ParseCsv(text);
        if (records.Count == 0)
            return [];

        string[] headers = records[0].Select(x => x.Trim()).ToArray();
        var rows = new List<Dictionary<string, string>>();

        foreach (List<string> record in records.Skip(1))
        {
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0]))
                continue;

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Length; i++)
                row[headers[i]] = i < record.Count ? record[i] : string.Empty;

            rows.Add(row);
        }

        return rows;
    }

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
            }
            else
            {
                if (ch == '"')
                {
                    inQuotes = true;
                }
                else if (ch == ',')
                {
                    record.Add(field.ToString());
                    field.Clear();
                }
                else if (ch == '\n')
                {
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                }
                else if (ch != '\r')
                {
                    field.Append(ch);
                }
            }
        }

        record.Add(field.ToString());
        if (record.Count > 1 || !string.IsNullOrWhiteSpace(record[0]))
            records.Add(record);

        return records;
    }

    private readonly record struct WeibullEstimate(double ShapeK, double ScaleLambda, double LogLikelihood);
}
