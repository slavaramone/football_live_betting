using System.Globalization;

namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class LiveCompetingHazardMonteCarloSimulationOptions
{
    public LiveMonteCarloRequest Request { get; init; } = new();
    public CompetingHazardCurveSet Curves { get; init; } = new();
    public double EffectiveEndMinute { get; init; }
    public int TracePathCount { get; init; }
}

public sealed class LiveCompetingHazardMonteCarloSimulator
{
    private const double Epsilon = 0.000001;

    public LiveMonteCarloSimulationResult Run(LiveCompetingHazardMonteCarloSimulationOptions options)
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
            throw new ArgumentException("Competing-hazard curve set contains no curves.", nameof(options));

        double maxCurveEnd = options.Curves.Curves.Max(x => x.BucketEndMinute);
        double effectiveEnd = Math.Min(options.EffectiveEndMinute, maxCurveEnd);
        if (effectiveEnd <= request.CurrentMinute + Epsilon)
            throw new ArgumentException($"Current minute {Format(request.CurrentMinute)} is outside fitted competing-hazard horizon ending at {Format(maxCurveEnd)}.", nameof(options));

        var warnings = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.EffectiveEndMinute > maxCurveEnd + Epsilon)
            warnings.Add($"Effective end {Format(options.EffectiveEndMinute)} is beyond last fitted competing-hazard bucket {Format(maxCurveEnd)}; simulation capped at {Format(effectiveEnd)}.");

        if (IsIntegerLine(request.Line))
            warnings.Add("Integer total line detected; push probability is reported separately. Fair odds are calculated from win probability only.");

        if (options.Curves.AfterGoalSettings.Enabled && options.Curves.AfterGoalFactors.Count == 0)
            warnings.Add("After-goal hazard factors are enabled but the competing-hazard model contains no after-goal factor rows; neutral multiplier 1.0 used.");

        var afterGoalFactors = options.Curves.AfterGoalFactors
            .ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        var rng = request.RandomSeed.HasValue ? new Random(request.RandomSeed.Value) : new Random();
        int p0Count = 0;
        int p1Count = 0;
        int p2Count = 0;
        int p3PlusCount = 0;
        int overCount = 0;
        int underCount = 0;
        int pushCount = 0;
        long remainingGoalSum = 0;
        long homeRemainingGoalSum = 0;
        long awayRemainingGoalSum = 0;
        var traceEvents = new List<LiveMonteCarloPathEvent>();

        int neededGoalsForOver = Math.Max(0, (int)Math.Floor(request.Line) + 1 - request.CurrentGoals);
        int tracePathCount = Math.Max(0, options.TracePathCount);

        for (int simulation = 1; simulation <= request.SimulationCount; simulation++)
        {
            int homeGoals = request.HomeGoals;
            int awayGoals = request.AwayGoals;
            int remainingGoals = 0;
            int homeRemainingGoals = 0;
            int awayRemainingGoals = 0;
            int goalIndex = 0;
            double minute = request.CurrentMinute;
            double? lastGoalMinute = request.LastGoalMinute;
            string lastGoalSide = NormalizeGoalSide(request.LastGoalSide);

            while (minute < effectiveEnd - Epsilon)
            {
                string directionalBucket = StateWeibullScoreBucketer.ResolveDirectionalScoreBucket(homeGoals, awayGoals);
                CompetingHazardCurve curve = ResolveCurve(options.Curves, directionalBucket, minute)
                    ?? throw new InvalidOperationException($"No competing-hazard curve found for directional score bucket '{directionalBucket}' at minute {Format(minute)}.");

                AddCurveWarning(warnings, curve);

                double segmentEnd = Math.Min(effectiveEnd, Math.Min(minute + request.StepMinutes, curve.BucketEndMinute));
                if (segmentEnd <= minute + Epsilon)
                {
                    minute = Math.Min(effectiveEnd, minute + request.StepMinutes);
                    continue;
                }

                double homeExpectedGoalsInStep = ExpectedGoalsBetween(curve, curve.Home, minute, segmentEnd);
                double awayExpectedGoalsInStep = ExpectedGoalsBetween(curve, curve.Away, minute, segmentEnd);

                AfterGoalStepAdjustment afterGoal = ResolveAfterGoalAdjustment(
                    options.Curves,
                    afterGoalFactors,
                    lastGoalMinute,
                    lastGoalSide,
                    minute);

                if (afterGoal.Factor is not null && !string.IsNullOrWhiteSpace(afterGoal.Factor.Warning))
                    warnings.Add($"after_goal/{afterGoal.Factor.Key}: {afterGoal.Factor.Warning}");

                homeExpectedGoalsInStep *= afterGoal.HomeMultiplier;
                awayExpectedGoalsInStep *= afterGoal.AwayMultiplier;

                double expectedGoalsInStep = homeExpectedGoalsInStep + awayExpectedGoalsInStep;
                double pGoal = 1.0 - Math.Exp(-expectedGoalsInStep);

                if (rng.NextDouble() < pGoal)
                {
                    double goalMinute = minute + rng.NextDouble() * (segmentEnd - minute);
                    double probabilityHomeGoal = expectedGoalsInStep > Epsilon
                        ? homeExpectedGoalsInStep / expectedGoalsInStep
                        : curve.ProbabilityHomeGoalInBucket;
                    probabilityHomeGoal = ClampProbability(probabilityHomeGoal);

                    string scoreBefore = $"{homeGoals}-{awayGoals}";
                    string scoreBucketBefore = StateWeibullScoreBucketer.ResolveScoreBucket(homeGoals, awayGoals);
                    bool homeScores = rng.NextDouble() < probabilityHomeGoal;
                    if (homeScores)
                    {
                        homeGoals++;
                        homeRemainingGoals++;
                    }
                    else
                    {
                        awayGoals++;
                        awayRemainingGoals++;
                    }

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
                            CurveStatus = $"total={curve.TotalStatus}; share={curve.ScorerShareStatus}",
                            CurveSource = $"total={curve.TotalCurveSource}; share={curve.ScorerShareSource}",
                            SideProbabilitySource = curve.ScorerShareSource,
                            ProbabilityHomeNextGoal = probabilityHomeGoal,
                            ExpectedGoalsInStep = expectedGoalsInStep,
                            GoalProbabilityInStep = pGoal,
                            AfterGoalBucket = afterGoal.BucketKey,
                            AfterGoalHomeMultiplier = afterGoal.HomeMultiplier,
                            AfterGoalAwayMultiplier = afterGoal.AwayMultiplier
                        });
                    }

                    lastGoalMinute = goalMinute;
                    lastGoalSide = homeScores ? "home" : "away";
                }

                minute = segmentEnd;
            }

            remainingGoalSum += remainingGoals;
            homeRemainingGoalSum += homeRemainingGoals;
            awayRemainingGoalSum += awayRemainingGoals;
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
            ModelVersion = options.Curves.AfterGoalSettings.Enabled && options.Curves.AfterGoalFactors.Count > 0
                ? "v3-competing-hazard-after-goal"
                : "v3-competing-hazard",
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
            ExpectedHomeRemainingGoals = homeRemainingGoalSum / sims,
            ExpectedAwayRemainingGoals = awayRemainingGoalSum / sims,
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
            Explanation = BuildExplanation(request, pOver, pUnder, pPush, fairOver, fairUnder, neededGoalsForOver, options.Curves.AfterGoalSettings.Enabled && options.Curves.AfterGoalFactors.Count > 0),
            Warnings = warnings.Take(50).ToList(),
            TraceEvents = traceEvents
        };
    }

    private static void AddCurveWarning(SortedSet<string> warnings, CompetingHazardCurve curve)
    {
        if (!string.IsNullOrWhiteSpace(curve.Warning))
            warnings.Add($"{curve.DirectionalScoreBucket}/{curve.TimeBucket}: {curve.Warning}");
        else
        {
            if (!curve.TotalStatus.Equals("ExactSupported", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"{curve.DirectionalScoreBucket}/{curve.TimeBucket}: total status {curve.TotalStatus}, source {curve.TotalCurveSource}.");
            if (!curve.ScorerShareStatus.Equals("ExactSupported", StringComparison.OrdinalIgnoreCase))
                warnings.Add($"{curve.DirectionalScoreBucket}/{curve.TimeBucket}: scorer-share status {curve.ScorerShareStatus}, source {curve.ScorerShareSource}.");
        }
    }

    private static AfterGoalStepAdjustment ResolveAfterGoalAdjustment(
        CompetingHazardCurveSet curveSet,
        IReadOnlyDictionary<string, CompetingHazardAfterGoalFactor> factors,
        double? lastGoalMinute,
        string lastGoalSide,
        double currentMinute)
    {
        if (!curveSet.AfterGoalSettings.Enabled || !lastGoalMinute.HasValue || factors.Count == 0)
            return AfterGoalStepAdjustment.Neutral;

        double minutesSinceGoal = Math.Max(0.0, currentMinute - lastGoalMinute.Value);
        CompetingHazardAfterGoalFactor? factor = curveSet.AfterGoalFactors
            .Where(x => minutesSinceGoal >= x.StartMinutesSinceGoal - Epsilon && minutesSinceGoal < x.EndMinutesSinceGoal - Epsilon)
            .OrderBy(x => x.StartMinutesSinceGoal)
            .FirstOrDefault();

        if (factor is null)
            return AfterGoalStepAdjustment.Neutral;

        string normalizedLastGoalSide = NormalizeGoalSide(lastGoalSide);
        if (normalizedLastGoalSide.Equals("home", StringComparison.OrdinalIgnoreCase))
        {
            return new AfterGoalStepAdjustment(
                factor.Key,
                ClampMultiplier(factor.SameTeamMultiplier),
                ClampMultiplier(factor.OpponentMultiplier),
                factor);
        }

        if (normalizedLastGoalSide.Equals("away", StringComparison.OrdinalIgnoreCase))
        {
            return new AfterGoalStepAdjustment(
                factor.Key,
                ClampMultiplier(factor.OpponentMultiplier),
                ClampMultiplier(factor.SameTeamMultiplier),
                factor);
        }

        double multiplier = ClampMultiplier(factor.TotalMultiplier);
        return new AfterGoalStepAdjustment(factor.Key, multiplier, multiplier, factor);
    }

    private static CompetingHazardCurve? ResolveCurve(CompetingHazardCurveSet curveSet, string directionalBucket, double minute)
    {
        CompetingHazardCurve? active = curveSet.Curves
            .Where(x => x.DirectionalScoreBucket.Equals(directionalBucket, StringComparison.OrdinalIgnoreCase)
                        && minute >= x.BucketStartMinute - Epsilon
                        && minute < x.BucketEndMinute - Epsilon)
            .OrderBy(x => x.BucketStartMinute)
            .FirstOrDefault();

        if (active is not null)
            return active;

        return curveSet.Curves
            .Where(x => x.DirectionalScoreBucket.Equals(directionalBucket, StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(minute - x.BucketEndMinute) <= Epsilon)
            .OrderByDescending(x => x.BucketEndMinute)
            .FirstOrDefault();
    }

    private static double ExpectedGoalsBetween(
        CompetingHazardCurve curve,
        CompetingHazardSideSplit side,
        double fromMinute,
        double toMinute)
    {
        double start = Math.Max(fromMinute, curve.BucketStartMinute);
        double end = Math.Min(toMinute, curve.BucketEndMinute);
        if (end <= start + Epsilon)
            return 0.0;

        return Math.Max(0.0, CumulativeExpectedGoalsInBucket(curve, side, end) - CumulativeExpectedGoalsInBucket(curve, side, start));
    }

    private static double CumulativeExpectedGoalsInBucket(
        CompetingHazardCurve curve,
        CompetingHazardSideSplit side,
        double minute)
    {
        double length = Math.Max(curve.BucketLengthMinutes, Epsilon);
        double localMinute = Math.Clamp(minute - curve.BucketStartMinute, 0.0, length);
        double x = localMinute / length;

        return side.ExpectedGoalsInBucket * Math.Pow(x, side.ShapeK);
    }

    private static string BuildExplanation(
        LiveMonteCarloRequest request,
        double pOver,
        double pUnder,
        double pPush,
        double? fairOver,
        double? fairUnder,
        int neededGoalsForOver,
        bool afterGoalEnabled)
    {
        string overNeed = neededGoalsForOver <= 0
            ? "Over is already winning at the current score"
            : $"Over {request.Line.ToString("0.##", CultureInfo.InvariantCulture)} needs {neededGoalsForOver}+ more goal(s)";

        string afterGoal = afterGoalEnabled ? " with after-goal hazard factors" : string.Empty;
        return $"{overNeed}. MC v3 competing hazards{afterGoal} POver={FormatProbability(pOver)}, PUnder={FormatProbability(pUnder)}, PPush={FormatProbability(pPush)}. Fair Over odds={FormatOdds(fairOver)}, fair Under odds={FormatOdds(fairUnder)}.";
    }

    private static bool IsIntegerLine(double line)
        => Math.Abs(line - Math.Round(line)) <= Epsilon;

    private static double ClampProbability(double value)
        => Math.Clamp(value, 0.000001, 0.999999);

    private static double ClampMultiplier(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return 1.0;
        return Math.Clamp(value, 0.05, 5.0);
    }

    private static string NormalizeGoalSide(string side)
    {
        if (side.Equals("h", StringComparison.OrdinalIgnoreCase) || side.Equals("home", StringComparison.OrdinalIgnoreCase))
            return "home";
        if (side.Equals("a", StringComparison.OrdinalIgnoreCase) || side.Equals("away", StringComparison.OrdinalIgnoreCase))
            return "away";
        return string.Empty;
    }

    private static double RoundMinute(double value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static string Format(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatProbability(double value)
        => value.ToString("0.00%", CultureInfo.InvariantCulture);

    private static string FormatOdds(double? value)
        => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "<none>";

    private sealed record AfterGoalStepAdjustment(
        string BucketKey,
        double HomeMultiplier,
        double AwayMultiplier,
        CompetingHazardAfterGoalFactor? Factor)
    {
        public static readonly AfterGoalStepAdjustment Neutral = new(string.Empty, 1.0, 1.0, null);
    }
}
