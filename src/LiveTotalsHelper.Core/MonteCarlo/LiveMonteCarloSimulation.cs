using System.Globalization;

namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class LiveMonteCarloSimulationOptions
{
    public LiveMonteCarloRequest Request { get; init; } = new();
    public StateWeibullCurveSet Curves { get; init; } = new();
    public NextGoalSideModelSet NextGoalSideModel { get; init; } = new();
    public double EffectiveEndMinute { get; init; }
    public int TracePathCount { get; init; }
}

public sealed class LiveMonteCarloSimulationResult
{
    public string ModelVersion { get; init; } = "v2-total-hazard";
    public string League { get; init; } = string.Empty;
    public double StartMinute { get; init; }
    public double EffectiveEndMinute { get; init; }
    public int StartHomeGoals { get; init; }
    public int StartAwayGoals { get; init; }
    public string StartScore => $"{StartHomeGoals}-{StartAwayGoals}";
    public double Line { get; init; }
    public double? OverOdds { get; init; }
    public double? UnderOdds { get; init; }
    public int CurrentGoals => StartHomeGoals + StartAwayGoals;
    public int NeededGoalsForOver { get; init; }

    public int SimulationCount { get; init; }
    public double StepMinutes { get; init; }
    public int? RandomSeed { get; init; }

    public double ExpectedRemainingGoals { get; init; }
    public double? ExpectedHomeRemainingGoals { get; init; }
    public double? ExpectedAwayRemainingGoals { get; init; }
    public RemainingGoalsDistribution Distribution { get; init; } = new();
    public LiveMonteCarloOutcomeCounts Counts { get; init; } = new();

    public double POver { get; init; }
    public double PUnder { get; init; }
    public double PPush { get; init; }
    public double? FairOverOdds { get; init; }
    public double? FairUnderOdds { get; init; }
    public double? OverEdge { get; init; }
    public double? UnderEdge { get; init; }

    public string Explanation { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<LiveMonteCarloPathEvent> TraceEvents { get; init; } = [];
}

public sealed class LiveMonteCarloOutcomeCounts
{
    public int ZeroGoals { get; init; }
    public int OneGoal { get; init; }
    public int TwoGoals { get; init; }
    public int ThreePlusGoals { get; init; }
    public int OverWins { get; init; }
    public int UnderWins { get; init; }
    public int Pushes { get; init; }
}

public sealed class LiveMonteCarloPathEvent
{
    public int Simulation { get; init; }
    public int GoalIndex { get; init; }
    public double GoalMinute { get; init; }
    public string Scorer { get; init; } = string.Empty;
    public string ScoreBefore { get; init; } = string.Empty;
    public string ScoreAfter { get; init; } = string.Empty;
    public string ScoreBucketBefore { get; init; } = string.Empty;
    public string ScoreBucketAfter { get; init; } = string.Empty;
    public string TimeBucket { get; init; } = string.Empty;
    public string CurveStatus { get; init; } = string.Empty;
    public string CurveSource { get; init; } = string.Empty;
    public string SideProbabilitySource { get; init; } = string.Empty;
    public double ProbabilityHomeNextGoal { get; init; }
    public double ExpectedGoalsInStep { get; init; }
    public double GoalProbabilityInStep { get; init; }
    public string AfterGoalBucket { get; init; } = string.Empty;
    public double AfterGoalHomeMultiplier { get; init; } = 1.0;
    public double AfterGoalAwayMultiplier { get; init; } = 1.0;
}

public sealed class LiveHazardMonteCarloSimulator
{
    private const double Epsilon = 0.000001;

    public LiveMonteCarloSimulationResult Run(LiveMonteCarloSimulationOptions options)
    {
        LiveMonteCarloRequest request = options.Request;
        if (request.CurrentMinute < 0)
            throw new ArgumentException("Current minute must be non-negative.", nameof(options));
        if (request.SimulationCount <= 0)
            throw new ArgumentException("Simulation count must be positive.", nameof(options));
        if (request.StepMinutes <= 0)
            throw new ArgumentException("Step minutes must be positive.", nameof(options));
        if (options.EffectiveEndMinute <= request.CurrentMinute + Epsilon)
            throw new ArgumentException("Effective end minute must be greater than current minute.", nameof(options));
        if (options.Curves.Curves.Count == 0)
            throw new ArgumentException("Curve set contains no curves.", nameof(options));
        if (options.NextGoalSideModel.Estimates.Count == 0)
            throw new ArgumentException("Next-goal-side model contains no estimates.", nameof(options));

        double maxCurveEnd = options.Curves.Curves.Max(x => x.BucketEndMinute);
        double effectiveEnd = Math.Min(options.EffectiveEndMinute, maxCurveEnd);
        if (effectiveEnd <= request.CurrentMinute + Epsilon)
            throw new ArgumentException($"Current minute {Format(request.CurrentMinute)} is outside fitted curve horizon ending at {Format(maxCurveEnd)}.", nameof(options));

        var warnings = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.EffectiveEndMinute > maxCurveEnd + Epsilon)
            warnings.Add($"Effective end {Format(options.EffectiveEndMinute)} is beyond last fitted curve bucket {Format(maxCurveEnd)}; simulation capped at {Format(effectiveEnd)}.");

