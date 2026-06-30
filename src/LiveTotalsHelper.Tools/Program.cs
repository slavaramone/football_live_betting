using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Infrastructure.Flashscore;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Flashscore;
using LiveTotalsHelper.Infrastructure.SofaScore;
using LiveTotalsHelper.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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
        "download-flashscore" => await RunDownloadFlashscore(commandArgs),
        "download-flashscore-fixtures" => await RunDownloadFlashscoreFixtures(commandArgs),
        "parse-flashscore-fixtures" => await RunDownloadFlashscoreFixtures(commandArgs),
        "download-sofascore" => await RunDownloadSofaScore(commandArgs),
        "import-flashscore" => await RunImportFlashscore(commandArgs),
        "import-flashscore-fixtures" => await RunImportFlashscoreFixtures(commandArgs),
        "build-after-goal-events" => await RunBuildAfterGoalEvents(commandArgs),
        "analyze-after-goal-angles" => await RunAnalyzeAfterGoalAngles(commandArgs),
        "build-after-goal-team-profiles" => await RunBuildAfterGoalTeamProfiles(commandArgs),
        "build-after-goal-entry-gates" => await RunBuildAfterGoalEntryGates(commandArgs),
        "validate-profiles" => RunValidateProfiles(commandArgs),
        "validate-db" => await RunValidateDb(commandArgs),
        "db-validate" => await RunValidateDb(commandArgs),
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

static async Task<int> RunDownloadFlashscore(string[] args)
{
    var parsed = ArgsParser.Parse(args);
    string seasonYear = parsed.String("season-year", string.Empty);
    int defaultYear = parsed.Has("default-year")
        ? parsed.Int("default-year", DateTimeOffset.UtcNow.Year)
        : TryParseSeasonYear(seasonYear) ?? DateTimeOffset.UtcNow.Year;

    var options = new FlashscoreDownloadOptions
    {
        Url = parsed.RequiredString("url"),
        League = parsed.RequiredString("league"),
        TournamentId = parsed.RequiredInt("tournament-id"),
        SeasonId = parsed.RequiredInt("season-id"),
        SeasonName = parsed.String("season-name", string.Empty),
        SeasonYear = seasonYear,
        CountryName = parsed.String("country", string.Empty),
        CountryCode = parsed.String("country-code", string.Empty),
        OutputRoot = parsed.String("output", "data/flashscore"),
        Overwrite = parsed.Bool("overwrite", false),
        DownloadIncidents = parsed.Bool("incidents", true),
        DownloadStatistics = parsed.Has("skip-stat") ? false : parsed.Bool("statistics", true),
        DownloadOdds = parsed.Bool("odds", true),
        SkipPlayoffs = parsed.Has("include-playoffs")
            ? !parsed.Bool("include-playoffs", true)
            : parsed.Bool("skip-playoffs", true),
        Headless = parsed.Has("show-browser") ? false : parsed.Bool("headless", true),
        RenderWaitMs = parsed.Int("render-wait-ms", 3_000),
        DetailWaitMs = parsed.Int("detail-wait-ms", 1_000),
        ShowMoreWaitMs = parsed.Int("show-more-wait-ms", 2_000),
        MaxShowMoreClicks = parsed.Int("max-show-more-clicks", 40),
        DelayMs = parsed.Int("delay-ms", 450),
        DefaultYear = defaultYear
    };

    AddOptionalRounds(options.Rounds, parsed);

    var downloader = new FlashscoreDownloader(new SofaScoreJsonFileStore());
    FlashscoreDownloadResult result = await downloader.DownloadAsync(options, Console.Out, CancellationToken.None);
    PrintFlashscoreDownloadResult(result);

    return result.Failures.Count == 0 ? 0 : 1;
}

