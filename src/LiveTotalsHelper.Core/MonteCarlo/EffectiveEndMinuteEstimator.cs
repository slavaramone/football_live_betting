namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class EffectiveEndMinuteEstimator
{
    public EffectiveEndMinuteEstimate Estimate(LiveMonteCarloRequest request, MonteCarloConfig config)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(config);

        if (request.CurrentMinute < 0)
            throw new ArgumentOutOfRangeException(nameof(request.CurrentMinute), "Current minute must be non-negative.");

        double effectiveEnd;
        string period;
        string reason;

        if (request.CurrentMinute < 45.0)
        {
            period = "1H";
            effectiveEnd = Math.Max(config.DefaultEffectiveEnd1H, request.CurrentMinute + 0.5);
            reason = $"1H default effective end {config.DefaultEffectiveEnd1H:0.#}.";
        }
        else
        {
            period = request.CurrentMinute >= 90.0 ? "2H stoppage" : "2H";

            int currentGoals = Math.Max(0, request.CurrentGoals);
            int redCards = Math.Max(0, request.TotalRedCards);
            double estimatedAdded = config.SecondHalfBaseAddedMinutes
                + config.AddedMinutesPerGoal * Math.Max(0, currentGoals - 1)
                + config.AddedMinutesPerRedCard * redCards;

            estimatedAdded = Math.Clamp(
                estimatedAdded,
                config.MinSecondHalfAddedMinutes,
                config.MaxSecondHalfAddedMinutes);

            double estimatedEndFromState = 90.0 + estimatedAdded;
            effectiveEnd = Math.Max(config.DefaultEffectiveEnd2H, estimatedEndFromState);

            if (request.CurrentMinute >= 90.0)
                effectiveEnd = Math.Max(effectiveEnd, request.CurrentMinute + config.StoppageResidualMinutes);
            else if (effectiveEnd <= request.CurrentMinute)
                effectiveEnd = request.CurrentMinute + 0.5;

            reason = $"2H default {config.DefaultEffectiveEnd2H:0.#}; state estimate 90+{estimatedAdded:0.#} from goals={currentGoals}, reds={redCards}.";
        }

        double remaining = Math.Max(0.0, effectiveEnd - request.CurrentMinute);
        return new EffectiveEndMinuteEstimate
        {
            CurrentMinute = request.CurrentMinute,
            EffectiveEndMinute = effectiveEnd,
            RemainingEffectiveMinutes = remaining,
            Period = period,
            Reason = reason
        };
    }
}
