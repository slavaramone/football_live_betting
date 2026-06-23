namespace LiveTotalsHelper.Tools;

public static class LiveTotalStateCorrectionScope
{
    public const string All = "All";
    public const string FixedMinute = "FixedMinute";
    public const string None = "None";

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals(FixedMinute, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("fixed", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("fixed-minute", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("fixedminute", StringComparison.OrdinalIgnoreCase))
            return FixedMinute;

        if (value.Equals(All, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("all-triggers", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("alltriggers", StringComparison.OrdinalIgnoreCase))
            return All;

        if (value.Equals(None, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("disabled", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("no", StringComparison.OrdinalIgnoreCase))
            return None;

        throw new ArgumentException($"Unknown state correction scope '{value}'. Use fixed-minute, all, or none.");
    }
}

public static class LiveTotalStateCorrectionGate
{
    public static LiveTotalStateCorrectionResolution Resolve(
        LiveTotalStateCorrectionFile correction,
        string correctionScope,
        string stateTrigger,
        int minute,
        int homeGoals,
        int awayGoals)
    {
        string normalizedScope = LiveTotalStateCorrectionScope.Normalize(correctionScope);
        string normalizedTrigger = LiveTotalStateTrigger.Normalize(stateTrigger);

        if (!ShouldApply(normalizedScope, normalizedTrigger))
            return BuildDisabledResolution(normalizedScope, normalizedTrigger, minute, homeGoals, awayGoals);

        LiveTotalStateCorrectionResolution resolved = LiveTotalStateCorrectionResolver.Resolve(correction, normalizedTrigger, minute, homeGoals, awayGoals);
        resolved.Source = $"scope={normalizedScope}; {resolved.Source}";
        return resolved;
    }

    public static bool IsApplied(LiveTotalStateCorrectionResolution resolution) =>
        resolution.IsSupported &&
        resolution.Source.StartsWith("scope=", StringComparison.OrdinalIgnoreCase) &&
        !resolution.Source.Contains("disabled", StringComparison.OrdinalIgnoreCase);

    public static bool IsGatedOut(LiveTotalStateCorrectionResolution resolution) =>
        resolution.Source.Contains("disabled by correction scope", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldApply(string correctionScope, string stateTrigger)
    {
        return correctionScope switch
        {
            LiveTotalStateCorrectionScope.All => true,
            LiveTotalStateCorrectionScope.FixedMinute => stateTrigger.Equals(LiveTotalStateTrigger.FixedMinute, StringComparison.OrdinalIgnoreCase),
            LiveTotalStateCorrectionScope.None => false,
            _ => false
        };
    }

    private static LiveTotalStateCorrectionResolution BuildDisabledResolution(
        string correctionScope,
        string stateTrigger,
        int minute,
        int homeGoals,
        int awayGoals)
    {
        return new LiveTotalStateCorrectionResolution
        {
            StateTrigger = stateTrigger,
            DetailedScoreState = LiveTotalStateCorrectionResolver.DetailedScoreState(homeGoals, awayGoals),
            MinuteBand = LiveTotalStateCorrectionResolver.MinuteBand(stateTrigger, minute),
            Factor = 1.0,
            IsSupported = true,
            Source = $"disabled by correction scope '{correctionScope}' for trigger '{stateTrigger}'"
        };
    }
}