static async Task<int> RunDownloadFlashscoreFixtures(string[] args)
{
    var parsed = ArgsParser.Parse(args);
    LeagueProfile? profile = await LoadOptionalProfileAsync(parsed);

    string seasonYear = parsed.String("season-year", profile?.FlashscoreSeasonYear ?? string.Empty);
    int seasonId = parsed.Has("season-id")
        ? parsed.RequiredInt("season-id")
        : profile?.FlashscoreSeasonId > 0
            ? profile.FlashscoreSeasonId
            : profile?.CurrentSeasonId > 0
                ? profile.CurrentSeasonId
                : DateTimeOffset.UtcNow.Year;

    if (string.IsNullOrWhiteSpace(seasonYear))
        seasonYear = seasonId.ToString(CultureInfo.InvariantCulture);

    int defaultYear = parsed.Has("default-year")
        ? parsed.Int("default-year", DateTimeOffset.UtcNow.Year)
        : TryParseSeasonYear(seasonYear) ?? seasonId;

    string league = parsed.String("league", profile?.League ?? string.Empty);
    string seasonName = parsed.String("season-name", profile?.FlashscoreSeasonName ?? seasonYear);
    if (string.IsNullOrWhiteSpace(seasonName))
        seasonName = seasonYear;

    int tournamentId = parsed.Has("tournament-id")
        ? parsed.RequiredInt("tournament-id")
        : profile?.FlashscoreTournamentId > 0
            ? profile.FlashscoreTournamentId
            : StablePositiveInt($"flashscore:tournament:{league}");

    var options = new FlashscoreDownloadOptions
    {
        Url = parsed.String("url", profile?.FlashscoreFixturesUrl ?? string.Empty),
        League = league,
        TournamentId = tournamentId,
        SeasonId = seasonId,
        SeasonName = seasonName,
        SeasonYear = seasonYear,
        CountryName = parsed.String("country", profile?.FlashscoreCountry ?? string.Empty),
        CountryCode = parsed.String("country-code", profile?.FlashscoreCountryCode ?? string.Empty),
        OutputRoot = parsed.String("output", "data/flashscore"),
        Overwrite = parsed.Bool("overwrite", true),
        DownloadIncidents = false,
        DownloadStatistics = false,
        DownloadOdds = false,
        FixturesOnly = true,
        NearestRoundOnly = true,
        SkipPlayoffs = parsed.Has("include-playoffs")
            ? !parsed.Bool("include-playoffs", true)
            : parsed.Bool("skip-playoffs", true),
        Headless = parsed.Has("show-browser") ? false : parsed.Bool("headless", true),
        RenderWaitMs = parsed.Int("render-wait-ms", 3_000),
        DetailWaitMs = parsed.Int("detail-wait-ms", 1_000),
        ShowMoreWaitMs = parsed.Int("show-more-wait-ms", 2_000),
        MaxShowMoreClicks = 0,
        DelayMs = parsed.Int("delay-ms", 150),
        DefaultYear = defaultYear
    };

    if (string.IsNullOrWhiteSpace(options.Url))
        throw new ArgumentException("Missing required argument --url, or provide --profile with flashscoreFixturesUrl set.");
    if (string.IsNullOrWhiteSpace(options.League))
        throw new ArgumentException("Missing required argument --league, or provide --profile with league set.");

    AddOptionalRounds(options.Rounds, parsed);

    var downloader = new FlashscoreDownloader(new SofaScoreJsonFileStore());
    FlashscoreDownloadResult result = await downloader.DownloadAsync(options, Console.Out, CancellationToken.None);
    PrintFlashscoreDownloadResult(result);

    return result.Failures.Count == 0 ? 0 : 1;
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
        DownloadStatistics = parsed.Has("skip-stat") ? false : parsed.Bool("statistics", true),
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

static async Task<int> RunValidateDb(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    var options = new DbValidationOptions
    {
        League = parsed.String("league", string.Empty),
        SeasonId = parsed.Int("season-id", 0),
        FailOnWarnings = parsed.Bool("fail-on-warnings", false),
        MaxExamplesPerCheck = parsed.Int("max-examples", 20),
        OutputPath = parsed.String("output", parsed.String("report", string.Empty))
    };

    if (parsed.Has("round") || parsed.Has("rounds") || parsed.Has("from-round") || parsed.Has("round-from") || parsed.Has("to-round"))
        AddRounds(options.Rounds, parsed);

    IConfiguration configuration = BuildConfiguration();
    await using LiveTotalsDbContext dbContext = CreateDbContext(configuration);
    var runner = new DbValidationRunner(dbContext, options);
    DbValidationResult result = await runner.RunAsync(CancellationToken.None);

    var reportBuilder = new StringBuilder();
    using (var writer = new StringWriter(reportBuilder, CultureInfo.InvariantCulture))
        WriteDbValidationReport(writer, result, options);

    string reportText = reportBuilder.ToString();
    Console.Write(reportText);

    if (!string.IsNullOrWhiteSpace(options.OutputPath))
    {
        string fullPath = Path.GetFullPath(options.OutputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(fullPath, reportText, Encoding.UTF8, CancellationToken.None);
        Console.WriteLine();
        Console.WriteLine($"Validation report written: {fullPath}");
    }

    if (result.ErrorCount > 0)
        return 1;

    if (options.FailOnWarnings && result.WarningCount > 0)
        return 1;

    return 0;
}

static async Task<int> RunBuildAfterGoalEvents(string[] args)
{
    var parsed = ArgsParser.Parse(args);
    LeagueProfile? profile = await LoadOptionalProfileAsync(parsed);

    string league = parsed.String("league", profile?.League ?? string.Empty);
    int tournamentId = parsed.Has("tournament-id")
        ? parsed.RequiredInt("tournament-id")
        : profile?.FlashscoreTournamentId ?? 0;

    if (string.IsNullOrWhiteSpace(league) && tournamentId <= 0)
        throw new ArgumentException("Provide --profile, --league, or --tournament-id.");

    string outputPath = parsed.String("output", DefaultAfterGoalEventsOutputPath(profile, league, tournamentId));
    string fullOutputPath = Path.GetFullPath(outputPath);
    string outputDirectory = Path.GetDirectoryName(fullOutputPath) ?? Directory.GetCurrentDirectory();
    string warningsPath = Path.Combine(outputDirectory, "after-goal-events-warnings.csv");

    var options = new AfterGoalEventDatasetOptions
    {
        LeagueKey = profile?.Key ?? parsed.String("league-key", string.Empty),
        LeagueName = league,
        TournamentId = tournamentId,
        Season = parsed.String("season", parsed.String("season-id", string.Empty)),
        FromSeason = parsed.String("from-season", profile?.Seasons.DefaultTrainFrom.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
        ToSeason = parsed.String("to-season", profile?.Seasons.DefaultTestSeason.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
        MinMinute = parsed.Has("min-minute") ? parsed.RequiredInt("min-minute") : null,
        MaxMinute = parsed.Has("max-minute") ? parsed.RequiredInt("max-minute") : null
    };

    if (options.MinMinute.HasValue && options.MinMinute.Value < 0)
        throw new ArgumentException("Argument --min-minute must be zero or greater.");
    if (options.MaxMinute.HasValue && options.MaxMinute.Value < 0)
        throw new ArgumentException("Argument --max-minute must be zero or greater.");
    if (options.MinMinute.HasValue && options.MaxMinute.HasValue && options.MaxMinute.Value < options.MinMinute.Value)
        throw new ArgumentException("Argument --max-minute must be greater than or equal to --min-minute.");

    IConfiguration configuration = BuildConfiguration();
    await using LiveTotalsDbContext dbContext = CreateDbContext(configuration);
    var builder = new AfterGoalEventDatasetBuilder(dbContext);
    AfterGoalEventBuildResult result = await builder.BuildAsync(options, CancellationToken.None);

    await AfterGoalEventDatasetBuilder.WriteRowsCsvAsync(fullOutputPath, result.Rows, CancellationToken.None);
    await AfterGoalEventDatasetBuilder.WriteWarningsCsvAsync(warningsPath, result.Warnings, CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("After-goal event dataset build done.");
    Console.WriteLine($"Total matches scanned: {result.TotalMatchesScanned}");
    Console.WriteLine($"Finished matches with final score: {result.FinishedMatchesWithFinalScore}");
    Console.WriteLine($"Matches included: {result.MatchesIncluded}");
    Console.WriteLine($"Matches skipped because no valid goals: {result.MatchesSkippedNoValidGoals}");
    Console.WriteLine($"Matches skipped because reconstructed final score mismatched official final score: {result.MatchesSkippedFinalScoreMismatch}");
    Console.WriteLine($"Goal rows written: {result.Rows.Count}");
    Console.WriteLine($"Output path: {fullOutputPath}");
    Console.WriteLine($"Warnings path: {Path.GetFullPath(warningsPath)}");
    if (result.Warnings.Count > 0)
    {
        Console.WriteLine("Warnings by reason:");
        foreach (var group in result.Warnings.GroupBy(x => x.Reason).OrderByDescending(x => x.Count()).ThenBy(x => x.Key))
            Console.WriteLine($"  {group.Key}: {group.Count()}");
    }

    return 0;
}

static async Task<int> RunAnalyzeAfterGoalAngles(string[] args)
{
    var parsed = ArgsParser.Parse(args);
    LeagueProfile? profile = await LoadOptionalProfileAsync(parsed);
    ValidateAllowedOptions(parsed, "analyze-after-goal-angles",
    [
        "profile",
        "profiles-file",
        "input",
        "output-dir",
        "train-from-season",
        "train-to-season",
        "test-season",
        "min-sample",
        "strong-sample",
        "shrink-k",
        "include-opponent-pairs"
    ]);

    string inputPath = parsed.String("input", profile is null ? string.Empty : LeagueProfileStore.ResolveProfileArtifactPath(profile, profile.Artifacts.AfterGoalEventsFile));
    if (string.IsNullOrWhiteSpace(inputPath))
        throw new ArgumentException("Provide --input or --profile.");

    string defaultOutputDirectory = profile is null
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Directory.GetCurrentDirectory(), "after-goal-angles")
        : LeagueProfileStore.ResolveProfileArtifactPath(profile, profile.Artifacts.AfterGoalAnglesDir);
    var options = new AfterGoalAngleAnalysisOptions
    {
        InputPath = inputPath,
        OutputDirectory = parsed.String("output-dir", defaultOutputDirectory),
        TrainFromSeason = parsed.String("train-from-season", profile?.Seasons.DefaultTrainFrom > 0 ? profile.Seasons.DefaultTrainFrom.ToString(CultureInfo.InvariantCulture) : string.Empty),
        TrainToSeason = parsed.String("train-to-season", profile?.Seasons.DefaultTrainTo > 0 ? profile.Seasons.DefaultTrainTo.ToString(CultureInfo.InvariantCulture) : string.Empty),
        TestSeason = parsed.String("test-season", profile?.Seasons.DefaultTestSeason > 0 ? profile.Seasons.DefaultTestSeason.ToString(CultureInfo.InvariantCulture) : string.Empty),
        MinSample = parsed.Int("min-sample", profile?.AfterGoalAngles.MinSample ?? 30),
        StrongSample = parsed.Int("strong-sample", profile?.AfterGoalAngles.StrongSample ?? 80),
        ShrinkK = parsed.Double("shrink-k", profile?.AfterGoalAngles.ShrinkK ?? 50),
        IncludeOpponentPairs = parsed.Bool("include-opponent-pairs", profile?.AfterGoalAngles.IncludeOpponentPairsDefault ?? false),
        RawCommandLine = string.Join(" ", ["analyze-after-goal-angles", .. args])
    };

    if (options.MinSample <= 0)
        throw new ArgumentException("Argument --min-sample must be positive.");
    if (options.StrongSample < options.MinSample)
        throw new ArgumentException("Argument --strong-sample must be greater than or equal to --min-sample.");
    if (options.ShrinkK < 0)
        throw new ArgumentException("Argument --shrink-k must be zero or greater.");

    var analyzer = new AfterGoalAngleAnalyzer();
    AfterGoalAngleAnalysisResult result;
    try
    {
        result = await analyzer.AnalyzeAsync(options, CancellationToken.None);
    }
    catch (ArgumentException ex)
    {
        await WriteAngleAnalysisErrorAsync(options, ex, CancellationToken.None);
        throw;
    }

    ValidateProfileLeagueKeys(profile, result.LeagueKeys);

    Console.WriteLine();
    Console.WriteLine("After-goal angle analysis split resolved.");
    Console.WriteLine($"Input path: {Path.GetFullPath(options.InputPath)}");
    Console.WriteLine($"Output directory: {Path.GetFullPath(options.OutputDirectory)}");
    Console.WriteLine($"Seasons found in input: {string.Join(", ", result.InputSeasons)}");
    Console.WriteLine($"Split mode: {result.SplitMode}");
    Console.WriteLine($"Requested train-from-season: {PrintableOption(options.TrainFromSeason)}");
    Console.WriteLine($"Requested train-to-season: {PrintableOption(options.TrainToSeason)}");
    Console.WriteLine($"Requested test-season: {PrintableOption(options.TestSeason)}");
    Console.WriteLine($"Resolved train seasons: {string.Join(", ", result.TrainSeasons)}");
    Console.WriteLine($"Resolved test season: {result.TestSeason}");
    Console.WriteLine($"Train rows: {result.TrainRows}");
    Console.WriteLine($"Test rows: {result.TestRows}");

    await AfterGoalAngleReportWriter.WriteAsync(options.OutputDirectory, options, result, CancellationToken.None);

    Console.WriteLine("After-goal angle analysis done.");
    Console.WriteLine($"Total rows read: {result.TotalRowsRead}");
    Console.WriteLine($"Rows used: {result.RowsUsed}");
    Console.WriteLine($"Train seasons: {string.Join(", ", result.TrainSeasons)}");
    Console.WriteLine($"Test season: {result.TestSeason}");
    Console.WriteLine("Report rows:");
    foreach (var report in result.ReportRowCounts.OrderBy(x => x.Key))
        Console.WriteLine($"  {report.Key}: {report.Value}");

    if (result.Warnings.Count > 0)
    {
        Console.WriteLine("Warnings:");
        foreach (string warning in result.Warnings)
            Console.WriteLine($"  - {warning}");
    }

    return 0;
}

static void ValidateAllowedOptions(ParsedArgs parsed, string command, IReadOnlyCollection<string> allowedOptions)
{
    var allowed = new HashSet<string>(allowedOptions, StringComparer.OrdinalIgnoreCase);
    List<string> unknown = parsed.Values.Keys.Where(x => !allowed.Contains(x)).OrderBy(x => x).ToList();
    if (unknown.Count > 0)
        throw new ArgumentException($"Unknown option(s) for {command}: {string.Join(", ", unknown.Select(x => "--" + x))}.");
}

static string PrintableOption(string value)
    => string.IsNullOrWhiteSpace(value) ? "<none>" : value;

static async Task<int> RunBuildAfterGoalTeamProfiles(string[] args)
{
    var parsed = ArgsParser.Parse(args);
    LeagueProfile? profile = await LoadOptionalProfileAsync(parsed);
    ValidateAllowedOptions(parsed, "build-after-goal-team-profiles",
    [
        "profile",
        "profiles-file",
        "angles-dir",
        "output-dir",
        "min-train-sample",
        "min-test-sample",
        "min-train-abs-residual",
        "min-test-abs-residual",
        "strong-test-abs-residual",
        "require-test-confirmation",
        "watchlist-enabled",
        "watchlist-train-sample-tolerance",
        "watchlist-test-sample-tolerance",
        "watchlist-residual-tolerance"
    ]);

    string anglesDirectory = parsed.String("angles-dir", profile is null ? string.Empty : LeagueProfileStore.ResolveProfileArtifactPath(profile, profile.Artifacts.AfterGoalAnglesDir));
    if (string.IsNullOrWhiteSpace(anglesDirectory))
        throw new ArgumentException("Provide --angles-dir or --profile.");

    string defaultOutputDirectory = profile is null
        ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(anglesDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))) ?? Directory.GetCurrentDirectory(), "after-goal-profiles")
        : LeagueProfileStore.ResolveProfileArtifactPath(profile, profile.Artifacts.AfterGoalProfilesDir);
    string outputDirectory = parsed.String("output-dir", defaultOutputDirectory);

    var options = new AfterGoalTeamProfileOptions
    {
        AnglesDirectory = anglesDirectory,
        OutputDirectory = outputDirectory,
        MinTrainSample = parsed.Int("min-train-sample", profile?.AfterGoalTeamProfiles.MinTrainSample ?? 50),
        MinTestSample = parsed.Int("min-test-sample", profile?.AfterGoalTeamProfiles.MinTestSample ?? 15),
        MinTrainAbsResidual = parsed.Double("min-train-abs-residual", profile?.AfterGoalTeamProfiles.MinTrainAbsResidual ?? 0.10),
        MinTestAbsResidual = parsed.Double("min-test-abs-residual", profile?.AfterGoalTeamProfiles.MinTestAbsResidual ?? 0.05),
        StrongTestAbsResidual = parsed.Double("strong-test-abs-residual", profile?.AfterGoalTeamProfiles.StrongTestAbsResidual ?? 0.15),
        RequireTestConfirmation = parsed.Bool("require-test-confirmation", profile?.AfterGoalTeamProfiles.RequireTestConfirmation ?? true),
        WatchlistEnabled = parsed.Bool("watchlist-enabled", profile?.AfterGoalTeamProfiles.Watchlist.Enabled ?? true),
        WatchlistTrainSampleTolerance = parsed.Int("watchlist-train-sample-tolerance", profile?.AfterGoalTeamProfiles.Watchlist.TrainSampleTolerance ?? 10),
        WatchlistTestSampleTolerance = parsed.Int("watchlist-test-sample-tolerance", profile?.AfterGoalTeamProfiles.Watchlist.TestSampleTolerance ?? 5),
        WatchlistResidualTolerance = parsed.Double("watchlist-residual-tolerance", profile?.AfterGoalTeamProfiles.Watchlist.ResidualTolerance ?? 0.03)
    };

    if (options.MinTrainSample <= 0)
        throw new ArgumentException("Argument --min-train-sample must be positive.");
    if (options.MinTestSample <= 0)
        throw new ArgumentException("Argument --min-test-sample must be positive.");
    if (options.MinTrainAbsResidual < 0 || options.MinTestAbsResidual < 0 || options.StrongTestAbsResidual < 0)
        throw new ArgumentException("Residual thresholds must be zero or greater.");
    if (options.WatchlistTrainSampleTolerance < 0 || options.WatchlistTestSampleTolerance < 0 || options.WatchlistResidualTolerance < 0)
        throw new ArgumentException("Watchlist tolerances must be zero or greater.");

    var builder = new AfterGoalTeamProfileBuilder();
    AfterGoalTeamProfileResult result = await builder.BuildAsync(options, CancellationToken.None);
    ValidateProfileLeagueKeys(profile, result.Profiles.Select(x => x.LeagueKey)
        .Concat(result.UsableSignals.Select(x => x.LeagueKey))
        .Concat(result.WatchlistSignals.Select(x => x.LeagueKey))
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    await AfterGoalTeamProfileReportWriter.WriteAsync(options.OutputDirectory, options, result, CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("After-goal team profile build done.");
    Console.WriteLine($"Angles directory: {Path.GetFullPath(options.AnglesDirectory)}");
    Console.WriteLine($"Output directory: {Path.GetFullPath(options.OutputDirectory)}");
    Console.WriteLine($"Source train seasons: {result.SourceTrainSeasons}");
    Console.WriteLine($"Source test season: {result.SourceTestSeason}");
    Console.WriteLine($"Teams analyzed: {result.TeamsAnalyzed}");
    Console.WriteLine($"Usable scoring signals: {result.UsableScoringSignalsCount}");
    Console.WriteLine($"Usable conceding signals: {result.UsableConcedingSignalsCount}");
    Console.WriteLine($"Watchlist signals: {result.WatchlistSignals.Count}");
    Console.WriteLine($"Watchlist scoring signals: {result.WatchlistAfterScoringCount}");
    Console.WriteLine($"Watchlist conceding signals: {result.WatchlistAfterConcedingCount}");
    Console.WriteLine($"Unstable signals: {result.UnstableSignalsCount}");
    Console.WriteLine($"No-signal teams: {result.NoSignalCount}");
    if (result.Warnings.Count > 0)
    {
        Console.WriteLine("Warnings:");
        foreach (string warning in result.Warnings)
            Console.WriteLine($"  - {warning}");
    }

    return 0;
}

static async Task<int> RunBuildAfterGoalEntryGates(string[] args)
{
    var parsed = ArgsParser.Parse(args);
    LeagueProfile? profile = await LoadOptionalProfileAsync(parsed);
    ValidateAllowedOptions(parsed, "build-after-goal-entry-gates",
    [
        "profile",
        "profiles-file",
        "events",
        "angles-dir",
        "profiles-dir",
        "output-dir",
        "train-from-season",
        "train-to-season",
        "test-season",
        "include-watchlist",
        "min-train-state-sample",
        "min-test-state-sample",
        "min-state-residual",
        "strong-state-residual",
        "require-test-confirmation",
        "conflict-policy"
    ]);

    string eventsPath = parsed.String("events", profile is null ? string.Empty : LeagueProfileStore.ResolveProfileArtifactPath(profile, profile.Artifacts.AfterGoalEventsFile));
    string anglesDirectory = parsed.String("angles-dir", profile is null ? string.Empty : LeagueProfileStore.ResolveProfileArtifactPath(profile, profile.Artifacts.AfterGoalAnglesDir));
    string profilesDirectory = parsed.String("profiles-dir", profile is null ? string.Empty : LeagueProfileStore.ResolveProfileArtifactPath(profile, profile.Artifacts.AfterGoalProfilesDir));
    string outputDirectory = parsed.String("output-dir", profile is null ? string.Empty : LeagueProfileStore.ResolveProfileArtifactPath(profile, profile.Artifacts.AfterGoalEntryGatesDir));

    if (string.IsNullOrWhiteSpace(eventsPath))
        throw new ArgumentException("Provide --events or --profile.");
    if (string.IsNullOrWhiteSpace(anglesDirectory))
        throw new ArgumentException("Provide --angles-dir or --profile.");
    if (string.IsNullOrWhiteSpace(profilesDirectory))
        throw new ArgumentException("Provide --profiles-dir or --profile.");
    if (string.IsNullOrWhiteSpace(outputDirectory))
        throw new ArgumentException("Provide --output-dir or --profile.");

    var options = new AfterGoalEntryGateOptions
    {
        EventsPath = eventsPath,
        AnglesDirectory = anglesDirectory,
        ProfilesDirectory = profilesDirectory,
        OutputDirectory = outputDirectory,
        TrainFromSeason = parsed.String("train-from-season", profile?.Seasons.DefaultTrainFrom > 0 ? profile.Seasons.DefaultTrainFrom.ToString(CultureInfo.InvariantCulture) : string.Empty),
        TrainToSeason = parsed.String("train-to-season", profile?.Seasons.DefaultTrainTo > 0 ? profile.Seasons.DefaultTrainTo.ToString(CultureInfo.InvariantCulture) : string.Empty),
        TestSeason = parsed.String("test-season", profile?.Seasons.DefaultTestSeason > 0 ? profile.Seasons.DefaultTestSeason.ToString(CultureInfo.InvariantCulture) : string.Empty),
        ProfileLeagueKey = profile?.Key ?? string.Empty,
        IncludeWatchlist = parsed.Bool("include-watchlist", profile?.AfterGoalEntryGates.IncludeWatchlist ?? true),
        MinTrainStateSample = parsed.Int("min-train-state-sample", profile?.AfterGoalEntryGates.MinTrainStateSample ?? 15),
        MinTestStateSample = parsed.Int("min-test-state-sample", profile?.AfterGoalEntryGates.MinTestStateSample ?? 5),
        MinStateResidual = parsed.Double("min-state-residual", profile?.AfterGoalEntryGates.MinStateResidual ?? 0.05),
        StrongStateResidual = parsed.Double("strong-state-residual", profile?.AfterGoalEntryGates.StrongStateResidual ?? 0.15),
        RequireTestConfirmation = parsed.Bool("require-test-confirmation", profile?.AfterGoalEntryGates.RequireTestConfirmation ?? true),
        ConflictPolicy = parsed.String("conflict-policy", profile?.AfterGoalEntryGates.ConflictPolicy ?? "NoBet"),
        MarketGateRequired = profile?.AfterGoalEntryGates.MarketGateRequired ?? true
    };

    var builder = new AfterGoalEntryGateBuilder();
    AfterGoalEntryGateResult result = await builder.BuildAsync(options, CancellationToken.None);
    await AfterGoalEntryGateReportWriter.WriteAsync(options.OutputDirectory, options, result, CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("After-goal entry gate build done.");
    Console.WriteLine($"Events: {Path.GetFullPath(options.EventsPath)}");
    Console.WriteLine($"Angles directory: {Path.GetFullPath(options.AnglesDirectory)}");
    Console.WriteLine($"Profiles directory: {Path.GetFullPath(options.ProfilesDirectory)}");
    Console.WriteLine($"Output directory: {Path.GetFullPath(options.OutputDirectory)}");
    Console.WriteLine($"League: {result.LeagueKey}");
    Console.WriteLine($"Train seasons: {result.TrainSeasons}");
    Console.WriteLine($"Test season: {result.TestSeason}");
    Console.WriteLine($"Strict signals analyzed: {result.StrictSignalsAnalyzed}");
    Console.WriteLine($"Watchlist signals analyzed: {result.WatchlistSignalsAnalyzed}");
    Console.WriteLine($"Context gate rows: {result.ContextGates.Count}");
    Console.WriteLine($"Active rules: {result.ActiveEntryRules}");
    Console.WriteLine($"Watchlist rules: {result.WatchlistEntryRules}");
    Console.WriteLine($"Too-thin rules: {result.TooThinRules}");
    Console.WriteLine($"No-usable-gate rules: {result.NoUsableGateRules}");
    Console.WriteLine("Generated files:");
    Console.WriteLine($"  {Path.Combine(Path.GetFullPath(options.OutputDirectory), "after-goal-profile-context-gates.csv")}");
    Console.WriteLine($"  {Path.Combine(Path.GetFullPath(options.OutputDirectory), "after-goal-entry-rules.csv")}");
    Console.WriteLine($"  {Path.Combine(Path.GetFullPath(options.OutputDirectory), "after-goal-entry-gates-summary.json")}");
    if (result.Warnings.Count > 0)
    {
        Console.WriteLine("Warnings:");
        foreach (string warning in result.Warnings)
            Console.WriteLine($"  - {warning}");
    }

    return 0;
}

static int RunValidateProfiles(string[] args)
{
    var parsed = ArgsParser.Parse(args);
    string profilesFile = parsed.String("profiles-file", "config/league-profiles.json");
    LeagueProfileValidationResult result = LeagueProfileStore.ValidateFile(profilesFile);

    Console.WriteLine();
    Console.WriteLine("Profile validation done.");
    Console.WriteLine($"Profiles file: {Path.GetFullPath(LeagueProfileStore.ResolvePath(profilesFile))}");
    Console.WriteLine($"Errors: {result.Errors.Count}");
    Console.WriteLine($"Warnings: {result.Warnings.Count}");

    foreach (string error in result.Errors)
        Console.WriteLine($"ERROR: {error}");
    foreach (string warning in result.Warnings)
        Console.WriteLine($"Warning: {warning}");

    return result.IsValid ? 0 : 1;
}

static async Task WriteAngleAnalysisErrorAsync(AfterGoalAngleAnalysisOptions options, Exception exception, CancellationToken cancellationToken)
{
    string outputDirectory = Path.GetFullPath(options.OutputDirectory);
    Directory.CreateDirectory(outputDirectory);

    foreach (string fileName in AngleAnalysisOutputFileNames())
    {
        string path = Path.Combine(outputDirectory, fileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    var errorSummary = new
    {
        Input = Path.GetFullPath(options.InputPath),
        OutputDir = outputDirectory,
        options.TrainFromSeason,
        options.TrainToSeason,
        options.TestSeason,
        Error = exception.Message,
        Timestamp = DateTimeOffset.UtcNow
    };

    string json = JsonSerializer.Serialize(errorSummary, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(Path.Combine(outputDirectory, "after-goal-angle-analysis-error.json"), json, Encoding.UTF8, cancellationToken);
}

static IEnumerable<string> AngleAnalysisOutputFileNames()
{
    yield return "league-after-goal-angles.csv";
    yield return "league-minute-after-goal-angles.csv";
    yield return "team-after-scoring-angles.csv";
    yield return "team-after-conceding-angles.csv";
    yield return "team-minute-after-scoring-angles.csv";
    yield return "team-minute-after-conceding-angles.csv";
    yield return "opponent-pair-after-goal-angles.csv";
    yield return "after-goal-angle-analysis-summary.json";
}

static async Task<int> RunImportFlashscore(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    string league = parsed.RequiredString("league");
    int tournamentId = parsed.Int("tournament-id", 0);
    int seasonId = parsed.RequiredInt("season-id");
    string inputRoot = parsed.String("input", parsed.String("output", "data/flashscore"));
    bool debugImport = parsed.Bool("debug-import", false);

    var rounds = new List<int>();
    if (parsed.Has("round") || parsed.Has("rounds") || parsed.Has("from-round") || parsed.Has("round-from") || parsed.Has("to-round"))
        AddRounds(rounds, parsed);

    IConfiguration configuration = BuildConfiguration();
    await using LiveTotalsDbContext dbContext = await DatabaseMigrator.CreateMigratedDbContextAsync(configuration, Console.Out, CancellationToken.None);
    var importer = new FlashscoreDbImporter(dbContext);

    FlashscoreImportResult result = await ImportFlashscoreFolderAsync(importer, inputRoot, league, tournamentId, seasonId, rounds, calendarOnly: false, debugImport, Console.Out, CancellationToken.None);

    PrintImportResult("Import done.", result, includeDetails: true);
    return result.Failures.Count == 0 ? 0 : 1;
}

static string DefaultAfterGoalEventsOutputPath(LeagueProfile? profile, string league, int tournamentId)
{
    if (profile is not null)
        return LeagueProfileStore.ResolveProfileArtifactPath(profile, profile.Artifacts.AfterGoalEventsFile);

    string folderName = FileNameSanitizer.Slugify(Coalesce(profile?.Key, league, tournamentId > 0 ? $"tournament-{tournamentId}" : "unknown-league"));
    return Path.Combine(@"C:\Temp\football_data\models", folderName, "after-goal-events.csv");
}

static void ValidateProfileLeagueKeys(LeagueProfile? profile, IEnumerable<string> inputLeagueKeys)
{
    if (profile is null || !profile.Safety.FailOnLeagueKeyMismatch)
        return;

    List<string> keys = inputLeagueKeys
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x)
        .ToList();
    if (keys.Count == 0)
        return;

    string expected = profile.Key;
    List<string> mismatches = keys
        .Where(x => !x.Equals(expected, StringComparison.OrdinalIgnoreCase))
        .ToList();
    if (mismatches.Count > 0)
        throw new ArgumentException($"Profile leagueKey {expected} does not match input LeagueKey {string.Join(", ", mismatches)}.");
}

static string Coalesce(params string?[] values)
    => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

static async Task<int> RunImportFlashscoreFixtures(string[] args)
{
    var parsed = ArgsParser.Parse(args);
    LeagueProfile? profile = await LoadOptionalProfileAsync(parsed);

    string league = parsed.String("league", profile?.League ?? string.Empty);
    if (string.IsNullOrWhiteSpace(league))
        throw new ArgumentException("Missing required argument --league, or provide --profile with league set.");

    int seasonId = parsed.Has("season-id")
        ? parsed.RequiredInt("season-id")
        : profile?.FlashscoreSeasonId > 0
            ? profile.FlashscoreSeasonId
            : profile?.CurrentSeasonId > 0
                ? profile.CurrentSeasonId
                : DateTimeOffset.UtcNow.Year;

    int tournamentId = parsed.Has("tournament-id")
        ? parsed.RequiredInt("tournament-id")
        : profile?.FlashscoreTournamentId > 0
            ? profile.FlashscoreTournamentId
            : StablePositiveInt($"flashscore:tournament:{league}");

    string inputRoot = parsed.String("input", parsed.String("output", "data/flashscore"));
    bool debugImport = parsed.Bool("debug-import", false);

    var rounds = new List<int>();
    if (parsed.Has("round") || parsed.Has("rounds") || parsed.Has("from-round") || parsed.Has("round-from") || parsed.Has("to-round"))
        AddRounds(rounds, parsed);

    IConfiguration configuration = BuildConfiguration();
    await using LiveTotalsDbContext dbContext = await DatabaseMigrator.CreateMigratedDbContextAsync(configuration, Console.Out, CancellationToken.None);
    var importer = new FlashscoreDbImporter(dbContext);

    FlashscoreImportResult result = await ImportFlashscoreFolderAsync(importer, inputRoot, league, tournamentId, seasonId, rounds, calendarOnly: true, debugImport, Console.Out, CancellationToken.None);

    PrintImportResult("Fixture import done.", result, includeDetails: false);
    return result.Failures.Count == 0 ? 0 : 1;
}

static async Task<FlashscoreImportResult> ImportFlashscoreFolderAsync(
    FlashscoreDbImporter importer,
    string inputRoot,
    string league,
    int tournamentId,
    int seasonId,
    IReadOnlyCollection<int> requestedRounds,
    bool calendarOnly,
    bool debugImport,
    TextWriter log,
    CancellationToken cancellationToken)
{
    var result = new FlashscoreImportResult();
    string leagueSlug = FileNameSanitizer.Slugify(league);
    string seasonFolder = Path.Combine(inputRoot, leagueSlug, $"season-{seasonId}");

    if (!Directory.Exists(seasonFolder))
        throw new ArgumentException($"Flashscore season folder was not found: {seasonFolder}");

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

    if (calendarOnly && requestedRounds.Count == 0)
        roundFolders = SelectNearestSavedFixtureRoundFolders(roundFolders);

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

        if (calendarOnly)
            continue;

        string eventsFolder = Path.Combine(roundFolder, "events");
        if (!Directory.Exists(eventsFolder))
        {
            result.Warnings.Add($"round {round}: events folder not found: {eventsFolder}");
            continue;
        }

        foreach (string eventFolder in Directory.GetDirectories(eventsFolder).OrderBy(x => x))
            await ImportFlashscoreEventFolderAsync(importer, result, round, eventFolder, debugImport, log, cancellationToken);
    }

    return result;
}

static async Task ImportFlashscoreEventFolderAsync(
    FlashscoreDbImporter importer,
    FlashscoreImportResult result,
    int round,
    string eventFolder,
    bool debugImport,
    TextWriter log,
    CancellationToken cancellationToken)
{
    string eventId = Path.GetFileName(eventFolder);
    if (string.IsNullOrWhiteSpace(eventId))
    {
        result.Warnings.Add($"round {round}: skipped non-event folder: {eventFolder}");
        return;
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

    string oddsPath = Path.Combine(eventFolder, "odds.json");
    if (File.Exists(oddsPath))
    {
        try
        {
            string json = await File.ReadAllTextAsync(oddsPath, cancellationToken);
            if (debugImport)
                await log.WriteLineAsync($"  event {eventId}: odds {oddsPath}");

            await importer.ImportOddsAsync(eventId, json, oddsPath, cancellationToken);
            result.OddsImported++;
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"event {eventId}: odds import failed:{Environment.NewLine}{FormatImportException(ex)}");
        }
    }
}

static void WriteDbValidationReport(TextWriter writer, DbValidationResult result, DbValidationOptions options)
{
    writer.WriteLine();
    writer.WriteLine("Database validation done.");
    writer.WriteLine($"Matches checked: {result.MatchesChecked}");
    writer.WriteLine($"Events checked: {result.EventsChecked}");
    writer.WriteLine($"Match stats checked: {result.MatchStatsChecked}");
    writer.WriteLine($"Odds checked: {result.OddsChecked}");
    writer.WriteLine($"Errors: {result.ErrorCount}");
    writer.WriteLine($"Warnings: {result.WarningCount}");
    writer.WriteLine($"Info: {result.InfoCount}");

    foreach (DbValidationCheckResult check in result.Checks)
    {
        writer.WriteLine();
        writer.WriteLine($"[{check.Severity}] {check.Name}: {check.Message}");
        foreach (string example in check.Examples.Take(options.MaxExamplesPerCheck))
            writer.WriteLine($"  - {example}");

        if (check.Examples.Count > options.MaxExamplesPerCheck)
            writer.WriteLine($"  ... {check.Examples.Count - options.MaxExamplesPerCheck} more");
    }
}

static IConfiguration BuildConfiguration()
{
    return new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();
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

static List<(int Round, string Folder)> SelectNearestSavedFixtureRoundFolders(List<(int Round, string Folder)> roundFolders)
{
    if (roundFolders.Count <= 1)
        return roundFolders;

    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    var candidates = new List<(int Round, string Folder, long EarliestFutureStart, long EarliestKnownStart)>();
    foreach ((int round, string folder) in roundFolders)
    {
        string calendarPath = Path.Combine(folder, "calendar.json");
        long earliestFutureStart = long.MaxValue;
        long earliestKnownStart = long.MaxValue;

        if (File.Exists(calendarPath))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(calendarPath));
                if (document.RootElement.TryGetProperty("events", out JsonElement eventsElement) &&
                    eventsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement eventElement in eventsElement.EnumerateArray())
                    {
                        if (!eventElement.TryGetProperty("startTimestamp", out JsonElement startElement) ||
                            !startElement.TryGetInt64(out long startTimestamp))
                            continue;

                        if (startTimestamp < earliestKnownStart)
                            earliestKnownStart = startTimestamp;
                        if (startTimestamp >= now - 6 * 60 * 60 && startTimestamp < earliestFutureStart)
                            earliestFutureStart = startTimestamp;
                    }
                }
            }
            catch (JsonException)
            {
                // Bad calendar JSON will be reported during the actual import path.
            }
            catch (IOException)
            {
                // File read races are non-fatal here; the actual import will report them.
            }
        }

        candidates.Add((round, folder, earliestFutureStart, earliestKnownStart));
    }

    var selected = candidates
        .Where(x => x.EarliestFutureStart != long.MaxValue)
        .OrderBy(x => x.EarliestFutureStart)
        .ThenBy(x => x.Round)
        .FirstOrDefault();

    if (selected.Folder is null)
        selected = candidates
            .OrderBy(x => x.EarliestKnownStart)
            .ThenBy(x => x.Round)
            .First();

    return [(selected.Round, selected.Folder)];
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

static async Task<LeagueProfile?> LoadOptionalProfileAsync(ParsedArgs parsed)
{
    if (!parsed.Has("profile"))
        return null;

    string profilesFile = parsed.String("profiles-file", "config/league-profiles.json");
    LeagueProfileStore profileStore = await LeagueProfileStore.LoadAsync(profilesFile, CancellationToken.None);
    return profileStore.FindRequired(parsed.RequiredString("profile"));
}

static int StablePositiveInt(string value)
{
    const uint offset = 2166136261;
    const uint prime = 16777619;

    uint hash = offset;
    foreach (char c in value ?? string.Empty)
    {
        hash ^= c;
        hash *= prime;
    }

    return (int)(hash % 2_000_000_000U) + 1;
}

static int? TryParseSeasonYear(string value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;

    var match = System.Text.RegularExpressions.Regex.Match(value, @"\b(19|20|21)\d{2}\b");
    return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int year)
        ? year
        : null;
}

static void AddRounds(ICollection<int> target, ParsedArgs parsed)
{
    if (parsed.Has("rounds"))
        AddRoundList(target, parsed.RequiredString("rounds"), "rounds");

    if (parsed.Has("round"))
    {
        AddRoundList(target, parsed.RequiredString("round"), "round");
    }
    else if (parsed.Has("from-round") || parsed.Has("round-from") || parsed.Has("to-round"))
    {
        int from = parsed.Has("round-from")
            ? parsed.RequiredInt("round-from")
            : parsed.RequiredInt("from-round");
        int to = parsed.RequiredInt("to-round");
        if (to < from)
            throw new ArgumentException("to-round must be greater than or equal to round-from/from-round.");

        for (int round = from; round <= to; round++)
            AddRound(target, round, "round range");
    }

    if (target.Count == 0)
        throw new ArgumentException("Provide either --round, --rounds, or --round-from/--from-round and --to-round.");
}

static void AddOptionalRounds(ICollection<int> target, ParsedArgs parsed)
{
    if (parsed.Has("round") || parsed.Has("rounds") || parsed.Has("from-round") || parsed.Has("round-from") || parsed.Has("to-round"))
        AddRounds(target, parsed);
}

static void AddRoundList(ICollection<int> target, string raw, string argumentName)
{
    foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!int.TryParse(token, out int round))
            throw new ArgumentException($"Argument --{argumentName} contains invalid round '{token}'. Use an integer or comma-separated integers.");

        AddRound(target, round, argumentName);
    }
}

