using LiveTotalsHelper.Core.Models;
using LiveTotalsHelper.Core.Services;

namespace LiveTotalsHelper.Modeling;

public sealed class BettingModelService : IBettingModelService
{
    private static readonly double[] TargetLines = [1.5, 2.0, 2.5, 3.0];
    private readonly IWeibullParameterProvider _weibullParameters;

    public BettingModelService(IWeibullParameterProvider weibullParameters)
    {
        _weibullParameters = weibullParameters;
    }

    public ModelSummary CalculateSummary(MatchSnapshot match, OddsInput odds)
    {
        double preMatchTotalXg = InferPoissonMeanFromOverUnder(
            odds.PreTotalLine,
            odds.PreOverOdds,
            odds.PreUnderOdds);

        WeibullParameters league = _weibullParameters.GetLeagueParameters(match.League);
        WeibullParameters opponents = _weibullParameters.GetOpponentParameters(match.League, match.HomeTeam, match.AwayTeam);

        double leagueRemaining = WeibullMath.RemainingShare(match.Minute, league.ShapeK, league.ScaleLambda);
        double opponentRemaining = WeibullMath.RemainingShare(match.Minute, opponents.ShapeK, opponents.ScaleLambda);

        double opponentWeight = OpponentWeight(opponents.SampleMatches);
        double mixedRemaining = (1.0 - opponentWeight) * leagueRemaining + opponentWeight * opponentRemaining;

        double remainingXg = preMatchTotalXg
            * mixedRemaining
            * ScoreStateFactor(match.HomeGoals, match.AwayGoals)
            * RedCardFactor(match.HomeRedCards, match.AwayRedCards);

        return new ModelSummary
        {
            PreMatchTotalXg = preMatchTotalXg,
            LeagueRemainingShare = leagueRemaining,
            OpponentRemainingShare = opponentRemaining,
            MixedRemainingShare = mixedRemaining,
            RemainingXg = remainingXg
        };
    }

    public IReadOnlyList<BetDecision> CalculateDecisions(MatchSnapshot match, OddsInput odds, ModelSummary summary)
    {
        var bookOdds = new Dictionary<double, double>
        {
            [1.5] = odds.LiveOverOdds15,
            [2.0] = odds.LiveOverOdds20,
            [2.5] = odds.LiveOverOdds25,
            [3.0] = odds.LiveOverOdds30
        };

        int currentGoals = match.HomeGoals + match.AwayGoals;
        var decisions = new List<BetDecision>();

        foreach (double line in TargetLines)
        {
            double probability = OverProbability(line, currentGoals, summary.RemainingXg);
            double fairOdds = probability <= 0 ? 999.0 : 1.0 / probability;
            double offeredOdds = bookOdds[line];
            double edgePercent = ((offeredOdds / fairOdds) - 1.0) * 100.0;

            decisions.Add(new BetDecision
            {
                Line = line,
                BookOverOdds = offeredOdds,
                ModelOverProbability = probability,
                FairOverOdds = fairOdds,
                EdgePercent = edgePercent,
                Decision = edgePercent >= 5.0 ? "BET" : edgePercent >= 2.0 ? "LEAN" : "NO"
            });
        }

        return decisions;
    }

    private static double OpponentWeight(int sampleMatches)
    {
        if (sampleMatches < 10) return 0.15;
        if (sampleMatches < 20) return 0.30;
        if (sampleMatches < 40) return 0.40;
        return 0.50;
    }

    private static double FairOverProbability(double overOdds, double underOdds)
    {
        double rawOver = 1.0 / overOdds;
        double rawUnder = 1.0 / underOdds;
        return rawOver / (rawOver + rawUnder);
    }

    private static double InferPoissonMeanFromOverUnder(double line, double overOdds, double underOdds)
    {
        double target = FairOverProbability(overOdds, underOdds);
        double low = 0.05;
        double high = 8.0;

        for (int i = 0; i < 80; i++)
        {
            double mid = (low + high) / 2.0;
            double probability = OverProbability(line, 0, mid);

            if (probability < target)
                low = mid;
            else
                high = mid;
        }

        return (low + high) / 2.0;
    }

    private static double OverProbability(double line, int currentGoals, double remainingLambda)
    {
        int targetGoals = (int)Math.Floor(line) + 1;
        int neededGoals = targetGoals - currentGoals;

        if (neededGoals <= 0)
            return 1.0;

        return 1.0 - PoissonMath.Cdf(neededGoals - 1, remainingLambda);
    }

    private static double ScoreStateFactor(int homeGoals, int awayGoals)
    {
        int diff = Math.Abs(homeGoals - awayGoals);
        return diff switch
        {
            0 => 1.00,
            1 => 1.07,
            2 => 1.12,
            _ => 1.00
        };
    }

    private static double RedCardFactor(int homeReds, int awayReds)
    {
        int totalReds = homeReds + awayReds;
        return totalReds switch
        {
            0 => 1.00,
            1 => 1.08,
            _ => 1.15
        };
    }
}
