using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.SofaScore;
using LiveTotalsHelper.Infrastructure.SofaScore;
using LiveTotalsHelper.Tools;
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
        "download-sofascore" => await RunDownloadSofaScore(commandArgs),
        "import-sofascore" => await RunImportSofaScore(commandArgs),
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
