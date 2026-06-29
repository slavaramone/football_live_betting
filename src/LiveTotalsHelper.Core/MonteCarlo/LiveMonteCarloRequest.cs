namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class LiveMonteCarloRequest
{
    public string LeagueKey { get; init; } = string.Empty;
    public double CurrentMinute { get; init; }

    public int HomeGoals { get; init; }
    public int AwayGoals { get; init; }

    public int HomeRedCards { get; init; }
    public int AwayRedCards { get; init; }

    public double? LastGoalMinute { get; init; }
    public string LastGoalSide { get; init; } = string.Empty;

    public double Line { get; init; }
    public double? OverOdds { get; init; }
    public double? UnderOdds { get; init; }

    public double? MarketTotal { get; init; }
    public double? PregameTotal { get; init; }
    public double? PregameTotalLine { get; init; }
    public double? PregameOverOdds { get; init; }
    public double? PregameUnderOdds { get; init; }
    public bool UseMarketBaseline { get; init; } = true;

    public int SimulationCount { get; init; }
    public double StepMinutes { get; init; }
    public int? RandomSeed { get; init; }

    public int CurrentGoals => HomeGoals + AwayGoals;
    public int GoalDifference => Math.Abs(HomeGoals - AwayGoals);
    public int TotalRedCards => HomeRedCards + AwayRedCards;

    public double? MinutesSinceLastGoal => LastGoalMinute.HasValue
        ? Math.Max(0.0, CurrentMinute - LastGoalMinute.Value)
        : null;
}
