namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class MonteCarloConfig
{
    public bool Enabled { get; init; }
    public int SimulationCount { get; init; } = 20_000;
    public double StepMinutes { get; init; } = 0.25;
    public int? RandomSeed { get; init; } = 12_345;
    public int MaxRemainingGoalsTracked { get; init; } = 6;

    public double DefaultEffectiveEnd1H { get; init; } = 47.0;
    public double DefaultEffectiveEnd2H { get; init; } = 96.0;
    public double SecondHalfBaseAddedMinutes { get; init; } = 5.0;
    public double AddedMinutesPerGoal { get; init; } = 0.4;
    public double AddedMinutesPerRedCard { get; init; } = 1.0;
    public double MinSecondHalfAddedMinutes { get; init; } = 4.0;
    public double MaxSecondHalfAddedMinutes { get; init; } = 10.0;
    public double StoppageResidualMinutes { get; init; } = 1.5;

    public MonteCarloConfig WithDefaultsFrom(MonteCarloConfig fallback)
    {
        return new MonteCarloConfig
        {
            Enabled = Enabled,
            SimulationCount = SimulationCount > 0 ? SimulationCount : fallback.SimulationCount,
            StepMinutes = StepMinutes > 0 ? StepMinutes : fallback.StepMinutes,
            RandomSeed = RandomSeed ?? fallback.RandomSeed,
            MaxRemainingGoalsTracked = MaxRemainingGoalsTracked > 0 ? MaxRemainingGoalsTracked : fallback.MaxRemainingGoalsTracked,
            DefaultEffectiveEnd1H = DefaultEffectiveEnd1H > 0 ? DefaultEffectiveEnd1H : fallback.DefaultEffectiveEnd1H,
            DefaultEffectiveEnd2H = DefaultEffectiveEnd2H > 0 ? DefaultEffectiveEnd2H : fallback.DefaultEffectiveEnd2H,
            SecondHalfBaseAddedMinutes = SecondHalfBaseAddedMinutes > 0 ? SecondHalfBaseAddedMinutes : fallback.SecondHalfBaseAddedMinutes,
            AddedMinutesPerGoal = AddedMinutesPerGoal >= 0 ? AddedMinutesPerGoal : fallback.AddedMinutesPerGoal,
            AddedMinutesPerRedCard = AddedMinutesPerRedCard >= 0 ? AddedMinutesPerRedCard : fallback.AddedMinutesPerRedCard,
            MinSecondHalfAddedMinutes = MinSecondHalfAddedMinutes > 0 ? MinSecondHalfAddedMinutes : fallback.MinSecondHalfAddedMinutes,
            MaxSecondHalfAddedMinutes = MaxSecondHalfAddedMinutes > 0 ? MaxSecondHalfAddedMinutes : fallback.MaxSecondHalfAddedMinutes,
            StoppageResidualMinutes = StoppageResidualMinutes > 0 ? StoppageResidualMinutes : fallback.StoppageResidualMinutes
        };
    }
}
