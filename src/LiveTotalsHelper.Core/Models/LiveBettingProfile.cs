namespace LiveTotalsHelper.Core.Models;

public sealed class LiveBettingProfile
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string RiskLevel { get; init; } = "Paper test";
    public bool AllowFixedMinuteBetting { get; init; } = true;
    public bool AllowAfterGoalBetting { get; init; }
    public bool AllowAfterRedCardBetting { get; init; }
    public bool UseCurrentSeasonVolume { get; init; } = true;
    public int? DefaultBeforeRound { get; init; }
    public double EdgeThreshold { get; init; } = 0.10;
    public bool UseProbabilityMoveFilter { get; init; }
    public string DecisionMode { get; init; } = "FullModel";
    public int? MinMinute { get; init; }
    public bool RequireGoalTrigger { get; init; }
    public double? MinLine { get; init; }
    public IReadOnlyList<double> TargetLines { get; init; } = [];
    public IReadOnlyList<double> AllowedLines { get; init; } = [];
    public bool FallbackBettingEnabled { get; init; } = true;
    public int LiveBettingRulesCount { get; init; }
    public string Notes { get; init; } = string.Empty;
}
