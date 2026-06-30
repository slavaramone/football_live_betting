using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Core.MonteCarlo;

namespace LiveTotalsHelper.Tools;

public sealed class LiveStateCorrectionFitOptions
{
    public string SourceEvaluationSummaryPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = "outputs/calibration/live-state-correction.json";
    public int MinRows { get; init; } = 80;
    public double PriorRows { get; init; } = 150.0;
    public double Shrink { get; init; } = 0.8;
    public double MinMultiplier { get; init; } = 0.75;
    public double MaxMultiplier { get; init; } = 1.35;
    public double MinAbsBias { get; init; } = 0.03;
    public double MinRawMultiplierDistance { get; init; } = 0.03;
    public bool IncludeLineFactors { get; init; }
    public bool IncludeMinuteFactors { get; init; }
    public bool IncludePregameFactors { get; init; } = true;
    public string SummaryOutputPath { get; init; } = string.Empty;
}

public sealed class LiveStateCorrectionFitResult
{
    public LiveStateCorrectionSet Correction { get; init; } = new();
    public string OutputPath { get; init; } = string.Empty;
    public string SummaryOutputPath { get; init; } = string.Empty;
}

public sealed class LiveStateCorrectionFitter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly TextWriter _log;

    public LiveStateCorrectionFitter(TextWriter? log = null)
    {
        _log = log ?? TextWriter.Null;
    }

    public async Task<LiveStateCorrectionFitResult> FitAsync(
        LiveStateCorrectionFitOptions options,
        CancellationToken cancellationToken)
    {
        Validate(options);

        MonteCarloModelEvaluationSummary source = await ReadJsonAsync<MonteCarloModelEvaluationSummary>(options.SourceEvaluationSummaryPath, cancellationToken);
        var factors = new List<LiveStateCorrectionFactor>();
        foreach (MonteCarloSliceSummary slice in source.Slices)
        {
            LiveStateCorrectionFactor? factor = TryBuildFactor(source, slice, options);
            if (factor is not null)
                factors.Add(factor);
        }

        factors = factors
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(f => f.Rows).First())
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Key)
            .ToList();

        var correction = new LiveStateCorrectionSet
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            League = source.League,
            SourceEvaluationSummaryPath = Path.GetFullPath(options.SourceEvaluationSummaryPath),
            Settings = new LiveStateCorrectionSettings
            {
                Enabled = true,
                MinRows = options.MinRows,
                PriorRows = options.PriorRows,
                Shrink = options.Shrink,
                MinMultiplier = options.MinMultiplier,
                MaxMultiplier = options.MaxMultiplier
            },
            Factors = factors
        };

        string outputPath = await WriteJsonAsync(options.OutputPath, correction, cancellationToken);
        string summaryPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(options.SummaryOutputPath))
            summaryPath = await WriteCsvSummaryAsync(options.SummaryOutputPath, correction, cancellationToken);

        await _log.WriteLineAsync($"Live-state correction fitted: {factors.Count} factor(s). Output: {outputPath}");
        return new LiveStateCorrectionFitResult
        {
            Correction = correction,
            OutputPath = outputPath,
            SummaryOutputPath = summaryPath
        };
    }

    private static LiveStateCorrectionFactor? TryBuildFactor(
        MonteCarloModelEvaluationSummary summary,
        MonteCarloSliceSummary slice,
        LiveStateCorrectionFitOptions options)
    {
        string name = slice.Name.Trim();
        if (!IsEligibleSlice(name, options))
            return null;

        MonteCarloPredictionMetrics metrics = slice.Prediction;
        if (metrics.Rows < options.MinRows)
            return null;
        if (metrics.PredictedRemainingAvg <= 0.02)
            return null;
        if (Math.Abs(metrics.Bias) < options.MinAbsBias)
            return null;

        double rawMultiplier = metrics.ActualRemainingAvg / metrics.PredictedRemainingAvg;
        if (double.IsNaN(rawMultiplier) || double.IsInfinity(rawMultiplier) || rawMultiplier <= 0)
            return null;
        if (Math.Abs(rawMultiplier - 1.0) < options.MinRawMultiplierDistance)
            return null;

        double credibility = metrics.Rows / (metrics.Rows + Math.Max(0.0, options.PriorRows));
        double shrunk = 1.0 + (rawMultiplier - 1.0) * Math.Clamp(options.Shrink, 0.0, 2.0) * credibility;
        double multiplier = Math.Clamp(shrunk, Math.Max(0.05, options.MinMultiplier), Math.Max(options.MinMultiplier, options.MaxMultiplier));

        LiveStateCorrectionFactor factor = BuildConditions(name);
        string warning = metrics.Rows < Math.Max(options.MinRows * 2, 160)
            ? $"Small correction slice sample ({metrics.Rows} rows); multiplier is shrunk toward 1.0."
            : string.Empty;

        return new LiveStateCorrectionFactor
        {
            Key = factor.Key,
            SourceSlice = name,
            Priority = factor.Priority,
            ScoreBucket = factor.ScoreBucket,
            MinMinute = factor.MinMinute,
            MaxMinute = factor.MaxMinute,
            MinCurrentGoals = factor.MinCurrentGoals,
            MaxCurrentGoals = factor.MaxCurrentGoals,
            MinMinutesSinceLastGoal = factor.MinMinutesSinceLastGoal,
            MaxMinutesSinceLastGoal = factor.MaxMinutesSinceLastGoal,
            Line = factor.Line,
            MinPregameTotalLine = factor.MinPregameTotalLine,
            MaxPregameTotalLine = factor.MaxPregameTotalLine,
            Rows = metrics.Rows,
            ActualRemainingAvg = metrics.ActualRemainingAvg,
            PredictedRemainingAvg = metrics.PredictedRemainingAvg,
            Bias = metrics.Bias,
            RawMultiplier = rawMultiplier,
            Credibility = credibility,
            Multiplier = multiplier,
            Status = "Fitted",
            Warning = warning
        };
    }

    private static bool IsEligibleSlice(string name, LiveStateCorrectionFitOptions options)
    {
        if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("score_", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Equals("current_goals_2_plus", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Equals("after_goal_0_5", StringComparison.OrdinalIgnoreCase) || name.Equals("after_goal_5_10", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Equals("late_75_plus", StringComparison.OrdinalIgnoreCase))
            return true;
        if (options.IncludePregameFactors && name.StartsWith("pregame_total_", StringComparison.OrdinalIgnoreCase))
            return true;
        if (options.IncludeLineFactors && name.StartsWith("line_", StringComparison.OrdinalIgnoreCase))
            return true;
        if (options.IncludeMinuteFactors && name.StartsWith("minute_", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static LiveStateCorrectionFactor BuildConditions(string name)
    {
        if (name.Equals("all", StringComparison.OrdinalIgnoreCase))
            return new LiveStateCorrectionFactor { Key = "all", SourceSlice = name, Priority = 1 };

        if (name.StartsWith("score_", StringComparison.OrdinalIgnoreCase))
        {
            string scoreBucket = name["score_".Length..];
            return new LiveStateCorrectionFactor
            {
                Key = name,
                SourceSlice = name,
                Priority = 90,
                ScoreBucket = scoreBucket
            };
        }

        if (name.Equals("after_goal_0_5", StringComparison.OrdinalIgnoreCase))
        {
            return new LiveStateCorrectionFactor
            {
                Key = name,
                SourceSlice = name,
                Priority = 80,
                MinMinutesSinceLastGoal = 0,
                MaxMinutesSinceLastGoal = 5
            };
        }

        if (name.Equals("after_goal_5_10", StringComparison.OrdinalIgnoreCase))
        {
            return new LiveStateCorrectionFactor
            {
                Key = name,
                SourceSlice = name,
                Priority = 75,
                MinMinutesSinceLastGoal = 5,
                MaxMinutesSinceLastGoal = 10
            };
        }

        if (name.Equals("current_goals_2_plus", StringComparison.OrdinalIgnoreCase))
        {
            return new LiveStateCorrectionFactor
            {
                Key = name,
                SourceSlice = name,
                Priority = 55,
                MinCurrentGoals = 2
            };
        }

        if (name.Equals("late_75_plus", StringComparison.OrdinalIgnoreCase))
        {
            return new LiveStateCorrectionFactor
            {
                Key = name,
                SourceSlice = name,
                Priority = 40,
                MinMinute = 75
            };
        }

        if (name.StartsWith("line_", StringComparison.OrdinalIgnoreCase))
        {
            double? line = TryParseSlugDouble(name["line_".Length..]);
            return new LiveStateCorrectionFactor
            {
                Key = name,
                SourceSlice = name,
                Priority = 35,
                Line = line
            };
        }

        if (name.StartsWith("minute_", StringComparison.OrdinalIgnoreCase))
        {
            double? minute = TryParseSlugDouble(name["minute_".Length..]);
            return new LiveStateCorrectionFactor
            {
                Key = name,
                SourceSlice = name,
                Priority = 30,
                MinMinute = minute,
                MaxMinute = minute
            };
        }

        if (name.Equals("pregame_total_3_5_plus", StringComparison.OrdinalIgnoreCase))
        {
            return new LiveStateCorrectionFactor
            {
                Key = name,
                SourceSlice = name,
                Priority = 25,
                MinPregameTotalLine = 3.5
            };
        }

        if (name.Equals("pregame_total_2_5_or_lower", StringComparison.OrdinalIgnoreCase))
        {
            return new LiveStateCorrectionFactor
            {
                Key = name,
                SourceSlice = name,
                Priority = 25,
                MaxPregameTotalLine = 2.5
            };
        }

        return new LiveStateCorrectionFactor { Key = name, SourceSlice = name, Priority = 1 };
    }

    private static double? TryParseSlugDouble(string value)
    {
        string normalized = value.Replace('_', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
    }

    private static void Validate(LiveStateCorrectionFitOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SourceEvaluationSummaryPath))
            throw new ArgumentException("Source evaluation summary path is required.", nameof(options));
        if (!File.Exists(options.SourceEvaluationSummaryPath))
            throw new FileNotFoundException($"Evaluation summary JSON was not found: {options.SourceEvaluationSummaryPath}", options.SourceEvaluationSummaryPath);
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("Output path is required.", nameof(options));
        if (options.MinRows <= 0)
            throw new ArgumentException("MinRows must be positive.", nameof(options));
        if (options.PriorRows < 0)
            throw new ArgumentException("PriorRows must be non-negative.", nameof(options));
        if (options.MinMultiplier <= 0 || options.MaxMultiplier < options.MinMultiplier)
            throw new ArgumentException("Multiplier bounds are invalid.", nameof(options));
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        string json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new ArgumentException($"Could not read JSON file: {path}");
    }

    private static async Task<string> WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, cancellationToken);
        return fullPath;
    }

    private static async Task<string> WriteCsvSummaryAsync(string path, LiveStateCorrectionSet correction, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        builder.AppendLine("key,source_slice,priority,condition,rows,actual_remaining,predicted_remaining,bias,raw_multiplier,credibility,multiplier,status,warning");
        foreach (LiveStateCorrectionFactor factor in correction.Factors)
        {
            builder.Append(Csv(factor.Key)); builder.Append(',');
            builder.Append(Csv(factor.SourceSlice)); builder.Append(',');
            builder.Append(factor.Priority.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(Csv(factor.DescribeCondition())); builder.Append(',');
            builder.Append(factor.Rows.ToString(CultureInfo.InvariantCulture)); builder.Append(',');
            builder.Append(Format(factor.ActualRemainingAvg)); builder.Append(',');
            builder.Append(Format(factor.PredictedRemainingAvg)); builder.Append(',');
            builder.Append(Format(factor.Bias)); builder.Append(',');
            builder.Append(Format(factor.RawMultiplier)); builder.Append(',');
            builder.Append(Format(factor.Credibility)); builder.Append(',');
            builder.Append(Format(factor.Multiplier)); builder.Append(',');
            builder.Append(Csv(factor.Status)); builder.Append(',');
            builder.Append(Csv(factor.Warning));
            builder.AppendLine();
        }

        await File.WriteAllTextAsync(fullPath, builder.ToString(), Encoding.UTF8, cancellationToken);
        return fullPath;
    }

    private static string Format(double value)
        => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Csv(string value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
