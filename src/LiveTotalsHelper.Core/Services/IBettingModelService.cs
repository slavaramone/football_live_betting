using LiveTotalsHelper.Core.Models;

namespace LiveTotalsHelper.Core.Services;

public interface IBettingModelService
{
    ModelSummary CalculateSummary(MatchSnapshot match, OddsInput odds);
    IReadOnlyList<BetDecision> CalculateDecisions(MatchSnapshot match, OddsInput odds, ModelSummary summary);
}