        if (IsIntegerLine(request.Line))
            warnings.Add("Integer total line detected; push probability is reported separately. Fair odds are calculated from win probability only.");

        var rng = request.RandomSeed.HasValue ? new Random(request.RandomSeed.Value) : new Random();
        int p0Count = 0;
        int p1Count = 0;
        int p2Count = 0;
        int p3PlusCount = 0;
        int overCount = 0;
        int underCount = 0;
        int pushCount = 0;
        long remainingGoalSum = 0;
        var traceEvents = new List<LiveMonteCarloPathEvent>();

        int neededGoalsForOver = Math.Max(0, (int)Math.Floor(request.Line) + 1 - request.CurrentGoals);
        int tracePathCount = Math.Max(0, options.TracePathCount);

        for (int simulation = 1; simulation <= request.SimulationCount; simulation++)
        {
            int homeGoals = request.HomeGoals;
            int awayGoals = request.AwayGoals;
            int remainingGoals = 0;
            int goalIndex = 0;
            double minute = request.CurrentMinute;

            while (minute < effectiveEnd - Epsilon)
            {
                string scoreBucket = StateWeibullScoreBucketer.ResolveScoreBucket(homeGoals, awayGoals);
                StateWeibullCurve curve = ResolveCurve(options.Curves, scoreBucket, minute)
                    ?? throw new InvalidOperationException($"No Weibull curve found for score bucket '{scoreBucket}' at minute {Format(minute)}.");

                if (!string.IsNullOrWhiteSpace(curve.Warning))
                    warnings.Add($"{curve.ScoreBucket}/{curve.TimeBucket}: {curve.Warning}");
                else if (!curve.Status.Equals("ExactSupported", StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"{curve.ScoreBucket}/{curve.TimeBucket}: curve status {curve.Status}, source {curve.CurveSource}.");

                double segmentEnd = Math.Min(effectiveEnd, Math.Min(minute + request.StepMinutes, curve.BucketEndMinute));
                if (segmentEnd <= minute + Epsilon)
                {
                    minute = Math.Min(effectiveEnd, minute + request.StepMinutes);
                    continue;
                }

                double expectedGoalsInStep = ExpectedGoalsBetween(curve, minute, segmentEnd);
                double pGoal = 1.0 - Math.Exp(-expectedGoalsInStep);

                if (rng.NextDouble() < pGoal)
                {
                    double goalMinute = minute + rng.NextDouble() * (segmentEnd - minute);
                    NextGoalSideEstimate sideEstimate = ResolveNextGoalSide(options.NextGoalSideModel, homeGoals, awayGoals, goalMinute)
                        ?? CreateRuleBasedSideEstimate(homeGoals, awayGoals, goalMinute);

                    if (!string.IsNullOrWhiteSpace(sideEstimate.Warning))
                        warnings.Add($"{sideEstimate.DirectionalScoreBucket}/{sideEstimate.TimeBucket}: {sideEstimate.Warning}");
                    else if (!sideEstimate.Status.Equals("ExactSupported", StringComparison.OrdinalIgnoreCase))
                        warnings.Add($"{sideEstimate.DirectionalScoreBucket}/{sideEstimate.TimeBucket}: side model status {sideEstimate.Status}, source {sideEstimate.ProbabilitySource}.");

                    string scoreBefore = $"{homeGoals}-{awayGoals}";
                    string scoreBucketBefore = StateWeibullScoreBucketer.ResolveScoreBucket(homeGoals, awayGoals);
                    bool homeScores = rng.NextDouble() < sideEstimate.ProbabilityHomeNextGoal;
                    if (homeScores)
                        homeGoals++;
                    else
                        awayGoals++;

                    remainingGoals++;
                    goalIndex++;

                    if (simulation <= tracePathCount)
                    {
                        traceEvents.Add(new LiveMonteCarloPathEvent
                        {
                            Simulation = simulation,
                            GoalIndex = goalIndex,
                            GoalMinute = RoundMinute(goalMinute),
                            Scorer = homeScores ? "home" : "away",
                            ScoreBefore = scoreBefore,
                            ScoreAfter = $"{homeGoals}-{awayGoals}",
                            ScoreBucketBefore = scoreBucketBefore,
                            ScoreBucketAfter = StateWeibullScoreBucketer.ResolveScoreBucket(homeGoals, awayGoals),
                            TimeBucket = curve.TimeBucket,
                            CurveStatus = curve.Status,
                            CurveSource = curve.CurveSource,
                            SideProbabilitySource = sideEstimate.ProbabilitySource,
                            ProbabilityHomeNextGoal = sideEstimate.ProbabilityHomeNextGoal,
                            ExpectedGoalsInStep = expectedGoalsInStep,
                            GoalProbabilityInStep = pGoal
                        });
                    }
                }

                minute = segmentEnd;
            }

            remainingGoalSum += remainingGoals;
            if (remainingGoals == 0)
                p0Count++;
            else if (remainingGoals == 1)
                p1Count++;
            else if (remainingGoals == 2)
                p2Count++;
            else
                p3PlusCount++;

            int finalTotalGoals = request.CurrentGoals + remainingGoals;
            if (finalTotalGoals > request.Line)
                overCount++;
            else if (finalTotalGoals < request.Line)
                underCount++;
            else
                pushCount++;
        }

        double sims = request.SimulationCount;
        double pOver = overCount / sims;
        double pUnder = underCount / sims;
        double pPush = pushCount / sims;
        double? fairOver = pOver > 0 ? 1.0 / pOver : null;
        double? fairUnder = pUnder > 0 ? 1.0 / pUnder : null;
        if (!fairOver.HasValue)
            warnings.Add("Over win probability is zero in simulation; fair over odds are not finite.");
        if (!fairUnder.HasValue)
            warnings.Add("Under win probability is zero in simulation; fair under odds are not finite.");

        return new LiveMonteCarloSimulationResult
        {
            ModelVersion = "v2-total-hazard",
            League = string.IsNullOrWhiteSpace(options.Curves.League) ? request.LeagueKey : options.Curves.League,
            StartMinute = request.CurrentMinute,
            EffectiveEndMinute = effectiveEnd,
            StartHomeGoals = request.HomeGoals,
            StartAwayGoals = request.AwayGoals,
            Line = request.Line,
            OverOdds = request.OverOdds,
            UnderOdds = request.UnderOdds,
            NeededGoalsForOver = neededGoalsForOver,
            SimulationCount = request.SimulationCount,
            StepMinutes = request.StepMinutes,
            RandomSeed = request.RandomSeed,
            ExpectedRemainingGoals = remainingGoalSum / sims,
            Distribution = new RemainingGoalsDistribution
            {
                P0 = p0Count / sims,
                P1 = p1Count / sims,
                P2 = p2Count / sims,
                P3Plus = p3PlusCount / sims
            },
            Counts = new LiveMonteCarloOutcomeCounts
            {
                ZeroGoals = p0Count,
                OneGoal = p1Count,
                TwoGoals = p2Count,
                ThreePlusGoals = p3PlusCount,
                OverWins = overCount,
                UnderWins = underCount,
                Pushes = pushCount
            },
            POver = pOver,
            PUnder = pUnder,
            PPush = pPush,
            FairOverOdds = fairOver,
            FairUnderOdds = fairUnder,
            OverEdge = request.OverOdds.HasValue && request.OverOdds.Value > 0 ? pOver - 1.0 / request.OverOdds.Value : null,
            UnderEdge = request.UnderOdds.HasValue && request.UnderOdds.Value > 0 ? pUnder - 1.0 / request.UnderOdds.Value : null,
            Explanation = BuildExplanation(request, pOver, pUnder, pPush, fairOver, fairUnder, neededGoalsForOver),
            Warnings = warnings.Take(50).ToList(),
            TraceEvents = traceEvents
        };
    }

