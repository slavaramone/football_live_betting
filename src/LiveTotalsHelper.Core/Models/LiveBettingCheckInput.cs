namespace LiveTotalsHelper.Core.Models;

public sealed class LiveBettingCheckInput
{
    public string ProfileKey { get; set; } = "china-super-league";
    public string MatchName { get; set; } = string.Empty;
    public string StateTrigger { get; set; } = "fixed-minute";
    public int Minute { get; set; } = 60;
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    public int HomeRedCards { get; set; }
    public int AwayRedCards { get; set; }
    public int LastGoalMinute { get; set; } = -1;
    public int RecentGoalMinutes { get; set; } = 2;
    public int? BeforeRound { get; set; }

    public double StartingLine { get; set; } = 2.5;
    public double StartingOverOdds { get; set; } = 1.90;
    public double StartingUnderOdds { get; set; } = 1.90;

    public double LiveOddsLine { get; set; } = 2.5;
    public double LiveOverOdds { get; set; } = 2.30;
    public double LiveUnderOdds { get; set; } = 1.65;
    public string LiveOverOddsText { get; set; } = "2.5=2.30";
    public string LiveUnderOddsText { get; set; } = "2.5=1.65";
    public string TargetLinesText { get; set; } = "2.5,3.5";

    public double SelectedBetLine { get; set; } = 2.5;
    public string SelectedBetLineText { get; set; } = "2.5";
    public string SelectedBetSide { get; set; } = "UNDER";
    public double SelectedBetOdds { get; set; } = 1.90;
    public double Stake { get; set; } = 2000;
    public string BetMode { get; set; } = "Paper";
    public string BetNotes { get; set; } = string.Empty;
}
