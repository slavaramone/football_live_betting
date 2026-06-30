using System.Globalization;

namespace LiveTotalsHelper.Core.MonteCarlo;

public sealed class LiveCompetingHazardMonteCarloSimulationOptions
{
    public LiveMonteCarloRequest Request { get; init; } = new();
    public CompetingHazardCurveSet Curves { get; init; } = new();
    public LiveStateCorrectionSet LiveStateCorrection { get; init; } = LiveStateCorrectionSet.Disabled;
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
        if (options.Curves.GoalDrawSuppressionSettings.Enabled && options.Curves.GoalDrawSuppressionFactors.Count == 0)
            warnings.Add("Goal-draw suppression is enabled but the competing-hazard model contains no goal-draw factor rows; neutral multiplier 1.0 used.");
        if (request.UseLiveStateCorrection && options.LiveStateCorrection.Settings.Enabled && options.LiveStateCorrection.Factors.Count == 0)
            warnings.Add("Live-state correction is enabled but the correction model contains no factors; neutral multiplier 1.0 used.");

        LiveMarketBaselineAdjustment marketBaseline = ResolveMarketBaselineAdjustment(options.Curves, request, effectiveEnd, warnings);
        if (!string.IsNullOrWhiteSpace(marketBaseline.Warning))
            warnings.Add($"market_baseline: {marketBaseline.Warning}");

        var afterGoalFactors = options.Curves.AfterGoalFactors
            .ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var goalDrawFactors = options.Curves.GoalDrawSuppressionFactors
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

                GoalDrawStepAdjustment goalDraw = ResolveGoalDrawAdjustment(options.Curves, goalDrawFactors, curve);
                if (goalDraw.Factor is not null && !string.IsNullOrWhiteSpace(goalDraw.Factor.Warning))
                    warnings.Add($"goal_draw/{goalDraw.Factor.Key}: {goalDraw.Factor.Warning}");

                homeExpectedGoalsInStep *= goalDraw.Multiplier;
                awayExpectedGoalsInStep *= goalDraw.Multiplier;

                homeExpectedGoalsInStep *= marketBaseline.Multiplier;
                awayExpectedGoalsInStep *= marketBaseline.Multiplier;

                LiveStateCorrectionAdjustment liveStateCorrection = ResolveLiveStateCorrection(
                    options.LiveStateCorrection,
                    request,
                    homeGoals,
                    awayGoals,
                    minute,
                    lastGoalMinute);

                if (!string.IsNullOrWhiteSpace(liveStateCorrection.Warning))
                    warnings.Add($"live_state_correction/{liveStateCorrection.FactorKey}: {liveStateCorrection.Warning}");

                homeExpectedGoalsInStep *= liveStateCorrection.Multiplier;
                awayExpectedGoalsInStep *= liveStateCorrection.Multiplier;

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
                            AfterGoalAwayMultiplier = afterGoal.AwayMultiplier,
                            GoalDrawFactorKey = goalDraw.FactorKey,
                            GoalDrawMultiplier = goalDraw.Multiplier,
                            MarketBaselineMultiplier = marketBaseline.Multiplier,
                            LiveStateCorrectionFactorKey = liveStateCorrection.FactorKey,
                            LiveStateCorrectionMultiplier = liveStateCorrection.Multiplier
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
            ModelVersion = ResolveModelVersion(options.Curves, request.UseLiveStateCorrection ? options.LiveStateCorrection : LiveStateCorrectionSet.Disabled),
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
            MarketBaseline = marketBaseline,
            LiveStateCorrection = InitialLiveStateCorrection(options.LiveStateCorrection, request),
            Explanation = BuildExplanation(request, pOver, pUnder, pPush, fairOver, fairUnder, neededGoalsForOver, options.Curves.AfterGoalSettings.Enabled && options.Curves.AfterGoalFactors.Count > 0, options.Curves.GoalDrawSuppressionSettings.Enabled && options.Curves.GoalDrawSuppressionFactors.Count > 0, marketBaseline, options.LiveStateCorrection),
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

