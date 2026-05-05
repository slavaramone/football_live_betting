using LiveTotalsHelper.Core.Models;

namespace LiveTotalsHelper.Core.Services;

public interface IWeibullParameterProvider
{
    WeibullParameters GetLeagueParameters(string league);
    WeibullParameters GetOpponentParameters(string league, string homeTeam, string awayTeam);
}
