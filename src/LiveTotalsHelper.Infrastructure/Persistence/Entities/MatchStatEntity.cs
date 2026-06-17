namespace LiveTotalsHelper.Infrastructure.Persistence.Entities;

public sealed class MatchStatEntity
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public string EventId { get; set; } = string.Empty;

    public string Period { get; set; } = string.Empty;

    public double? HomeExpectedGoals { get; set; }
    public double? AwayExpectedGoals { get; set; }
    public double? HomeBallPossession { get; set; }
    public double? AwayBallPossession { get; set; }
    public double? HomeTotalShots { get; set; }
    public double? AwayTotalShots { get; set; }
    public double? HomeShotsOnTarget { get; set; }
    public double? AwayShotsOnTarget { get; set; }
    public double? HomeShotsOffTarget { get; set; }
    public double? AwayShotsOffTarget { get; set; }
    public double? HomeBlockedShots { get; set; }
    public double? AwayBlockedShots { get; set; }
    public double? HomeBigChances { get; set; }
    public double? AwayBigChances { get; set; }
    public double? HomeBigChancesMissed { get; set; }
    public double? AwayBigChancesMissed { get; set; }
    public double? HomeCornerKicks { get; set; }
    public double? AwayCornerKicks { get; set; }
    public double? HomeFouls { get; set; }
    public double? AwayFouls { get; set; }
    public double? HomeYellowCards { get; set; }
    public double? AwayYellowCards { get; set; }
    public double? HomeRedCards { get; set; }
    public double? AwayRedCards { get; set; }
    public double? HomeGoalkeeperSaves { get; set; }
    public double? AwayGoalkeeperSaves { get; set; }
    public double? HomeOffsides { get; set; }
    public double? AwayOffsides { get; set; }
    public double? HomeThrowIns { get; set; }
    public double? AwayThrowIns { get; set; }
    public double? HomeFreeKicks { get; set; }
    public double? AwayFreeKicks { get; set; }
    public double? HomePasses { get; set; }
    public double? AwayPasses { get; set; }
    public double? HomeAccuratePasses { get; set; }
    public double? AwayAccuratePasses { get; set; }
    public double? HomeLongBalls { get; set; }
    public double? AwayLongBalls { get; set; }
    public double? HomeCrosses { get; set; }
    public double? AwayCrosses { get; set; }
    public double? HomeTackles { get; set; }
    public double? AwayTackles { get; set; }
    public double? HomeClearances { get; set; }
    public double? AwayClearances { get; set; }
    public double? HomeTouchesInOppositionBox { get; set; }
    public double? AwayTouchesInOppositionBox { get; set; }
    public double? HomeFinalThirdEntries { get; set; }
    public double? AwayFinalThirdEntries { get; set; }

    public string StatisticsJsonPath { get; set; } = string.Empty;
    public DateTimeOffset ImportedAtUtc { get; set; }

    public MatchEntity? Match { get; set; }
}
