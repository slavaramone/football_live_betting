using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Core.MonteCarlo;
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
        "validate-db" => await RunValidateDb(commandArgs),
        "db-validate" => await RunValidateDb(commandArgs),
        "debug-effective-end" => await RunDebugEffectiveEnd(commandArgs),
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


static async Task<int> RunDebugEffectiveEnd(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    LeagueProfile? profile = await LoadProfileByKeyOrLeagueAsync(parsed);
    string leagueKey = profile?.Key ?? parsed.String("league", parsed.String("profile", string.Empty));

    (int homeGoals, int awayGoals) = ParseScore(parsed.String("score", "0-0"));
    var request = new LiveMonteCarloRequest
    {
        LeagueKey = leagueKey,
        CurrentMinute = parsed.RequiredDouble("minute"),
        HomeGoals = homeGoals,
        AwayGoals = awayGoals,
        HomeRedCards = parsed.Int("hr", parsed.Int("home-red-cards", 0)),
        AwayRedCards = parsed.Int("ar", parsed.Int("away-red-cards", 0)),
        LastGoalMinute = parsed.Has("last-goal-minute")
            ? parsed.Double("last-goal-minute", 0.0)
            : null
    };

    MonteCarloConfig config = profile?.MonteCarlo ?? new MonteCarloConfig();

    var estimator = new EffectiveEndMinuteEstimator();
    EffectiveEndMinuteEstimate estimate = estimator.Estimate(request, config);

    Console.WriteLine("Effective end debug");
    Console.WriteLine($"League: {(string.IsNullOrWhiteSpace(leagueKey) ? "<default>" : leagueKey)}");
    Console.WriteLine($"Minute: {request.CurrentMinute.ToString("0.##", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Score: {request.HomeGoals}-{request.AwayGoals}");
    Console.WriteLine($"Red cards: {request.HomeRedCards}-{request.AwayRedCards}");
    Console.WriteLine($"Period: {estimate.Period}");
    Console.WriteLine($"Estimated effective end: {estimate.EffectiveEndMinute.ToString("0.##", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Remaining effective minutes: {estimate.RemainingEffectiveMinutes.ToString("0.##", CultureInfo.InvariantCulture)}");
    Console.WriteLine($"Reason: {estimate.Reason}");

    string outputPath = parsed.String("out", parsed.String("output", string.Empty));
    if (!string.IsNullOrWhiteSpace(outputPath))
    {
        string fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var payload = new
        {
            league = leagueKey,
            minute = request.CurrentMinute,
            score = $"{request.HomeGoals}-{request.AwayGoals}",
            homeRedCards = request.HomeRedCards,
            awayRedCards = request.AwayRedCards,
            lastGoalMinute = request.LastGoalMinute,
            estimate
        };

        await File.WriteAllTextAsync(
            fullPath,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8,
            CancellationToken.None);

        Console.WriteLine($"Output written: {fullPath}");
    }

    return 0;
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


static async Task<LeagueProfile?> LoadProfileByKeyOrLeagueAsync(ParsedArgs parsed)
{
    string profileKey = parsed.String("profile", string.Empty);
    string leagueKey = parsed.String("league", string.Empty);
    string lookup = !string.IsNullOrWhiteSpace(profileKey) ? profileKey : leagueKey;
    if (string.IsNullOrWhiteSpace(lookup))
        return null;

    string profilesFile = parsed.String("profiles-file", "config/league-profiles.json");
    LeagueProfileStore profileStore = await LeagueProfileStore.LoadAsync(profilesFile, CancellationToken.None);
    return profileStore.FindRequired(lookup);
}

static (int HomeGoals, int AwayGoals) ParseScore(string raw)
{
    if (string.IsNullOrWhiteSpace(raw))
        return (0, 0);

    string[] parts = raw.Trim().Split(new[] { '-', ':' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length != 2 ||
        !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int homeGoals) ||
        !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int awayGoals) ||
        homeGoals < 0 ||
        awayGoals < 0)
        throw new ArgumentException("Argument --score must use non-negative '<home>-<away>' format, for example --score 2-0.");

    return (homeGoals, awayGoals);
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
