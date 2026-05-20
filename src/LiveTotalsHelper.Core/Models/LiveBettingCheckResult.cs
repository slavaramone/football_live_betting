namespace LiveTotalsHelper.Core.Models;

public sealed class LiveBettingCheckResult
{
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.Now;
    public bool IsBettingAllowed { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Warnings { get; init; } = string.Empty;
    public string ModelSummary { get; init; } = string.Empty;
    public string DecisionRulesSummary { get; init; } = string.Empty;
    public double RemainingXg { get; init; }
    public double StateCorrectionFactor { get; init; }
    public string StateCorrectionSource { get; init; } = string.Empty;
    public bool StateCorrectionSupported { get; init; }
    public double VolumeFactor { get; init; }
    public string VolumeFactorSource { get; init; } = string.Empty;
    public IReadOnlyList<LiveBettingDecisionRow> Decisions { get; init; } = [];
}
