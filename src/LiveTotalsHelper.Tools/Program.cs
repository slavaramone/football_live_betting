using System.Globalization;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.SofaScore;
using LiveTotalsHelper.Infrastructure.SofaScore;
using LiveTotalsHelper.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

try
{
    if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
    {
        HelpPrinter.Print();
        return 0;
    }

    string command = args[0].Trim().ToLowerInvariant();
    string[] commandArgs = args.Skip(1).ToArray();

    return command switch
    {
        "download-sofascore" => await RunDownloadSofaScore(commandArgs),
        "import-sofascore" => await RunImportSofaScore(commandArgs),
        "validate-db" => await RunValidateDb(commandArgs),
        "build-live-total-calibration-dataset" => await RunBuildLiveTotalCalibrationDataset(commandArgs),
        "analyze-live-total-calibration" => await RunAnalyzeLiveTotalCalibration(commandArgs),
        "fit-live-total-state-correction" => await RunFitLiveTotalStateCorrection(commandArgs),
        "fit-weibull" => await RunFitWeibull(commandArgs),
        "price-live-total" => await RunPriceLiveTotal(commandArgs),
        _ => HelpPrinter.UnknownCommand(command)
    };
}
catch (ArgumentException ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"Argument error: {ex.Message}");
    Console.ResetColor();
    Console.Error.WriteLine();
    HelpPrinter.Print();
    return 2;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine(ex.ToString());
    Console.ResetColor();
    return 1;
}

static async Task<int> RunDownloadSofaScore(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    var options = new SofaScoreDownloadOptions
    {
        League = parsed.RequiredString("league"),
        TournamentId = parsed.RequiredInt("tournament-id"),
        SeasonId = parsed.RequiredInt("season-id"),
        OutputRoot = parsed.String("output", "data/sofascore"),
        DelayMs = parsed.Int("delay-ms", 450),
        Overwrite = parsed.Bool("overwrite", false),
        DownloadIncidents = parsed.Bool("incidents", true),
        DownloadStatistics = parsed.Bool("statistics", true),
        Headless = parsed.Has("show-browser") ? false : parsed.Bool("headless", true),
        WarmupDelayMs = parsed.Int("warmup-delay-ms", 1000),
        CalendarMode = parsed.String("calendar-mode", "round"),
        SkipDetailsForNotStartedEvents = parsed.Bool("skip-details-for-not-started", true),
        StrictEventDetails = parsed.Bool("strict-event-details", false)
    };

    AddRounds(options.Rounds, parsed);

    await using var client = await SofaScoreClient.CreateAsync(options, Console.Out, CancellationToken.None);
    var downloader = new SofaScoreDownloader(client, new SofaScoreJsonFileStore());

    SofaScoreDownloadResult result = await downloader.DownloadAsync(options, Console.Out, CancellationToken.None);
    PrintDownloadResult(result);

    return result.Failures.Count == 0 ? 0 : 1;
}



