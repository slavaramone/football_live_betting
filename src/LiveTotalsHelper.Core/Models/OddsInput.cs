namespace LiveTotalsHelper.Core.Models;

public sealed class OddsInput
{
    public double PreHomeOdds { get; set; } = 2.15;
    public double PreDrawOdds { get; set; } = 3.40;
    public double PreAwayOdds { get; set; } = 3.10;

    public double PreTotalLine { get; set; } = 2.5;
    public double PreOverOdds { get; set; } = 1.93;
    public double PreUnderOdds { get; set; } = 1.87;

    public double LiveOverOdds15 { get; set; } = 1.33;
    public double LiveOverOdds20 { get; set; } = 1.95;
    public double LiveOverOdds25 { get; set; } = 2.65;
    public double LiveOverOdds30 { get; set; } = 3.80;
}
