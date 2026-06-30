using System.Globalization;

namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class LiveStateCorrectionSet
{
    public string Version { get; init; } = "live-state-correction-v1";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string League { get; init; } = string.Empty;
    public string SourceEvaluationSummaryPath { get; init; } = string.Empty;
    public LiveStateCorrectionSettings Settings { get; init; } = new();
    public List<LiveStateCorrectionFactor> Factors { get; init; } = [];

    public bool IsEffectivelyEnabled => Settings.Enabled && Factors.Count > 0;

    public static LiveStateCorrectionSet Disabled => new()
    {
        Settings = new LiveStateCorrectionSettings { Enabled = false }
    };

    public static LiveStateCorrectionSet EnabledWithoutFactors(string league = "") => new()
    {
        League = league,
        Settings = new LiveStateCorrectionSettings { Enabled = true },
        Factors = []
    };
}

public sealed class LiveStateCorrectionSettings
{
    public bool Enabled { get; init; }
    public int MinRows { get; init; } = 80;
    public double PriorRows { get; init; } = 150.0;
    public double Shrink { get; init; } = 0.8;
    public double MinMultiplier { get; init; } = 0.75;
    public double MaxMultiplier { get; init; } = 1.35;
    public string Strategy { get; init; } = "Profile-specific live-state residual correction fitted from evaluation summary slices. During v3 simulation the highest-priority matching factor multiplies both competing hazards.";
}

public sealed class LiveStateCorrectionFactor
{
    public string Key { get; init; } = string.Empty;
    public string SourceSlice { get; init; } = string.Empty;
    public int Priority { get; init; }

    public string ScoreBucket { get; init; } = string.Empty;
    public double? MinMinute { get; init; }
    public double? MaxMinute { get; init; }
    public int? MinCurrentGoals { get; init; }
    public int? MaxCurrentGoals { get; init; }
    public double? MinMinutesSinceLastGoal { get; init; }
    public double? MaxMinutesSinceLastGoal { get; init; }
    public double? Line { get; init; }
    public double? MinPregameTotalLine { get; init; }
    public double? MaxPregameTotalLine { get; init; }

    public int Rows { get; init; }
    public double ActualRemainingAvg { get; init; }
    public double PredictedRemainingAvg { get; init; }
    public double Bias { get; init; }
    public double RawMultiplier { get; init; } = 1.0;
    public double Credibility { get; init; }
    public double Multiplier { get; init; } = 1.0;
    public string Status { get; init; } = string.Empty;
    public string Warning { get; init; } = string.Empty;

    public string DescribeCondition()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ScoreBucket))
            parts.Add($"score={ScoreBucket}");
        if (MinCurrentGoals.HasValue || MaxCurrentGoals.HasValue)
            parts.Add($"goals={FormatNullable(MinCurrentGoals)}..{FormatNullable(MaxCurrentGoals)}");
        if (MinMinute.HasValue || MaxMinute.HasValue)
            parts.Add($"minute={FormatNullable(MinMinute)}..{FormatNullable(MaxMinute)}");
        if (MinMinutesSinceLastGoal.HasValue || MaxMinutesSinceLastGoal.HasValue)
            parts.Add($"since_goal={FormatNullable(MinMinutesSinceLastGoal)}..{FormatNullable(MaxMinutesSinceLastGoal)}");
        if (Line.HasValue)
            parts.Add($"line={Line.Value.ToString("0.##", CultureInfo.InvariantCulture)}");
        if (MinPregameTotalLine.HasValue || MaxPregameTotalLine.HasValue)
            parts.Add($"pregame_line={FormatNullable(MinPregameTotalLine)}..{FormatNullable(MaxPregameTotalLine)}");
        return parts.Count == 0 ? "all" : string.Join(";", parts);
    }

    private static string FormatNullable(double? value)
        => value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : "*";

    private static string FormatNullable(int? value)
        => value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "*";
}

public sealed class LiveStateCorrectionAdjustment
{
    public bool Enabled { get; init; }
    public bool Applied { get; init; }
    public string Status { get; init; } = string.Empty;
    public string FactorKey { get; init; } = string.Empty;
    public string SourceSlice { get; init; } = string.Empty;
    public double Multiplier { get; init; } = 1.0;
    public string Warning { get; init; } = string.Empty;

    public static LiveStateCorrectionAdjustment Disabled => new()
    {
        Enabled = false,
        Applied = false,
        Status = "Disabled",
        Multiplier = 1.0
    };

    public static LiveStateCorrectionAdjustment Neutral(string status, string warning = "") => new()
    {
        Enabled = true,
        Applied = false,
        Status = status,
        Multiplier = 1.0,
        Warning = warning
    };
}