    private static StateWeibullCurve? ResolveCurve(StateWeibullCurveSet curveSet, string scoreBucket, double minute)
    {
        StateWeibullCurve? active = curveSet.Curves
            .Where(x => x.ScoreBucket.Equals(scoreBucket, StringComparison.OrdinalIgnoreCase)
                        && minute >= x.BucketStartMinute - Epsilon
                        && minute < x.BucketEndMinute - Epsilon)
            .OrderBy(x => x.BucketStartMinute)
            .FirstOrDefault();

        if (active is not null)
            return active;

        return curveSet.Curves
            .Where(x => x.ScoreBucket.Equals(scoreBucket, StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(minute - x.BucketEndMinute) <= Epsilon)
            .OrderByDescending(x => x.BucketEndMinute)
            .FirstOrDefault();
    }

    private static NextGoalSideEstimate? ResolveNextGoalSide(
        NextGoalSideModelSet model,
        int homeGoals,
        int awayGoals,
        double minute)
    {
        string directional = StateWeibullScoreBucketer.ResolveDirectionalScoreBucket(homeGoals, awayGoals);

        NextGoalSideEstimate? active = model.Estimates
            .Where(x => x.DirectionalScoreBucket.Equals(directional, StringComparison.OrdinalIgnoreCase)
                        && minute >= x.BucketStartMinute - Epsilon
                        && minute < x.BucketEndMinute - Epsilon)
            .OrderBy(x => x.BucketStartMinute)
            .FirstOrDefault();

        if (active is not null)
            return active;

        return model.Estimates
            .Where(x => x.DirectionalScoreBucket.Equals(directional, StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(minute - x.BucketEndMinute) <= Epsilon)
            .OrderByDescending(x => x.BucketEndMinute)
            .FirstOrDefault();
    }

    private static NextGoalSideEstimate CreateRuleBasedSideEstimate(int homeGoals, int awayGoals, double minute)
    {
        string directional = StateWeibullScoreBucketer.ResolveDirectionalScoreBucket(homeGoals, awayGoals);
        string neutral = StateWeibullScoreBucketer.ResolveScoreBucket(homeGoals, awayGoals);
        string pressure = StateWeibullScoreBucketer.ResolvePressureBucket(homeGoals, awayGoals);
        double pHome = StateWeibullScoreBucketer.RuleBasedHomeNextGoalProbability(homeGoals, awayGoals);

        return new NextGoalSideEstimate
        {
            DirectionalScoreBucket = directional,
            NeutralScoreBucket = neutral,
            PressureBucket = pressure,
            TimeBucket = "<rule_based>",
            BucketStartMinute = minute,
            BucketEndMinute = minute,
            Status = "RuleBasedFallback",
            ProbabilitySource = "rule_based",
            ProbabilityHomeNextGoal = pHome,
            FallbackProbabilityHomeNextGoal = pHome,
            RuleBasedProbabilityHomeNextGoal = pHome,
            Warning = "No fitted next-goal-side estimate found; rule-based fallback used."
        };
    }

    private static double ExpectedGoalsBetween(StateWeibullCurve curve, double fromMinute, double toMinute)
    {
        double start = Math.Max(fromMinute, curve.BucketStartMinute);
        double end = Math.Min(toMinute, curve.BucketEndMinute);
        if (end <= start + Epsilon)
            return 0.0;

        return Math.Max(0.0, CumulativeExpectedGoalsInBucket(curve, end) - CumulativeExpectedGoalsInBucket(curve, start));
    }

    private static double CumulativeExpectedGoalsInBucket(StateWeibullCurve curve, double minute)
    {
        double length = Math.Max(curve.BucketLengthMinutes, Epsilon);
        double localMinute = Math.Clamp(minute - curve.BucketStartMinute, 0.0, length);
        double x = localMinute / length;

        return curve.ExpectedGoalsInBucket * Math.Pow(x, curve.ShapeK);
    }

    private static string BuildExplanation(
        LiveMonteCarloRequest request,
        double pOver,
        double pUnder,
        double pPush,
        double? fairOver,
        double? fairUnder,
        int neededGoalsForOver)
    {
        string overNeed = neededGoalsForOver <= 0
            ? "Over is already winning at the current score"
            : $"Over {request.Line.ToString("0.##", CultureInfo.InvariantCulture)} needs {neededGoalsForOver}+ more goal(s)";

        return $"{overNeed}. MC POver={FormatProbability(pOver)}, PUnder={FormatProbability(pUnder)}, PPush={FormatProbability(pPush)}. Fair Over odds={FormatOdds(fairOver)}, fair Under odds={FormatOdds(fairUnder)}.";
    }

    private static bool IsIntegerLine(double line)
        => Math.Abs(line - Math.Round(line)) <= Epsilon;

    private static double RoundMinute(double value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string Format(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatProbability(double value)
        => value.ToString("0.00%", CultureInfo.InvariantCulture);

    private static string FormatOdds(double? value)
        => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "<none>";
}