static async Task<int> RunBuildLiveTotalCalibrationDataset(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    LeagueProfile? profile = null;
    string profilesFile = parsed.String("profiles-file", "league-profiles.json");
    if (parsed.Has("profile"))
    {
        LeagueProfileStore profileStore = await LeagueProfileStore.LoadAsync(profilesFile, CancellationToken.None);
        profile = profileStore.FindRequired(parsed.RequiredString("profile"));
    }

    string modelPath = parsed.Has("model")
        ? parsed.RequiredString("model")
        : profile?.ModelPath ?? throw new ArgumentException("Missing required argument --model, or provide --profile with modelPath in league-profiles.json.");

    var options = new LiveTotalCalibrationDatasetOptions
    {
        League = parsed.String("league", profile?.League ?? string.Empty),
        ModelPath = modelPath,
        OutputPath = parsed.String("output", string.Empty),
        EmpiricalWeight = parsed.Double("empirical-weight", profile?.DefaultEmpiricalWeight ?? 0.80),
        IncludeUnreliableMatches = parsed.Bool("include-unreliable", false),
        IncludeEventTriggers = parsed.Bool("include-event-triggers", true),
        MaxExamples = parsed.Int("max-examples", 20)
    };

    AddSeasonIds(options.SeasonIds, parsed);
    if (parsed.Has("round") || parsed.Has("from-round") || parsed.Has("to-round"))
        AddRounds(options.Rounds, parsed);
    AddOptionalIntList(options.SnapshotMinutes, parsed, "minutes", clearExisting: true);

    IConfiguration configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();

    await using LiveTotalsDbContext dbContext = CreateDbContext(configuration);
    var builder = new LiveTotalCalibrationDatasetBuilder(dbContext, options);
    LiveTotalCalibrationDatasetResult result = await builder.BuildAsync(CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("Live total calibration dataset build done.");
    Console.WriteLine($"League: {(string.IsNullOrWhiteSpace(options.League) ? "all" : options.League)}");
    Console.WriteLine($"Seasons included: {(result.SeasonsIncluded.Count == 0 ? "none" : string.Join(", ", result.SeasonsIncluded))}");
    Console.WriteLine($"Matches checked: {result.MatchesChecked}");
    Console.WriteLine($"Finished matches: {result.FinishedMatches}");
    Console.WriteLine($"Reliable finished matches: {result.ReliableFinishedMatches}");
    Console.WriteLine($"Unreliable finished matches: {result.UnreliableFinishedMatches}");
    Console.WriteLine($"States written: {result.StatesWritten}");
    Console.WriteLine($"  Fixed minute: {result.FixedMinuteStatesWritten}");
    Console.WriteLine($"  After goal: {result.AfterGoalStatesWritten}");
    Console.WriteLine($"  After red card: {result.AfterRedCardStatesWritten}");
    Console.WriteLine($"Output: {result.OutputPath}");
    foreach (string warning in result.Warnings)
        Console.WriteLine($"Warning: {warning}");

    return 0;
}

static async Task<int> RunAnalyzeLiveTotalCalibration(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    var options = new LiveTotalCalibrationAnalysisOptions
    {
        InputPath = parsed.RequiredString("input"),
        OutputPath = parsed.String("output", string.Empty)
    };
    AddOptionalIntList(options.TrainingSeasonIds, parsed, "training-season-ids", clearExisting: true);
    AddOptionalIntList(options.TestSeasonIds, parsed, "test-season-ids", clearExisting: true);

    var analyzer = new LiveTotalCalibrationAnalyzer(options);
    LiveTotalCalibrationAnalysisResult result = await analyzer.AnalyzeAsync(CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("Live total calibration analysis done.");
    Console.WriteLine($"Input: {result.InputPath}");
    Console.WriteLine($"Rows read: {result.RowsRead}");
    Console.WriteLine($"Rows analyzed: {result.RowsAnalyzed}");
    Console.WriteLine($"Output: {result.OutputPath}");

    if (result.HasTrainTestSplit)
    {
        Console.WriteLine($"Train/test buckets written: {result.TrainTestBuckets.Count}");
        Console.WriteLine();
        Console.WriteLine("Trigger       MinuteBand  ScoreState            TrRows  TeRows  Factor  TestActual  TestBase  TestCorrected  BaseAbsErr  CorrAbsErr");
        foreach (LiveTotalCalibrationTrainTestBucketResult bucket in result.TrainTestBuckets)
        {
            Console.WriteLine($"{bucket.StateTrigger,-13} {bucket.MinuteBand,-11} {bucket.DetailedScoreState,-20} {bucket.TrainRows,6} {bucket.TestRows,6}  {F(bucket.CorrectionFactor),6}  {bucket.TestActualRemainingGoalsPerRow,10:0.###}  {bucket.TestBaselineRemainingGoalsPerRow,8:0.###}  {D(bucket.TestCorrectedRemainingGoalsPerRow),13}  {D(bucket.TestBaselineAbsErrorPerRow),10}  {D(bucket.TestCorrectedAbsErrorPerRow),10}");
        }

        return 0;
    }

    Console.WriteLine($"Buckets written: {result.Buckets.Count}");
    Console.WriteLine();
    Console.WriteLine("Trigger       MinuteBand  ScoreState            Rows  Matches  ActualRem/Row  BaseRem/Row  TimingShare  Factor");
    foreach (LiveTotalCalibrationBucketResult bucket in result.Buckets)
    {
        Console.WriteLine($"{bucket.StateTrigger,-13} {bucket.MinuteBand,-11} {bucket.DetailedScoreState,-20} {bucket.Rows,5}  {bucket.Matches,7}  {bucket.ActualRemainingGoalsPerRow,13:0.###}  {bucket.BaselineRemainingGoalsPerRow,11:0.###}  {P(bucket.AverageTimingRemainingShare),11}  {F(bucket.CorrectionFactor),7}");
    }

    return 0;

    static string P(double value) => value.ToString("P1", CultureInfo.InvariantCulture);
    static string F(double? value) => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "n/a";
    static string D(double? value) => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "n/a";
}

static async Task<int> RunFitLiveTotalStateCorrection(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    var options = new LiveTotalStateCorrectionFitOptions
    {
        InputPath = parsed.RequiredString("input"),
        OutputPath = parsed.String("output", string.Empty),
        MinBucketMatches = parsed.Int("min-bucket-matches", 100),
        MinStateMatches = parsed.Int("min-state-matches", 200),
        MinFactor = parsed.Double("min-factor", 0.50),
        MaxFactor = parsed.Double("max-factor", 2.50)
    };
    AddRequiredIntList(options.TrainingSeasonIds, parsed, "training-season-ids");

    var fitter = new LiveTotalStateCorrectionFitter(options);
    LiveTotalStateCorrectionFitResult result = await fitter.FitAsync(CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("Live total state correction fit done.");
    Console.WriteLine($"Input: {result.InputPath}");
    Console.WriteLine($"Output: {result.OutputPath}");
    Console.WriteLine($"League: {(string.IsNullOrWhiteSpace(result.League) ? "unknown" : result.League)}");
    Console.WriteLine($"Training seasons: {string.Join(", ", result.TrainingSeasonIds)}");
    Console.WriteLine($"Rows used: {result.TrainingRowsUsed}");
    Console.WriteLine($"Matches used: {result.TrainingMatchesUsed}");
    Console.WriteLine($"League average final goals: {result.LeagueAverageFinalGoals:0.###}");

    Console.WriteLine();
    Console.WriteLine("Bucket factors:");
    Console.WriteLine("Trigger       Band    ScoreState            Rows  Matches  Raw    Used   Usable");
    foreach (LiveTotalStateCorrectionBucket bucket in result.Buckets)
        Console.WriteLine($"{bucket.StateTrigger,-13} {bucket.MinuteBand,-7} {bucket.DetailedScoreState,-20} {bucket.Rows,5}  {bucket.Matches,7}  {bucket.RawFactor,5:0.###}  {bucket.Factor,5:0.###}  {bucket.IsUsable}");

    Console.WriteLine();
    Console.WriteLine("State fallback factors:");
    Console.WriteLine("Trigger       ScoreState            Rows  Matches  Raw    Used   Usable");
    foreach (LiveTotalStateCorrectionFallback fallback in result.StateFallbacks)
        Console.WriteLine($"{fallback.StateTrigger,-13} {fallback.DetailedScoreState,-20} {fallback.Rows,5}  {fallback.Matches,7}  {fallback.RawFactor,5:0.###}  {fallback.Factor,5:0.###}  {fallback.IsUsable}");

    return 0;
}

static async Task<int> RunFitWeibull(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    var options = new WeibullFitOptions
    {
        OutputPath = parsed.String("output", string.Empty),
        League = parsed.String("league", string.Empty),
        MaxMinute = parsed.Int("max-minute", 90),
        GroupByColumn = parsed.String("group-by", string.Empty),
        MinGroupGoals = parsed.Int("min-group-goals", 30),
        MaxIterations = parsed.Int("max-iterations", 100),
        Tolerance = parsed.Double("tolerance", 1e-9),
        BlendWeibullWeight = parsed.Double("blend-weibull-weight", 0.30)
    };

    var sampleOptions = new WeibullDbSampleOptions
    {
        League = options.League,
        GroupByColumn = options.GroupByColumn,
        MaxMinute = options.MaxMinute,
        IncludeUnreliableMatches = parsed.Bool("include-unreliable", false),
        MaxExamples = parsed.Int("max-examples", 20)
    };
    AddSeasonIds(sampleOptions.SeasonIds, parsed);
    if (parsed.Has("round") || parsed.Has("from-round") || parsed.Has("to-round"))
        AddRounds(sampleOptions.Rounds, parsed);

    IConfiguration configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();

    await using LiveTotalsDbContext dbContext = CreateDbContext(configuration);
    var sampleLoader = new WeibullDbSampleLoader(dbContext, sampleOptions);
    WeibullDbSampleResult sample = await sampleLoader.LoadAsync(CancellationToken.None);

    var fitter = new WeibullModelFitter(options);
    WeibullFitResult result = await fitter.FitAsync(sample.Rows, "database", CancellationToken.None);
    foreach (string warning in sample.Warnings)
        result.Warnings.Insert(0, warning);

    Console.WriteLine();
    Console.WriteLine("Weibull fit done.");
    Console.WriteLine("Source: database");
    Console.WriteLine($"Output: {result.OutputPath}");
    Console.WriteLine($"League: {(string.IsNullOrWhiteSpace(result.League) ? "unknown" : result.League)}");
    Console.WriteLine($"Seasons included: {(result.SeasonIds.Count == 0 ? "unknown" : string.Join(", ", result.SeasonIds))}");
    Console.WriteLine($"Matches checked: {sample.MatchesChecked}");
    Console.WriteLine($"Finished matches: {sample.FinishedMatches}");
    Console.WriteLine($"Reliable finished matches: {sample.ReliableFinishedMatches}");
    Console.WriteLine($"Unreliable finished matches: {sample.UnreliableFinishedMatches}");
    Console.WriteLine($"Goals used: {result.GoalCount}");
    Console.WriteLine($"Matches represented: {result.MatchCount}");
    Console.WriteLine($"Mean goal minute: {result.MeanGoalMinute:0.00}");
    Console.WriteLine($"Median goal minute: {result.MedianGoalMinute:0.00}");
    Console.WriteLine($"Shape k: {result.ShapeK:0.######}");
    Console.WriteLine($"Scale lambda: {result.ScaleLambda:0.######}");
    Console.WriteLine($"Log-likelihood: {result.LogLikelihood:0.###}");
    Console.WriteLine($"CDF at max minute ({result.MaxMinute}): {result.CdfAtMaxMinute:P2}");
    Console.WriteLine($"Blend weights: Weibull {result.BlendWeibullWeight:P0}, Empirical {result.BlendEmpiricalWeight:P0}");
    if (!string.IsNullOrWhiteSpace(result.GroupByColumn))
        Console.WriteLine($"Group by: {result.GroupByColumn} ({result.Groups.Count} fitted groups)");

    Console.WriteLine();
    Console.WriteLine("Minute checkpoints, remaining share by model:");
    Console.WriteLine("Minute   Weibull   Empirical   Blended");
    foreach (TimingMinuteCheckpoint checkpoint in result.Checkpoints)
        Console.WriteLine($"{checkpoint.Minute,6}   {checkpoint.WeibullRemainingShare,7:P1}   {checkpoint.EmpiricalRemainingShare,9:P1}   {checkpoint.BlendedRemainingShare,7:P1}");

    Console.WriteLine();
    Console.WriteLine("Bucket comparison:");
    Console.WriteLine("Bucket     Actual    Weibull   Empirical   Blended");
    foreach (TimingBucketComparison bucket in result.Buckets)
        Console.WriteLine($"{bucket.Bucket,8}   {bucket.ActualPct,7:P1}   {bucket.WeibullExpectedPct,7:P1}   {bucket.EmpiricalExpectedPct,9:P1}   {bucket.BlendedExpectedPct,7:P1}   ({bucket.ActualGoals} goals)");

    Console.WriteLine();
    Console.WriteLine("Timing model fit scores by bucket error:");
    Console.WriteLine("Model       MAE       RMSE      MaxErr");
    foreach (TimingModelFitScore score in result.FitScores.OrderBy(x => x.MeanAbsoluteBucketError))
        Console.WriteLine($"{score.Model,-9}   {score.MeanAbsoluteBucketError,7:P2}   {score.RootMeanSquaredBucketError,7:P2}   {score.MaxAbsoluteBucketError,7:P2}");

    if (result.Groups.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Grouped timing models:");
        Console.WriteLine("Group                 Goals  Matches  MeanMin  k        Lambda   Emp75Rem  Best");
        foreach (TimingModelGroupResult group in result.Groups.OrderByDescending(x => x.GoalCount))
        {
            TimingMinuteCheckpoint? cp75 = group.Checkpoints.FirstOrDefault(x => x.Minute == 75);
            string best = group.FitScores.OrderBy(x => x.MeanAbsoluteBucketError).FirstOrDefault()?.Model ?? "n/a";
            Console.WriteLine($"{group.GroupName,-20} {group.GoalCount,5}  {group.MatchCount,7}  {group.MeanGoalMinute,7:0.00}  {group.ShapeK,7:0.####}  {group.ScaleLambda,7:0.##}  {cp75?.EmpiricalRemainingShare ?? 0,8:P1}  {best}");
        }
    }

    if (result.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Warnings:");
        foreach (string warning in result.Warnings)
            Console.WriteLine($"- {warning}");
    }

    return 0;
}


static async Task<int> RunPriceLiveTotal(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    LeagueProfile? profile = null;
    string profileNameForOutput = string.Empty;
    string profilesFile = parsed.String("profiles-file", "league-profiles.json");
    if (parsed.Has("profile"))
    {
        string requestedProfile = parsed.RequiredString("profile");
        LeagueProfileStore profileStore = await LeagueProfileStore.LoadAsync(profilesFile, CancellationToken.None);
        profile = profileStore.FindRequired(requestedProfile);
        profileNameForOutput = string.IsNullOrWhiteSpace(profile.Name) ? profile.Key : profile.Name;
    }

    string modelPath = parsed.Has("model")
        ? parsed.RequiredString("model")
        : profile?.ModelPath ?? throw new ArgumentException("Missing required argument --model, or provide --profile with a modelPath in league-profiles.json.");

    var options = new LiveTotalPriceOptions
    {
        ModelPath = modelPath,
        StateCorrectionPath = parsed.String("state-correction", profile?.StateCorrectionPath ?? string.Empty),
        StateTrigger = LiveTotalStateTrigger.Normalize(parsed.String("state-trigger", LiveTotalStateTrigger.FixedMinute)),
        StartingLine = parsed.RequiredDouble("starting-line"),
        StartingOverOdds = parsed.RequiredDouble("starting-over"),
        StartingUnderOdds = parsed.RequiredDouble("starting-under"),
        Minute = parsed.RequiredInt("minute"),
        HomeGoals = parsed.RequiredInt("home-goals"),
        AwayGoals = parsed.RequiredInt("away-goals"),
        EmpiricalWeight = parsed.Double("empirical-weight", profile?.DefaultEmpiricalWeight ?? 0.80),
        EdgeThreshold = parsed.Double("edge-threshold", profile?.EdgeThreshold ?? 0.10),
        HomeRedCards = parsed.Int("home-red-cards", parsed.Int("home-reds", 0)),
        AwayRedCards = parsed.Int("away-red-cards", parsed.Int("away-reds", 0)),
        LastGoalMinute = parsed.Int("last-goal-minute", -1),
        RecentGoalMinutes = parsed.Int("recent-goal-minutes", 2),
        VolumeFactor = parsed.Double("volume-factor", 1.0),
        VolumeFactorSource = parsed.Has("volume-factor") ? "manual --volume-factor" : "none/default 1.0"
    };

    bool explicitTargetLines = parsed.Has("target-lines");
    if (!explicitTargetLines && profile?.TargetLines is { Count: > 0 })
    {
        options.TargetLines.Clear();
        foreach (double line in profile.TargetLines)
            options.TargetLines.Add(line);
    }

    bool useCurrentSeasonVolume = !parsed.Has("volume-factor") && parsed.Bool("use-current-season-volume", profile?.UseCurrentSeasonVolume ?? false);
    SeasonVolumeFactorResult? seasonVolume = null;
    if (useCurrentSeasonVolume)
    {
        string league = parsed.String("league", profile?.League ?? string.Empty);
        int currentSeasonId = parsed.Has("current-season-id")
            ? parsed.RequiredInt("current-season-id")
            : profile?.CurrentSeasonId ?? 0;
        int beforeRound = parsed.Has("before-round")
            ? parsed.RequiredInt("before-round")
            : profile?.DefaultBeforeRound ?? 0;
        int priorStrength = parsed.Int("prior-strength-matches", profile?.PriorStrengthMatches ?? 100);

        if (string.IsNullOrWhiteSpace(league))
            throw new ArgumentException("Current-season volume requires --league or a profile with league set.");
        if (currentSeasonId <= 0)
            throw new ArgumentException("Current-season volume requires --current-season-id or a profile with currentSeasonId set.");
        if (beforeRound <= 0)
            throw new ArgumentException("Current-season volume requires --before-round. It should be the next/current round, so only earlier completed rounds are used.");

        var volumeOptions = new SeasonVolumeFactorOptions
        {
            League = league,
            CurrentSeasonId = currentSeasonId,
            BeforeRound = beforeRound,
            PriorStrengthMatches = priorStrength
        };

        if (parsed.Has("base-season-ids"))
        {
            AddRequiredIntList(volumeOptions.BaseSeasonIds, parsed, "base-season-ids");
        }
        else if (profile?.BaseSeasonIds is { Count: > 0 })
        {
            foreach (int seasonId in profile.BaseSeasonIds)
                volumeOptions.BaseSeasonIds.Add(seasonId);
        }
        else
        {
            throw new ArgumentException("Current-season volume requires --base-season-ids or a profile with baseSeasonIds set.");
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        await using LiveTotalsDbContext dbContext = CreateDbContext(configuration);
        var volumeCalculator = new SeasonVolumeFactorCalculator(dbContext);
        seasonVolume = await volumeCalculator.CalculateAsync(volumeOptions, CancellationToken.None);
        options.VolumeFactor = seasonVolume.Factor;
        options.VolumeFactorSource = seasonVolume.Source;
    }

    AddOptionalDoubleList(options.TargetLines, parsed, "target-lines", clearExisting: true);
    AddLiveOverOdds(options.LiveOverOddsByLine, parsed);
    AddLiveUnderOdds(options.LiveUnderOddsByLine, parsed);
    AddLiveOddsLinesToTargets(options.TargetLines, options.LiveOverOddsByLine, explicitTargetLines);
    AddLiveOddsLinesToTargets(options.TargetLines, options.LiveUnderOddsByLine, explicitTargetLines);

    var pricer = new LiveTotalPricer(options);
    LiveTotalPriceResult result = await pricer.PriceAsync(CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("Live total pricing done.");
    if (!string.IsNullOrWhiteSpace(profileNameForOutput))
    {
        Console.WriteLine($"Profile: {profileNameForOutput} ({profile!.Key})");
        if (!string.IsNullOrWhiteSpace(profile.RiskLevel))
            Console.WriteLine($"Profile risk: {profile.RiskLevel}");
        if (!string.IsNullOrWhiteSpace(profile.Notes))
            Console.WriteLine($"Profile notes: {profile.Notes}");
    }
    Console.WriteLine($"Model: {result.ModelPath}");
    Console.WriteLine($"League: {(string.IsNullOrWhiteSpace(result.League) ? (profile?.League ?? "unknown") : result.League)}");
    Console.WriteLine($"Minute/score: {result.Minute}'  {result.HomeGoals}-{result.AwayGoals} ({result.ScoreState}; {result.DetailedScoreState}; {result.StateTrigger})");
    Console.WriteLine($"Timing group: {result.SelectedTimingGroup}");
    if (!string.IsNullOrWhiteSpace(result.TimingFallback))
        Console.WriteLine($"Timing fallback: {result.TimingFallback}");
    Console.WriteLine($"Starting O/U: line {result.StartingLine:0.##}, over {result.StartingOverOdds:0.###}, under {result.StartingUnderOdds:0.###}");
    Console.WriteLine($"Starting fair over probability: {result.StartingFairOverProbability:P2}");
    Console.WriteLine($"Starting total xG: {result.StartingTotalXg:0.###}");
    Console.WriteLine($"Blend: Empirical {result.EmpiricalWeight:P0}, Weibull {result.WeibullWeight:P0}");
    Console.WriteLine($"Edge threshold: {options.EdgeThreshold:P0}");
    Console.WriteLine($"Remaining share: Weibull {result.WeibullRemainingShare:P1}, Empirical {result.EmpiricalRemainingShare:P1}, Used {result.TimingRemainingShare:P1}");
    Console.WriteLine($"Remaining xG before state correction: {result.RemainingXgBeforeStateCorrection:0.###}");
    Console.WriteLine($"State correction: {result.StateCorrectionFactor:0.###} ({result.StateCorrectionSource})");
    Console.WriteLine($"State correction supported for betting: {result.StateCorrectionSupported}");
    Console.WriteLine($"Remaining xG before volume: {result.RemainingXgBeforeVolume:0.###}");
    Console.WriteLine($"Volume factor: {result.VolumeFactor:0.###} ({result.VolumeFactorSource})");
    if (seasonVolume is not null)
    {
        Console.WriteLine($"Volume base: {seasonVolume.BaseGoals} goals / {seasonVolume.BaseMatches} matches = {seasonVolume.BaseGoalsPerMatch:0.###} GPM");
        Console.WriteLine($"Volume current: {seasonVolume.CurrentGoals} goals / {seasonVolume.CurrentMatches} matches = {seasonVolume.CurrentGoalsPerMatch:0.###} GPM");
        Console.WriteLine($"Volume raw factor: {seasonVolume.RawFactor:0.###}, shrink weight: {seasonVolume.Weight:P1}");
        if (!string.IsNullOrWhiteSpace(seasonVolume.Warning))
            result.Warnings.Add(seasonVolume.Warning);
    }
    Console.WriteLine($"Expected remaining goals: {result.RemainingXg:0.###}");

    if (!result.StateCorrectionSupported && !string.IsNullOrWhiteSpace(options.StateCorrectionPath))
        result.Warnings.Add("Unsupported sparse state bucket - no betting decision will be allowed.");

    if (result.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Warnings:");
        foreach (string warning in result.Warnings)
            Console.WriteLine($"- {warning}");
    }

    Console.WriteLine();
    Console.WriteLine("Over/Under pricing:");
    Console.WriteLine("Line   Over%   Push%  Under%  FairO  BookO   EdgeO     EVO    FairU  BookU   EdgeU     EVU    Decision");
    foreach (LiveTotalLinePrice line in result.Lines)
    {
        string bookOver = line.BookOverOdds.HasValue ? line.BookOverOdds.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-";
        string overEdge = line.OverEdge.HasValue ? line.OverEdge.Value.ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture) : "-";
        string overEv = line.OverExpectedValue.HasValue ? line.OverExpectedValue.Value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) : "-";
        string fairOver = FormatFairOdds(line.FairOdds);

        string bookUnder = line.BookUnderOdds.HasValue ? line.BookUnderOdds.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-";
        string underEdge = line.UnderEdge.HasValue ? line.UnderEdge.Value.ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture) : "-";
        string underEv = line.UnderExpectedValue.HasValue ? line.UnderExpectedValue.Value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) : "-";
        string fairUnder = FormatFairOdds(line.FairUnderOdds);

        Console.WriteLine($"{line.Line,4:0.##}  {line.WinProbability,6:P1}  {line.PushProbability,6:P1}  {line.UnderWinProbability,6:P1}  {fairOver,5}  {bookOver,5}  {overEdge,7}  {overEv,7}  {fairUnder,6}  {bookUnder,5}  {underEdge,7}  {underEv,7}  {line.Decision}");
    }

    return 0;
}


static string FormatFairOdds(double odds)
{
    if (double.IsNaN(odds) || double.IsInfinity(odds) || odds > 9999)
        return "-";
    return odds.ToString("0.###", CultureInfo.InvariantCulture);
}


static async Task<int> RunValidateDb(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    var options = new DbValidationOptions
    {
        League = parsed.String("league", string.Empty),
        SeasonId = parsed.Int("season-id", 0),
        FailOnWarnings = parsed.Bool("fail-on-warnings", false),
        MaxExamplesPerCheck = parsed.Int("max-examples", 20)
    };

    if (parsed.Has("round") || parsed.Has("from-round") || parsed.Has("to-round"))
        AddRounds(options.Rounds, parsed);

    IConfiguration configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();

    await using LiveTotalsDbContext dbContext = CreateDbContext(configuration);
    var runner = new DbValidationRunner(dbContext, options);
    DbValidationResult result = await runner.RunAsync(CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("Database validation done.");
    Console.WriteLine($"Matches checked: {result.MatchesChecked}");
    Console.WriteLine($"Events checked: {result.EventsChecked}");
    Console.WriteLine($"Team stats checked: {result.TeamStatsChecked}");
    Console.WriteLine($"Errors: {result.ErrorCount}");
    Console.WriteLine($"Warnings: {result.WarningCount}");
    Console.WriteLine($"Info: {result.InfoCount}");

    foreach (DbValidationCheckResult check in result.Checks)
    {
        Console.WriteLine();
        Console.WriteLine($"[{check.Severity}] {check.Name}: {check.Message}");
        foreach (string example in check.Examples.Take(options.MaxExamplesPerCheck))
            Console.WriteLine($"  - {example}");

        if (check.Examples.Count > options.MaxExamplesPerCheck)
            Console.WriteLine($"  ... {check.Examples.Count - options.MaxExamplesPerCheck} more");
    }

    if (result.ErrorCount > 0)
        return 1;

    if (options.FailOnWarnings && result.WarningCount > 0)
        return 1;

    return 0;
}

static LiveTotalsDbContext CreateDbContext(IConfiguration configuration)
{
    string connectionString = configuration.GetConnectionString("LiveTotalsDb")
        ?? throw new InvalidOperationException("Connection string 'LiveTotalsDb' was not found in appsettings.json.");

    var options = new DbContextOptionsBuilder<LiveTotalsDbContext>()
        .UseNpgsql(connectionString)
        .Options;

    return new LiveTotalsDbContext(options);
}

static async Task<int> RunImportSofaScore(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    string league = parsed.RequiredString("league");
    int tournamentId = parsed.Int("tournament-id", 0);
    int seasonId = parsed.RequiredInt("season-id");
    string inputRoot = parsed.String("input", parsed.String("output", "data/sofascore"));
    bool debugImport = parsed.Bool("debug-import", false);

    var rounds = new List<int>();
    if (parsed.Has("round") || parsed.Has("from-round") || parsed.Has("to-round"))
        AddRounds(rounds, parsed);

    IConfiguration configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();

    await using LiveTotalsDbContext dbContext = await DatabaseMigrator.CreateMigratedDbContextAsync(configuration, Console.Out, CancellationToken.None);
    var importer = new SofaScoreDbImporter(dbContext);

    SofaScoreImportResult result = await ImportSofaScoreFolderAsync(importer, inputRoot, league, tournamentId, seasonId, rounds, debugImport, Console.Out, CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("Import done.");
    Console.WriteLine($"Rounds imported: {result.RoundsImported}");
    Console.WriteLine($"Calendars imported: {result.CalendarsImported}");
    Console.WriteLine($"Incidents files imported: {result.IncidentsImported}");
    Console.WriteLine($"Statistics files imported: {result.StatisticsImported}");
    Console.WriteLine($"Warnings: {result.Warnings.Count}");
    Console.WriteLine($"Failures: {result.Failures.Count}");

    if (result.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Warnings:");
        foreach (string warning in result.Warnings)
            Console.WriteLine($"- {warning}");
    }

    if (result.Failures.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Failures:");
        foreach (string failure in result.Failures)
            Console.WriteLine($"- {failure}");
    }

    return result.Failures.Count == 0 ? 0 : 1;
}

static async Task<SofaScoreImportResult> ImportSofaScoreFolderAsync(
    SofaScoreDbImporter importer,
    string inputRoot,
    string league,
    int tournamentId,
    int seasonId,
    IReadOnlyCollection<int> requestedRounds,
    bool debugImport,
    TextWriter log,
    CancellationToken cancellationToken)
{
    var result = new SofaScoreImportResult();
    string leagueSlug = FileNameSanitizer.Slugify(league);
    string seasonFolder = Path.Combine(inputRoot, leagueSlug, $"season-{seasonId}");

    if (!Directory.Exists(seasonFolder))
        throw new ArgumentException($"SofaScore season folder was not found: {seasonFolder}");

    List<(int Round, string Folder)> roundFolders = [];
    if (requestedRounds.Count > 0)
    {
        foreach (int round in requestedRounds.Distinct().OrderBy(x => x))
        {
            string folder = FindRoundFolder(seasonFolder, round);
            if (string.IsNullOrWhiteSpace(folder))
            {
                result.Warnings.Add($"round {round}: folder not found under {seasonFolder}");
                continue;
            }

            roundFolders.Add((round, folder));
        }
    }
    else
    {
        foreach (string folder in Directory.GetDirectories(seasonFolder, "round-*"))
        {
            if (TryParseRoundFromFolder(folder, out int round))
                roundFolders.Add((round, folder));
        }

        roundFolders = roundFolders.OrderBy(x => x.Round).ToList();
    }

    foreach ((int round, string roundFolder) in roundFolders)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await log.WriteLineAsync($"Round {round}: importing saved JSON...");

        string calendarPath = Path.Combine(roundFolder, "calendar.json");
        if (!File.Exists(calendarPath))
        {
            result.Warnings.Add($"round {round}: calendar.json not found: {calendarPath}");
            continue;
        }

        try
        {
            string calendarJson = await File.ReadAllTextAsync(calendarPath, cancellationToken);
            if (debugImport)
                await log.WriteLineAsync($"  calendar: {calendarPath}");

            await importer.ImportCalendarAsync(calendarJson, tournamentId, seasonId, round, calendarPath, cancellationToken);
            result.CalendarsImported++;
            result.RoundsImported++;
        }
        catch (Exception ex)
        {
            result.Failures.Add($"round {round}: calendar import failed:{Environment.NewLine}{FormatImportException(ex)}");
            continue;
        }

        string eventsFolder = Path.Combine(roundFolder, "events");
        if (!Directory.Exists(eventsFolder))
        {
            result.Warnings.Add($"round {round}: events folder not found: {eventsFolder}");
            continue;
        }

        foreach (string eventFolder in Directory.GetDirectories(eventsFolder).OrderBy(x => x))
        {
            string eventFolderName = Path.GetFileName(eventFolder);
            if (!long.TryParse(eventFolderName, out long eventId))
            {
                result.Warnings.Add($"round {round}: skipped non-event folder: {eventFolder}");
                continue;
            }

            string incidentsPath = Path.Combine(eventFolder, "incidents.json");
            if (File.Exists(incidentsPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(incidentsPath, cancellationToken);
                    if (debugImport)
                        await log.WriteLineAsync($"  event {eventId}: incidents {incidentsPath}");

                    await importer.ImportIncidentsAsync(eventId, json, incidentsPath, cancellationToken);
                    result.IncidentsImported++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"event {eventId}: incidents import failed:{Environment.NewLine}{FormatImportException(ex)}");
                }
            }

            string statisticsPath = Path.Combine(eventFolder, "statistics.json");
            if (File.Exists(statisticsPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(statisticsPath, cancellationToken);
                    if (debugImport)
                        await log.WriteLineAsync($"  event {eventId}: statistics {statisticsPath}");

                    await importer.ImportStatisticsAsync(eventId, json, statisticsPath, cancellationToken);
                    result.StatisticsImported++;
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"event {eventId}: statistics import failed:{Environment.NewLine}{FormatImportException(ex)}");
                }
            }
        }
    }

    return result;
}


