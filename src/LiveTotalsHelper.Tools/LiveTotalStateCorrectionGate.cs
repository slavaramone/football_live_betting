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


public static class LiveTotalStateCorrectionDirectionGuard
{
    public const string UpOnly = "UpOnly";
    public const string Both = "Both";

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals(UpOnly, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("up-only", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("positive-only", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("increase-only", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("up", StringComparison.OrdinalIgnoreCase))
            return UpOnly;

        if (value.Equals(Both, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("both", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("disabled", StringComparison.OrdinalIgnoreCase))
            return Both;

        throw new ArgumentException($"Unknown state correction direction guard '{value}'. Use up-only or both.");
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
        return Resolve(
            correction,
            correctionScope,
            LiveTotalStateCorrectionDirectionGuard.UpOnly,
            stateTrigger,
            minute,
            homeGoals,
            awayGoals);
    }

    public static LiveTotalStateCorrectionResolution Resolve(
        LiveTotalStateCorrectionFile correction,
        string correctionScope,
        string correctionDirectionGuard,
        string stateTrigger,
        int minute,
        int homeGoals,
        int awayGoals)
    {
        string normalizedScope = LiveTotalStateCorrectionScope.Normalize(correctionScope);
        string normalizedDirectionGuard = LiveTotalStateCorrectionDirectionGuard.Normalize(correctionDirectionGuard);
        string normalizedTrigger = LiveTotalStateTrigger.Normalize(stateTrigger);

        if (!ShouldApply(normalizedScope, normalizedTrigger))
            return BuildDisabledResolution(normalizedScope, normalizedTrigger, minute, homeGoals, awayGoals);

        LiveTotalStateCorrectionResolution resolved = LiveTotalStateCorrectionResolver.Resolve(correction, normalizedTrigger, minute, homeGoals, awayGoals);
        resolved.Source = $"scope={normalizedScope}; direction={normalizedDirectionGuard}; {resolved.Source}";

        if (ShouldDirectionGate(normalizedDirectionGuard, resolved))
            return BuildDirectionGuardedResolution(normalizedDirectionGuard, resolved);

        return resolved;
    }

    public static bool IsApplied(LiveTotalStateCorrectionResolution resolution) =>
        resolution.IsSupported &&
        !resolution.Source.Contains("disabled", StringComparison.OrdinalIgnoreCase) &&
        Math.Abs(resolution.Factor - 1.0) > 1e-12;

    public static bool IsGatedOut(LiveTotalStateCorrectionResolution resolution) =>
        resolution.Source.Contains("disabled by correction scope", StringComparison.OrdinalIgnoreCase) ||
        resolution.Source.Contains("disabled by correction direction", StringComparison.OrdinalIgnoreCase);

    public static bool IsDirectionGatedOut(LiveTotalStateCorrectionResolution resolution) =>
        resolution.Source.Contains("disabled by correction direction", StringComparison.OrdinalIgnoreCase);

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

    private static bool ShouldDirectionGate(string correctionDirectionGuard, LiveTotalStateCorrectionResolution resolved)
    {
        if (!resolved.IsSupported)
            return false;

        return correctionDirectionGuard switch
        {
            LiveTotalStateCorrectionDirectionGuard.UpOnly => resolved.Factor < 1.0,
            LiveTotalStateCorrectionDirectionGuard.Both => false,
            _ => false
        };
    }

    private static LiveTotalStateCorrectionResolution BuildDirectionGuardedResolution(
        string correctionDirectionGuard,
        LiveTotalStateCorrectionResolution resolved)
    {
        return new LiveTotalStateCorrectionResolution
        {
            StateTrigger = resolved.StateTrigger,
            DetailedScoreState = resolved.DetailedScoreState,
            MinuteBand = resolved.MinuteBand,
            Factor = 1.0,
            IsSupported = true,
            Source = $"disabled by correction direction '{correctionDirectionGuard}' for {resolved.Source}; original factor={resolved.Factor:0.###}"
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