static void AddRound(ICollection<int> target, int round, string argumentName)
{
    if (round <= 0)
        throw new ArgumentException($"Argument --{argumentName} must contain positive round numbers.");

    if (!target.Contains(round))
        target.Add(round);
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
    PrintWarningsAndFailures(result.Warnings, result.Failures);
}

static void PrintFlashscoreDownloadResult(FlashscoreDownloadResult result)
{
    Console.WriteLine();
    Console.WriteLine("Download done.");
    Console.WriteLine($"Rounds: {result.RoundsDownloaded}");
    Console.WriteLine($"Events discovered: {result.EventsDiscovered}");
    Console.WriteLine($"Files written: {result.FilesWritten}");
    Console.WriteLine($"Files skipped: {result.FilesSkipped}");
    Console.WriteLine($"Warnings: {result.Warnings.Count}");
    Console.WriteLine($"Failures: {result.Failures.Count}");
    PrintWarningsAndFailures(result.Warnings, result.Failures);
}

static void PrintImportResult(string title, FlashscoreImportResult result, bool includeDetails)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine($"Rounds imported: {result.RoundsImported}");
    Console.WriteLine($"Calendars imported: {result.CalendarsImported}");
    if (includeDetails)
    {
        Console.WriteLine($"Incidents files imported: {result.IncidentsImported}");
        Console.WriteLine($"Statistics files imported: {result.StatisticsImported}");
        Console.WriteLine($"Odds files imported: {result.OddsImported}");
    }
    Console.WriteLine($"Warnings: {result.Warnings.Count}");
    Console.WriteLine($"Failures: {result.Failures.Count}");
    PrintWarningsAndFailures(result.Warnings, result.Failures);
}

static void PrintWarningsAndFailures(IReadOnlyCollection<string> warnings, IReadOnlyCollection<string> failures)
{
    if (warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Warnings:");
        foreach (string warning in warnings)
            Console.WriteLine($"- {warning}");
    }

    if (failures.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Failures:");
        foreach (string failure in failures)
            Console.WriteLine($"- {failure}");
    }
}

internal sealed class FlashscoreImportResult
{
    public int RoundsImported { get; set; }
    public int CalendarsImported { get; set; }
    public int IncidentsImported { get; set; }
    public int StatisticsImported { get; set; }
    public int OddsImported { get; set; }
    public List<string> Warnings { get; } = [];
    public List<string> Failures { get; } = [];
}