static string FormatImportException(Exception exception)
{
    var lines = new List<string>();
    Exception? current = exception;
    int depth = 0;
    while (current is not null && depth < 8)
    {
        string prefix = depth == 0 ? string.Empty : $"Inner[{depth}]: ";
        lines.Add($"{prefix}{current.GetType().FullName}: {current.Message}");
        current = current.InnerException;
        depth++;
    }

    return string.Join(Environment.NewLine, lines);
}



static void AddLiveOverOdds(IDictionary<double, double> target, ParsedArgs parsed)
{
    AddLiveOverOddsForLine(target, parsed, 1.5, "live-over-1.5", "live-over-15", "over-1.5", "over-15");
    AddLiveOverOddsForLine(target, parsed, 2.0, "live-over-2.0", "live-over-20", "over-2.0", "over-20");
    AddLiveOverOddsForLine(target, parsed, 2.5, "live-over-2.5", "live-over-25", "over-2.5", "over-25");
    AddLiveOverOddsForLine(target, parsed, 3.0, "live-over-3.0", "live-over-30", "over-3.0", "over-30");

    AddDynamicLiveOverOdds(target, parsed);

    // Generic syntax: --live-over-odds "1.5=1.40,2.0=1.85,2.5=2.45,3.5=4.10"
    if (!parsed.Has("live-over-odds"))
        return;

    string raw = parsed.RequiredString("live-over-odds");
    foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        string[] parts = token.Split('=', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double line) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double odds))
        {
            throw new ArgumentException("Argument --live-over-odds must use comma-separated line=odds pairs, for example 1.5=1.40,2.0=1.85,3.5=4.10.");
        }

        if (odds <= 1.0)
            throw new ArgumentException($"Live over odds for line {line:0.##} must be greater than 1.0.");

        target[LiveTotalPricer.NormalizeLineKey(line)] = odds;
    }
}