    private static string ResolveModelVersion(CompetingHazardCurveSet curves, LiveStateCorrectionSet liveStateCorrection)
    {
        bool afterGoal = curves.AfterGoalSettings.Enabled && curves.AfterGoalFactors.Count > 0;
        bool goalDraw = curves.GoalDrawSuppressionSettings.Enabled && curves.GoalDrawSuppressionFactors.Count > 0;
        bool marketBaseline = curves.MarketBaselineSettings.Enabled;
        bool stateCorrection = liveStateCorrection.Settings.Enabled && liveStateCorrection.Factors.Count > 0;

        if (afterGoal && goalDraw && marketBaseline && stateCorrection)
            return "v3-competing-hazard-after-goal-goal-draw-market-baseline-live-state-correction";
        if (afterGoal && goalDraw && marketBaseline)
            return "v3-competing-hazard-after-goal-goal-draw-market-baseline";
        if (afterGoal && goalDraw)
            return "v3-competing-hazard-after-goal-goal-draw";
        if (afterGoal)
            return "v3-competing-hazard-after-goal";
        if (goalDraw)
            return "v3-competing-hazard-goal-draw";
        return "v3-competing-hazard";
    }


    private static LiveMarketBaselineAdjustment ResolveMarketBaselineAdjustment(
        CompetingHazardCurveSet curveSet,
        LiveMonteCarloRequest request,
        double effectiveEnd,
        SortedSet<string> warnings)
    {
        CompetingHazardMarketBaselineSettings settings = curveSet.MarketBaselineSettings;
        if (!request.UseMarketBaseline || !settings.Enabled)
            return LiveMarketBaselineAdjustment.Disabled;

        MarketExpectedTotalInput input = ResolveMarketExpectedTotal(settings, request);
        if (!input.ExpectedTotal.HasValue)
            return LiveMarketBaselineAdjustment.Neutral("NoPregameInput", "none", "No pregame total input was supplied; market baseline multiplier 1.0 used.");

        double modelBaseline = settings.ModelBaselineExpectedTotalGoals > Epsilon
            ? settings.ModelBaselineExpectedTotalGoals
            : EstimateModelBaselineExpectedTotal(curveSet, effectiveEnd);
        if (modelBaseline <= Epsilon)
        {
            return LiveMarketBaselineAdjustment.Neutral(
                "MissingModelBaseline",
                input.Source,
                "Could not estimate fitted model pregame baseline expected total; market baseline multiplier 1.0 used.");
        }

        double marketExpected = Math.Clamp(
            input.ExpectedTotal.Value,
            Math.Max(settings.MinMarketExpectedTotalGoals, Epsilon),
            Math.Max(settings.MaxMarketExpectedTotalGoals, settings.MinMarketExpectedTotalGoals + Epsilon));
        double rawMultiplier = marketExpected / modelBaseline;
        double defaultShrink = Math.Clamp(settings.MultiplierShrink, 0.0, 1.0);
        double lowTotalShrink = Math.Clamp(request.MarketBaselineLowTotalShrink ?? settings.LowTotalMultiplierShrink ?? defaultShrink, 0.0, 1.0);
        double highTotalShrink = Math.Clamp(request.MarketBaselineHighTotalShrink ?? settings.HighTotalMultiplierShrink ?? defaultShrink, 0.0, 1.0);
        double shrink = rawMultiplier < 1.0 ? lowTotalShrink : highTotalShrink;
        double shrunkMultiplier = 1.0 + (rawMultiplier - 1.0) * shrink;
        double minMultiplier = Math.Max(Epsilon, request.MarketBaselineMinMultiplier ?? settings.MinMultiplier);
        double maxMultiplier = Math.Max(minMultiplier, request.MarketBaselineMaxMultiplier ?? settings.MaxMultiplier);
        double multiplier = Math.Clamp(shrunkMultiplier, minMultiplier, maxMultiplier);

        if (Math.Abs(multiplier - 1.0) > 0.0001)
        {
            warnings.Add($"market_baseline: source={input.Source}, market expected total {Format(marketExpected)}, model baseline {Format(modelBaseline)}, raw x{rawMultiplier.ToString("0.###", CultureInfo.InvariantCulture)}, applied x{multiplier.ToString("0.###", CultureInfo.InvariantCulture)}.");
        }

        return new LiveMarketBaselineAdjustment
        {
            Enabled = true,
            Applied = true,
            Status = "Applied",
            Source = input.Source,
            PregameTotalLine = input.PregameTotalLine,
            PregameOverOdds = input.PregameOverOdds,
            PregameUnderOdds = input.PregameUnderOdds,
            NoVigPOver = input.NoVigPOver,
            MarketExpectedTotalGoals = marketExpected,
            ModelBaselineExpectedTotalGoals = modelBaseline,
            RawMultiplier = rawMultiplier,
            Multiplier = multiplier,
            Warning = input.Warning
        };
    }

    private static MarketExpectedTotalInput ResolveMarketExpectedTotal(
        CompetingHazardMarketBaselineSettings settings,
        LiveMonteCarloRequest request)
    {
        if (request.PregameTotal.HasValue && request.PregameTotal.Value > 0)
        {
            return new MarketExpectedTotalInput(
                request.PregameTotal.Value,
                "pregame_total_direct",
                null,
                null,
                null,
                null,
                string.Empty);
        }

        if (request.PregameTotalLine.HasValue && request.PregameTotalLine.Value > 0
            && request.PregameOverOdds.HasValue && request.PregameOverOdds.Value > 1.0
            && request.PregameUnderOdds.HasValue && request.PregameUnderOdds.Value > 1.0)
        {
            double impliedOver = 1.0 / request.PregameOverOdds.Value;
            double impliedUnder = 1.0 / request.PregameUnderOdds.Value;
            double noVigPOver = impliedOver / (impliedOver + impliedUnder);
            double oddsSensitivityGoals = Math.Max(0.0, request.MarketBaselineOddsSensitivityGoals ?? settings.OddsSensitivityGoals);
            double expected = request.PregameTotalLine.Value + (noVigPOver - 0.5) * oddsSensitivityGoals;
            return new MarketExpectedTotalInput(
                expected,
                "pregame_total_line_odds",
                request.PregameTotalLine,
                request.PregameOverOdds,
                request.PregameUnderOdds,
                noVigPOver,
                string.Empty);
        }

        if (request.MarketTotal.HasValue && request.MarketTotal.Value > 0)
        {
            return new MarketExpectedTotalInput(
                request.MarketTotal.Value,
                "market_total_direct",
                null,
                null,
                null,
                null,
                "MarketTotal was used as a direct expected-total baseline because no pregame total line/odds were supplied.");
        }

        return new MarketExpectedTotalInput(null, "none", null, null, null, null, string.Empty);
    }

    private static double EstimateModelBaselineExpectedTotal(CompetingHazardCurveSet curveSet, double effectiveEnd)
    {
        double current = 0.0;
        double total = 0.0;
        double maxEnd = curveSet.Curves.Count == 0 ? effectiveEnd : curveSet.Curves.Max(x => x.BucketEndMinute);
        double end = Math.Min(effectiveEnd, maxEnd);

        while (current < end - Epsilon)
        {
            CompetingHazardCurve? curve = ResolveCurve(curveSet, "draw_0_0", current);
            if (curve is null)
                break;

            double segmentEnd = Math.Min(end, curve.BucketEndMinute);
            total += ExpectedGoalsBetween(curve, curve.Home, current, segmentEnd)
                     + ExpectedGoalsBetween(curve, curve.Away, current, segmentEnd);
            current = segmentEnd;
        }

        return total;
    }

    private static GoalDrawStepAdjustment ResolveGoalDrawAdjustment(
        CompetingHazardCurveSet curveSet,
        IReadOnlyDictionary<string, CompetingHazardGoalDrawSuppressionFactor> factors,
        CompetingHazardCurve curve)
    {
        if (!curveSet.GoalDrawSuppressionSettings.Enabled || factors.Count == 0)
            return GoalDrawStepAdjustment.Neutral;

        string targetBucket = string.IsNullOrWhiteSpace(curveSet.GoalDrawSuppressionSettings.NeutralScoreBucket)
            ? "draw_1_1_plus"
            : curveSet.GoalDrawSuppressionSettings.NeutralScoreBucket;

        if (!curve.NeutralScoreBucket.Equals(targetBucket, StringComparison.OrdinalIgnoreCase))
            return GoalDrawStepAdjustment.Neutral;

        string timeKey = $"goal_draw_{curve.TimeBucket}";
        if (!factors.TryGetValue(timeKey, out CompetingHazardGoalDrawSuppressionFactor? factor))
            factors.TryGetValue("goal_draw_overall", out factor);

        if (factor is null)
            return GoalDrawStepAdjustment.Neutral;

        return new GoalDrawStepAdjustment(factor.Key, ClampMultiplier(factor.Multiplier), factor);
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


    private static LiveStateCorrectionAdjustment InitialLiveStateCorrection(
        LiveStateCorrectionSet correctionSet,
        LiveMonteCarloRequest request)
    {
        return ResolveLiveStateCorrection(
            correctionSet,
            request,
            request.HomeGoals,
            request.AwayGoals,
            request.CurrentMinute,
            request.LastGoalMinute);
    }

    private static LiveStateCorrectionAdjustment ResolveLiveStateCorrection(
        LiveStateCorrectionSet correctionSet,
        LiveMonteCarloRequest request,
        int homeGoals,
        int awayGoals,
        double minute,
        double? lastGoalMinute)
    {
        if (!request.UseLiveStateCorrection || !correctionSet.Settings.Enabled)
            return LiveStateCorrectionAdjustment.Disabled;
        if (correctionSet.Factors.Count == 0)
            return LiveStateCorrectionAdjustment.Neutral("NoFactors", "Live-state correction model has no factors.");

        string scoreBucket = StateWeibullScoreBucketer.ResolveScoreBucket(homeGoals, awayGoals);
        int currentGoals = homeGoals + awayGoals;
        double? minutesSinceLastGoal = lastGoalMinute.HasValue
            ? Math.Max(0.0, minute - lastGoalMinute.Value)
            : null;

        LiveStateCorrectionFactor? factor = correctionSet.Factors
            .Where(x => MatchesLiveStateFactor(x, scoreBucket, currentGoals, minute, minutesSinceLastGoal, request))
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.Rows)
            .FirstOrDefault();

        if (factor is null)
            return LiveStateCorrectionAdjustment.Neutral("NoMatch");

        double minMultiplier = Math.Max(Epsilon, correctionSet.Settings.MinMultiplier);
        double maxMultiplier = Math.Max(minMultiplier, correctionSet.Settings.MaxMultiplier);
        double multiplier = Math.Clamp(ClampMultiplier(factor.Multiplier), minMultiplier, maxMultiplier);

        return new LiveStateCorrectionAdjustment
        {
            Enabled = true,
            Applied = Math.Abs(multiplier - 1.0) > 0.0001,
            Status = "Applied",
            FactorKey = factor.Key,
            SourceSlice = factor.SourceSlice,
            Multiplier = multiplier,
            Warning = factor.Warning
        };
    }

    private static bool MatchesLiveStateFactor(
        LiveStateCorrectionFactor factor,
        string scoreBucket,
        int currentGoals,
        double minute,
        double? minutesSinceLastGoal,
        LiveMonteCarloRequest request)
    {
        if (!string.IsNullOrWhiteSpace(factor.ScoreBucket) &&
            !factor.ScoreBucket.Equals(scoreBucket, StringComparison.OrdinalIgnoreCase))
            return false;
        if (factor.MinMinute.HasValue && minute < factor.MinMinute.Value - Epsilon)
            return false;
        if (factor.MaxMinute.HasValue && minute > factor.MaxMinute.Value + Epsilon)
            return false;
        if (factor.MinCurrentGoals.HasValue && currentGoals < factor.MinCurrentGoals.Value)
            return false;
        if (factor.MaxCurrentGoals.HasValue && currentGoals > factor.MaxCurrentGoals.Value)
            return false;
        if (factor.MinMinutesSinceLastGoal.HasValue)
        {
            if (!minutesSinceLastGoal.HasValue || minutesSinceLastGoal.Value < factor.MinMinutesSinceLastGoal.Value - Epsilon)
                return false;
        }
        if (factor.MaxMinutesSinceLastGoal.HasValue)
        {
            if (!minutesSinceLastGoal.HasValue || minutesSinceLastGoal.Value > factor.MaxMinutesSinceLastGoal.Value + Epsilon)
                return false;
        }
        if (factor.Line.HasValue && Math.Abs(factor.Line.Value - request.Line) > 0.0001)
            return false;
        if (factor.MinPregameTotalLine.HasValue)
        {
            if (!request.PregameTotalLine.HasValue || request.PregameTotalLine.Value < factor.MinPregameTotalLine.Value - Epsilon)
                return false;
        }
        if (factor.MaxPregameTotalLine.HasValue)
        {
            if (!request.PregameTotalLine.HasValue || request.PregameTotalLine.Value > factor.MaxPregameTotalLine.Value + Epsilon)
                return false;
        }

        return true;
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
        bool afterGoalEnabled,
        bool goalDrawEnabled,
        LiveMarketBaselineAdjustment marketBaseline,
        LiveStateCorrectionSet liveStateCorrection)
    {
        string overNeed = neededGoalsForOver <= 0
            ? "Over is already winning at the current score"
            : $"Over {request.Line.ToString("0.##", CultureInfo.InvariantCulture)} needs {neededGoalsForOver}+ more goal(s)";

        var features = new List<string>();
        if (afterGoalEnabled)
            features.Add("after-goal hazard factors");
        if (goalDrawEnabled)
            features.Add("goal-draw suppression");
        if (marketBaseline.Applied)
            features.Add($"market baseline x{marketBaseline.Multiplier.ToString("0.###", CultureInfo.InvariantCulture)}");
        if (request.UseLiveStateCorrection && liveStateCorrection.Settings.Enabled && liveStateCorrection.Factors.Count > 0)
            features.Add("live-state correction");

        string suffix = features.Count > 0 ? " with " + string.Join(" and ", features) : string.Empty;
        return $"{overNeed}. MC v3 competing hazards{suffix} POver={FormatProbability(pOver)}, PUnder={FormatProbability(pUnder)}, PPush={FormatProbability(pPush)}. Fair Over odds={FormatOdds(fairOver)}, fair Under odds={FormatOdds(fairUnder)}.";
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

    private sealed record MarketExpectedTotalInput(
        double? ExpectedTotal,
        string Source,
        double? PregameTotalLine,
        double? PregameOverOdds,
        double? PregameUnderOdds,
        double? NoVigPOver,
        string Warning);

    private sealed record AfterGoalStepAdjustment(
        string BucketKey,
        double HomeMultiplier,
        double AwayMultiplier,
        CompetingHazardAfterGoalFactor? Factor)
    {
        public static readonly AfterGoalStepAdjustment Neutral = new(string.Empty, 1.0, 1.0, null);
    }

    private sealed record GoalDrawStepAdjustment(
        string FactorKey,
        double Multiplier,
        CompetingHazardGoalDrawSuppressionFactor? Factor)
    {
        public static readonly GoalDrawStepAdjustment Neutral = new(string.Empty, 1.0, null);
    }
}
