namespace LiveTotalsHelper.Modeling;

public static class ScoreStateResolver
{
    public const string Level = "Level";
    public const string OneGoalMargin = "OneGoalMargin";
    public const string TwoGoalMargin = "TwoGoalMargin";
    public const string ThreePlusGoalMargin = "ThreePlusGoalMargin";
    public const string All = "All";

    public static string FromScore(int homeGoals, int awayGoals)
    {
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

    public static int SortKey(string state)
    {
        return state switch
        {
            Level => 0,
            OneGoalMargin => 1,
            TwoGoalMargin => 2,
            ThreePlusGoalMargin => 3,
            All => 4,
            _ => 99
        };
    }
}