static void AddLiveUnderOdds(IDictionary<double, double> target, ParsedArgs parsed)
{
    AddLiveUnderOddsForLine(target, parsed, 1.5, "live-under-1.5", "live-under-15", "under-1.5", "under-15");
    AddLiveUnderOddsForLine(target, parsed, 2.0, "live-under-2.0", "live-under-20", "under-2.0", "under-20");
    AddLiveUnderOddsForLine(target, parsed, 2.5, "live-under-2.5", "live-under-25", "under-2.5", "under-25");
    AddLiveUnderOddsForLine(target, parsed, 3.0, "live-under-3.0", "live-under-30", "under-3.0", "under-30");

    AddDynamicLiveUnderOdds(target, parsed);

    // Generic syntax: --live-under-odds "1.5=3.60,2.0=2.10,2.5=1.65,3.5=1.90"
    if (!parsed.Has("live-under-odds"))
        return;

    string raw = parsed.RequiredString("live-under-odds");
    foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        string[] parts = token.Split('=', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double line) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double odds))
        {
            throw new ArgumentException("Argument --live-under-odds must use comma-separated line=odds pairs, for example 1.5=3.60,2.0=2.10,3.5=1.90.");
        }

        if (odds <= 1.0)
            throw new ArgumentException($"Live under odds for line {line:0.##} must be greater than 1.0.");

        target[LiveTotalPricer.NormalizeLineKey(line)] = odds;
    }
}

