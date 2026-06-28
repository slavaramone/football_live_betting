namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class LiveMatchState
{
    public string LeagueKey { get; init; } = string.Empty;
    public double Minute { get; init; }
    public int HomeGoals { get; init; }
    public int AwayGoals { get; init; }
    public int HomeRedCards { get; init; }
    public int AwayRedCards { get; init; }
    public double? LastGoalMinute { get; init; }

    public int CurrentGoals => HomeGoals + AwayGoals;
    public int GoalDifference => Math.Abs(HomeGoals - AwayGoals);
    public int TotalRedCards => HomeRedCards + AwayRedCards;
    public string Score => $"{HomeGoals}-{AwayGoals}";
}
