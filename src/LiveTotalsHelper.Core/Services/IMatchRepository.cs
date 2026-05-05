using LiveTotalsHelper.Core.Models;

namespace LiveTotalsHelper.Core.Services;

public interface IMatchRepository
{
    IReadOnlyList<MatchSnapshot> GetLiveMatches(string league);
}