static void AddDynamicLiveOverOdds(IDictionary<double, double> target, ParsedArgs parsed)
{
    foreach (KeyValuePair<string, string?> pair in parsed.Values)
    {
        string key = pair.Key;
        if (!TryParseLiveOverLineArgument(key, out double line))
            continue;

        if (string.IsNullOrWhiteSpace(pair.Value))
            throw new ArgumentException($"Argument --{key} requires odds value.");

        if (!double.TryParse(pair.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double odds) || odds <= 1.0)
            throw new ArgumentException($"Argument --{key} must be a number greater than 1.0.");

        target[LiveTotalPricer.NormalizeLineKey(line)] = odds;
    }
}

static void AddDynamicLiveUnderOdds(IDictionary<double, double> target, ParsedArgs parsed)
{
    foreach (KeyValuePair<string, string?> pair in parsed.Values)
    {
        string key = pair.Key;
        if (!TryParseLiveUnderLineArgument(key, out double line))
            continue;

        if (string.IsNullOrWhiteSpace(pair.Value))
            throw new ArgumentException($"Argument --{key} requires odds value.");

        if (!double.TryParse(pair.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double odds) || odds <= 1.0)
            throw new ArgumentException($"Argument --{key} must be a number greater than 1.0.");

        target[LiveTotalPricer.NormalizeLineKey(line)] = odds;
    }
}

