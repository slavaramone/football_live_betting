namespace LiveTotalsHelper.Tools;

public static class LiveTotalGoalChangeClassifier
{
    public const string None = "";
    public const string GoAheadGoal = "GoAheadGoal";
    public const string Equalizer = "Equalizer";
    public const string MarginIncrease = "MarginIncrease";
    public const string MarginDecrease = "MarginDecrease";

    public static string Classify(int beforeHomeGoals, int beforeAwayGoals, int afterHomeGoals, int afterAwayGoals)
    {
        int beforeDiff = beforeHomeGoals - beforeAwayGoals;
        int afterDiff = afterHomeGoals - afterAwayGoals;

        if (beforeDiff == 0 && afterDiff != 0)
            return GoAheadGoal;

        if (beforeDiff != 0 && afterDiff == 0)
            return Equalizer;

        int beforeAbs = Math.Abs(beforeDiff);
        int afterAbs = Math.Abs(afterDiff);

        if (afterAbs > beforeAbs)
            return MarginIncrease;

        if (afterAbs < beforeAbs)
            return MarginDecrease;

        return None;
    }

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return None;

        return value.Trim().ToLowerInvariant() switch
        {
            "goaheadgoal" or "go-ahead" or "goahead" or "go_ahead" or "go-ahead-goal" => GoAheadGoal,
            "equalizer" or "equaliser" => Equalizer,
            "marginincrease" or "margin-increase" or "increase" => MarginIncrease,
            "margindecrease" or "margin-decrease" or "decrease" => MarginDecrease,
            _ => value.Trim()
        };
    }
}
