using LiveTotalsHelper.Core.Models;

namespace LiveTotalsHelper.Core.Services;

public interface ILiveBettingSessionService
{
    IReadOnlyList<LiveBettingProfile> GetProfiles();
    LiveBettingProfile? FindProfileByLeague(string league);
    Task<LiveBettingCheckResult> BuildCheckAsync(LiveBettingCheckInput input, CancellationToken cancellationToken = default);
    string AppendPaperLog(LiveBettingCheckInput input, LiveBettingCheckResult result);
    string LogBet(LiveBettingCheckInput input, LiveBettingCheckResult result);
}