static bool TryParseLiveUnderLineArgument(string key, out double line)
{
    line = 0.0;

    string? suffix = null;
    if (key.StartsWith("live-under-", StringComparison.OrdinalIgnoreCase))
        suffix = key["live-under-".Length..];
    else if (key.StartsWith("under-", StringComparison.OrdinalIgnoreCase))
        suffix = key["under-".Length..];

    if (string.IsNullOrWhiteSpace(suffix) || suffix.Equals("odds", StringComparison.OrdinalIgnoreCase))
        return false;

    suffix = suffix.Replace('_', '.');

    if (suffix.Length == 2 && suffix.All(char.IsDigit) &&
        int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int compact) && compact > 0)
    {
        line = compact / 10.0;
        return line > 0;
    }

    if (double.TryParse(suffix, NumberStyles.Float, CultureInfo.InvariantCulture, out line))
        return line > 0;

    return false;
}

static bool TryParseLiveOverLineArgument(string key, out double line)
{
    line = 0.0;

    string? suffix = null;
    if (key.StartsWith("live-over-", StringComparison.OrdinalIgnoreCase))
        suffix = key["live-over-".Length..];
    else if (key.StartsWith("over-", StringComparison.OrdinalIgnoreCase))
        suffix = key["over-".Length..];

    if (string.IsNullOrWhiteSpace(suffix) || suffix.Equals("odds", StringComparison.OrdinalIgnoreCase))
        return false;

    suffix = suffix.Replace('_', '.');

    // Convenience form retained for old args: --live-over-35 means line 3.5.
    // Only two-digit compact values are interpreted this way; use decimal form for 4.25, 5.5, etc.
    if (suffix.Length == 2 && suffix.All(char.IsDigit) &&
        int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int compact) && compact > 0)
    {
        line = compact / 10.0;
        return line > 0;
    }

    if (double.TryParse(suffix, NumberStyles.Float, CultureInfo.InvariantCulture, out line))
        return line > 0;

    return false;
}

