using LiveTotalsHelper.Core.Models;
using LiveTotalsHelper.Core.Services;

namespace LiveTotalsHelper.Infrastructure;

public sealed class SampleWeibullParameterProvider : IWeibullParameterProvider
{
    public WeibullParameters GetLeagueParameters(string league)
        => league switch
        {
            "NPL NSW" => new WeibullParameters(1.38, 73.0, 60, "League-wide NPL NSW"),
            "NPL Victoria" => new WeibullParameters(1.34, 72.0, 60, "League-wide NPL Victoria"),
            _ => new WeibullParameters(1.35, 72.0, 60, "League-wide NPL Queensland")
        };

    public WeibullParameters GetOpponentParameters(string league, string homeTeam, string awayTeam)
    {
        // Placeholder until fitted from historical goal minutes.
        // The sample size drives shrinkage inside the model service.
        return new WeibullParameters(1.45, 74.0, 14, $"{homeTeam} + {awayTeam}");
    }
}
