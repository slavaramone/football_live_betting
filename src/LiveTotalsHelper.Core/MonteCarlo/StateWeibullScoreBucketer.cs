namespace LiveTotalsHelper.Core.MonteCarlo;

public static class StateWeibullScoreBucketer
{
    public static IReadOnlyList<string> StandardBuckets { get; } =
    [
        "draw_0_0",
        "draw_1_1_plus",
        "margin1_total1_2",
        "margin1_total3_plus",
        "margin2",
        "margin3_plus"
    ];

    public static string ResolveScoreBucket(int homeGoals, int awayGoals)
    {
        if (homeGoals < 0)
            throw new ArgumentOutOfRangeException(nameof(homeGoals));
        if (awayGoals < 0)
            throw new ArgumentOutOfRangeException(nameof(awayGoals));

        int totalGoals = homeGoals + awayGoals;
        int margin = Math.Abs(homeGoals - awayGoals);

        if (margin == 0)
            return totalGoals == 0 ? "draw_0_0" : "draw_1_1_plus";

        if (margin == 1)
            return totalGoals <= 2 ? "margin1_total1_2" : "margin1_total3_plus";

        if (margin == 2)
            return "margin2";

        return "margin3_plus";
    }

    public static string ResolveExactScore(int homeGoals, int awayGoals)
        => $"{homeGoals}-{awayGoals}";
}