static void AddLiveOddsLinesToTargets(ICollection<double> targetLines, IDictionary<double, double> liveOddsByLine, bool explicitTargetLines)
{
    if (explicitTargetLines)
        return;

    foreach (double line in liveOddsByLine.Keys)
    {
        if (!targetLines.Any(existing => Math.Abs(LiveTotalPricer.NormalizeLineKey(existing) - line) < 1e-9))
            targetLines.Add(line);
    }
}

static void AddLiveOverOddsForLine(IDictionary<double, double> target, ParsedArgs parsed, double line, params string[] names)
{
    foreach (string name in names)
    {
        if (!parsed.Has(name))
            continue;

        double odds = parsed.Double(name, 0.0);
        if (odds <= 1.0)
            throw new ArgumentException($"Argument --{name} must be greater than 1.0.");

        target[LiveTotalPricer.NormalizeLineKey(line)] = odds;
    }
}

static void AddLiveUnderOddsForLine(IDictionary<double, double> target, ParsedArgs parsed, double line, params string[] names)
{
    foreach (string name in names)
    {
        if (!parsed.Has(name))
            continue;

        double odds = parsed.Double(name, 0.0);
        if (odds <= 1.0)
            throw new ArgumentException($"Argument --{name} must be greater than 1.0.");

        target[LiveTotalPricer.NormalizeLineKey(line)] = odds;
    }
}

