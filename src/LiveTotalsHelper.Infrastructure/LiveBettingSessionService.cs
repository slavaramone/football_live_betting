using LiveTotalsHelper.Core.Models;
using LiveTotalsHelper.Core.Services;

namespace LiveTotalsHelper.Infrastructure;

/// <summary>
/// Compatibility placeholder. The Avalonia app uses
/// LiveTotalsHelper.App.Services.LiveBettingSessionService, which calls the live pricing core directly.
/// Keep this class only so older Infrastructure references do not break after the interface expansion.
/// </summary>
public sealed class LiveBettingSessionService : ILiveBettingSessionService
{
    public IReadOnlyList<LiveBettingProfile> GetProfiles() => [];

    public LiveBettingProfile? FindProfileByLeague(string league) => null;

    public Task<LiveBettingCheckResult> BuildCheckAsync(
        LiveBettingCheckInput input,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new LiveBettingCheckResult
        {
            IsBettingAllowed = false,
            Status = "NOT CONFIGURED",
            Warnings = "Use LiveTotalsHelper.App.Services.LiveBettingSessionService for Avalonia live pricing."
        });
    }

    public string AppendPaperLog(LiveBettingCheckInput input, LiveBettingCheckResult result)
    {
        throw new NotSupportedException("Use LiveTotalsHelper.App.Services.LiveBettingSessionService for logging.");
    }

    public string LogBet(LiveBettingCheckInput input, LiveBettingCheckResult result)
    {
        throw new NotSupportedException("Use LiveTotalsHelper.App.Services.LiveBettingSessionService for logging.");
    }
}
