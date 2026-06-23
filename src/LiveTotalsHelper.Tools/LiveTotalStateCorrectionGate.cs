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

public static class LiveTotalLateGameCorrectionMode
{
    public const string Off = "Off";
    public const string BoostUp = "BoostUp";

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals(BoostUp, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("boost-up", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("up-boost", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("attack", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("true", StringComparison.OrdinalIgnoreCase))
            return BoostUp;

        if (value.Equals(Off, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("disabled", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase))
            return Off;

        throw new ArgumentException($"Unknown late-game correction mode '{value}'. Use boost-up or off.");
    }
}

public sealed class LiveTotalLateGameCorrectionOptions
{
    public string Mode { get; set; } = LiveTotalLateGameCorrectionMode.Off;
    public int StartMinute { get; set; } = 70;
    public double FactorMultiplier { get; set; } = 1.15;
    public double MaxFactor { get; set; } = 2.50;

    public static LiveTotalLateGameCorrectionOptions Disabled() => new()
    {
        Mode = LiveTotalLateGameCorrectionMode.Off
    };

    public static LiveTotalLateGameCorrectionOptions BoostUpDefault() => new()
    {
        Mode = LiveTotalLateGameCorrectionMode.BoostUp,
        StartMinute = 70,
        FactorMultiplier = 1.15,
        MaxFactor = 2.50
    };

    public LiveTotalLateGameCorrectionOptions Normalized()
    {
        string mode = LiveTotalLateGameCorrectionMode.Normalize(Mode);
        if (StartMinute < 0 || StartMinute > 120)
            throw new ArgumentException("--late-game-start-minute must be between 0 and 120.");
        if (FactorMultiplier < 1.0 || FactorMultiplier > 5.0)
            throw new ArgumentException("--late-game-factor-multiplier must be between 1.0 and 5.0.");
        if (MaxFactor < 1.0 || MaxFactor > 10.0)
            throw new ArgumentException("--late-game-max-factor must be between 1.0 and 10.0.");

        return new LiveTotalLateGameCorrectionOptions
        {
            Mode = mode,
            StartMinute = StartMinute,
            FactorMultiplier = FactorMultiplier,
            MaxFactor = MaxFactor
        };
    }

    public string Summary()
    {
        LiveTotalLateGameCorrectionOptions normalized = Normalized();
        if (normalized.Mode == LiveTotalLateGameCorrectionMode.Off)
            return "off";

        return $"{normalized.Mode}; minute>={normalized.StartMinute}; multiplier={normalized.FactorMultiplier:0.###}; maxFactor={normalized.MaxFactor:0.###}";
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
            LiveTotalLateGameCorrectionOptions.Disabled(),
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
        return Resolve(
            correction,
            correctionScope,
            correctionDirectionGuard,
            LiveTotalLateGameCorrectionOptions.Disabled(),
            stateTrigger,
            minute,
            homeGoals,
            awayGoals);
    }

    public static LiveTotalStateCorrectionResolution Resolve(
        LiveTotalStateCorrectionFile correction,
        string correctionScope,
        string correctionDirectionGuard,
        LiveTotalLateGameCorrectionOptions? lateGameOptions,
        string stateTrigger,
        int minute,
        int homeGoals,
        int awayGoals)
    {
        string normalizedScope = LiveTotalStateCorrectionScope.Normalize(correctionScope);
        string normalizedDirectionGuard = LiveTotalStateCorrectionDirectionGuard.Normalize(correctionDirectionGuard);
        LiveTotalLateGameCorrectionOptions normalizedLateGame = (lateGameOptions ?? LiveTotalLateGameCorrectionOptions.Disabled()).Normalized();
        string normalizedTrigger = LiveTotalStateTrigger.Normalize(stateTrigger);

        if (!ShouldApply(normalizedScope, normalizedTrigger))
            return BuildDisabledResolution(normalizedScope, normalizedTrigger, minute, homeGoals, awayGoals);

        LiveTotalStateCorrectionResolution resolved = LiveTotalStateCorrectionResolver.Resolve(correction, normalizedTrigger, minute, homeGoals, awayGoals);
        resolved.Source = $"scope={normalizedScope}; direction={normalizedDirectionGuard}; lateGame={normalizedLateGame.Summary()}; {resolved.Source}";

        if (ShouldDirectionGate(normalizedDirectionGuard, resolved))
            return BuildDirectionGuardedResolution(normalizedDirectionGuard, resolved);

        return ApplyLateGameCorrection(normalizedLateGame, resolved, normalizedTrigger, minute);

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

    public static bool IsLateGameBoosted(LiveTotalStateCorrectionResolution resolution) =>
        resolution.Source.Contains("late-game boost", StringComparison.OrdinalIgnoreCase);

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

    private static LiveTotalStateCorrectionResolution ApplyLateGameCorrection(
        LiveTotalLateGameCorrectionOptions lateGameOptions,
        LiveTotalStateCorrectionResolution resolved,
        string stateTrigger,
        int minute)
    {
        if (!resolved.IsSupported ||
            lateGameOptions.Mode != LiveTotalLateGameCorrectionMode.BoostUp ||
            !stateTrigger.Equals(LiveTotalStateTrigger.FixedMinute, StringComparison.OrdinalIgnoreCase) ||
            minute < lateGameOptions.StartMinute ||
            resolved.Factor <= 1.0)
            return resolved;

        double boosted = 1.0 + ((resolved.Factor - 1.0) * lateGameOptions.FactorMultiplier);
        boosted = Math.Min(boosted, lateGameOptions.MaxFactor);
        if (Math.Abs(boosted - resolved.Factor) <= 1e-12)
            return resolved;

        return new LiveTotalStateCorrectionResolution
        {
            StateTrigger = resolved.StateTrigger,
            DetailedScoreState = resolved.DetailedScoreState,
            MinuteBand = resolved.MinuteBand,
            Factor = boosted,
            IsSupported = resolved.IsSupported,
            Source = $"late-game boost {resolved.Factor:0.###}->{boosted:0.###}; {resolved.Source}"
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