static void AddRequiredIntList(ICollection<int> target, ParsedArgs parsed, string argumentName)
{
    string raw = parsed.RequiredString(argumentName);
    AddIntList(target, raw, argumentName, clearExisting: false);
}

static void AddOptionalIntList(ICollection<int> target, ParsedArgs parsed, string argumentName, bool clearExisting)
{
    if (!parsed.Has(argumentName))
        return;

    string raw = parsed.RequiredString(argumentName);
    AddIntList(target, raw, argumentName, clearExisting);
}

static void AddIntList(ICollection<int> target, string raw, string argumentName, bool clearExisting)
{
    if (clearExisting)
        target.Clear();

    foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!int.TryParse(token, out int value))
            throw new ArgumentException($"Argument --{argumentName} must contain comma-separated integers.");
        target.Add(value);
    }
}

static void AddOptionalDoubleList(ICollection<double> target, ParsedArgs parsed, string argumentName, bool clearExisting)
{
    if (!parsed.Has(argumentName))
        return;

    string raw = parsed.RequiredString(argumentName);
    if (clearExisting)
        target.Clear();

    foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
            throw new ArgumentException($"Argument --{argumentName} must contain comma-separated numbers.");
        target.Add(value);
    }
}

static void AddSeasonIds(List<int> seasonIds, ParsedArgs parsed)
{
    if (parsed.Has("season-ids"))
    {
        string raw = parsed.RequiredString("season-ids");
        foreach (string part in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out int seasonId) || seasonId <= 0)
                throw new ArgumentException($"Argument --season-ids contains invalid season id '{part}'. Use comma-separated integers, for example --season-ids 57783,88562.");

            if (!seasonIds.Contains(seasonId))
                seasonIds.Add(seasonId);
        }
    }

    if (parsed.Has("season-id"))
    {
        int seasonId = parsed.Int("season-id", 0);
        if (seasonId > 0 && !seasonIds.Contains(seasonId))
            seasonIds.Add(seasonId);
    }

    seasonIds.Sort();
}

static void AddRounds(ICollection<int> target, ParsedArgs parsed)
{
    if (parsed.Has("round"))
    {
        target.Add(parsed.RequiredInt("round"));
    }
    else if (parsed.Has("from-round") || parsed.Has("to-round"))
    {
        int from = parsed.RequiredInt("from-round");
        int to = parsed.RequiredInt("to-round");
        if (to < from)
            throw new ArgumentException("to-round must be greater than or equal to from-round.");

        for (int round = from; round <= to; round++)
            target.Add(round);
    }
    else
    {
        throw new ArgumentException("Provide either --round or --from-round and --to-round.");
    }
}

static string FindRoundFolder(string seasonFolder, int round)
{
    string padded = Path.Combine(seasonFolder, $"round-{round:00}");
    if (Directory.Exists(padded))
        return padded;

    string plain = Path.Combine(seasonFolder, $"round-{round}");
    if (Directory.Exists(plain))
        return plain;

    return string.Empty;
}

static bool TryParseRoundFromFolder(string folder, out int round)
{
    string name = Path.GetFileName(folder);
    if (name.StartsWith("round-", StringComparison.OrdinalIgnoreCase))
        return int.TryParse(name["round-".Length..], out round);

    round = 0;
    return false;
}

static void PrintDownloadResult(SofaScoreDownloadResult result)
{
    Console.WriteLine();
    Console.WriteLine("Download done.");
    Console.WriteLine($"Rounds: {result.RoundsDownloaded}");
    Console.WriteLine($"Events discovered: {result.EventsDiscovered}");
    Console.WriteLine($"Files written: {result.FilesWritten}");
    Console.WriteLine($"Files skipped: {result.FilesSkipped}");
    Console.WriteLine($"Warnings: {result.Warnings.Count}");
    Console.WriteLine($"Failures: {result.Failures.Count}");

    if (result.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Warnings:");
        foreach (string warning in result.Warnings)
            Console.WriteLine($"- {warning}");
    }

    if (result.Failures.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Failures:");
        foreach (string failure in result.Failures)
            Console.WriteLine($"- {failure}");
    }
}

internal sealed class SofaScoreImportResult
{
    public int RoundsImported { get; set; }
    public int CalendarsImported { get; set; }
    public int IncidentsImported { get; set; }
    public int StatisticsImported { get; set; }
    public List<string> Warnings { get; } = [];
    public List<string> Failures { get; } = [];
}
