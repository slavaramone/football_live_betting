namespace LiveTotalsHelper.Modeling;

public static class ScoreStateResolver
{
    public const string NilNil = "NilNil";
    public const string LevelWithGoals = "LevelWithGoals";
    public const string Level = "Level";
    public const string OneGoalMargin = "OneGoalMargin";
    public const string TwoGoalMargin = "TwoGoalMargin";
    public const string ThreePlusGoalMargin = "ThreePlusGoalMargin";
    public const string All = "All";

    /// <summary>
    /// Legacy broad score-state resolver. Keeps old model JSONs and old CSVs compatible.
    /// </summary>
    public static string FromScore(int homeGoals, int awayGoals)
    {
        return FromAbsoluteGoalDifference(Math.Abs(homeGoals - awayGoals));
    }

    /// <summary>
    /// New detailed resolver for live totals. Splits 0-0 from other level scores.
    /// </summary>
    public static string FromScoreDetailed(int homeGoals, int awayGoals)
    {
        if (homeGoals == 0 && awayGoals == 0)
            return NilNil;

        if (homeGoals == awayGoals)
            return LevelWithGoals;

        return FromAbsoluteGoalDifference(Math.Abs(homeGoals - awayGoals));
    }

    public static string FromAbsoluteGoalDifference(int absGoalDifference)
    {
        return absGoalDifference switch
        {
            0 => Level,
            1 => OneGoalMargin,
            2 => TwoGoalMargin,
            _ => ThreePlusGoalMargin
        };
    }

    /// <summary>
    /// Fallback order for detailed models. This lets new pricing use detailed groups when present
    /// and safely fall back to legacy broad groups for older model files.
    /// </summary>
    public static IReadOnlyList<string> FallbackCandidates(string state)
    {
        return state switch
        {
            NilNil => [NilNil, Level, All],
            LevelWithGoals => [LevelWithGoals, Level, All],
            Level => [Level, All],
            OneGoalMargin => [OneGoalMargin, All],
            TwoGoalMargin => [TwoGoalMargin, All],
            ThreePlusGoalMargin => [ThreePlusGoalMargin, All],
            All => [All],
            _ => [state, All]
        };
    }

    public static int SortKey(string state)
    {
        return state switch
        {
            NilNil => 0,
            LevelWithGoals => 1,
            Level => 2,
            OneGoalMargin => 3,
            TwoGoalMargin => 4,
            ThreePlusGoalMargin => 5,
            All => 6,
            _ => 99
        };
    }
}
