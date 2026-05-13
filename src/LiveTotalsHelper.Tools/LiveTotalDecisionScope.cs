namespace LiveTotalsHelper.Tools;

public static class LiveTotalDecisionScope
{
    public const string FullModel = "FullModel";
    public const string AfterGoalOnly = "AfterGoalOnly";
    public const string SecondHalfAfterGoalOnly = "SecondHalfAfterGoalOnly";

    public static readonly string[] ComparisonScopes =
    [
        FullModel,
        AfterGoalOnly,
        SecondHalfAfterGoalOnly
    ];

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("full", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("full-model", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("fullmodel", StringComparison.OrdinalIgnoreCase))
            return FullModel;

        if (value.Equals(AfterGoalOnly, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("after-goal", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("after-goal-only", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("aftergoal", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("aftergoalonly", StringComparison.OrdinalIgnoreCase))
            return AfterGoalOnly;

        if (value.Equals(SecondHalfAfterGoalOnly, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("2h-after-goal", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("2h-after-goal-only", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("second-half-after-goal", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("second-half-after-goal-only", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("secondhalfaftergoalonly", StringComparison.OrdinalIgnoreCase))
            return SecondHalfAfterGoalOnly;

        throw new ArgumentException($"Unknown decision scope '{value}'. Use full-model, after-goal-only, or 2h-after-goal-only.");
    }

    public static bool IsEligible(string scope, string stateTrigger, int minute)
    {
        scope = Normalize(scope);
        stateTrigger = LiveTotalStateTrigger.Normalize(stateTrigger);

        return scope switch
        {
            FullModel => true,
            AfterGoalOnly => stateTrigger.Equals(LiveTotalStateTrigger.AfterGoal, StringComparison.OrdinalIgnoreCase),
            SecondHalfAfterGoalOnly =>
                minute >= 46 && stateTrigger.Equals(LiveTotalStateTrigger.AfterGoal, StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    public static int Order(string scope)
    {
        scope = Normalize(scope);
        return scope switch
        {
            FullModel => 0,
            AfterGoalOnly => 1,
            SecondHalfAfterGoalOnly => 2,
            _ => 99
        };
    }
}
