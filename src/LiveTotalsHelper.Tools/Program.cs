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
        "build-weibull-dataset" => await RunBuildWeibullDataset(commandArgs),
        "fit-weibull" => await RunFitWeibull(commandArgs),
        "backtest-timing-model" => await RunBacktestTimingModel(commandArgs),
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



static async Task<int> RunBuildWeibullDataset(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    var options = new WeibullDatasetOptions
    {
        League = parsed.String("league", string.Empty),
        SeasonId = parsed.Int("season-id", 0),
        OutputPath = parsed.String("output", string.Empty),
        MaxModelMinute = parsed.Int("max-model-minute", 90),
        IncludeUnreliableMatches = parsed.Bool("include-unreliable", false),
        MaxExamples = parsed.Int("max-examples", 20)
    };


    AddSeasonIds(options.SeasonIds, parsed);

    if (parsed.Has("round") || parsed.Has("from-round") || parsed.Has("to-round"))
        AddRounds(options.Rounds, parsed);

    IConfiguration configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();

    await using LiveTotalsDbContext dbContext = CreateDbContext(configuration);
    var builder = new WeibullDatasetBuilder(dbContext, options);
    WeibullDatasetResult result = await builder.BuildAsync(CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("Weibull dataset build done.");
    Console.WriteLine($"Matches checked: {result.MatchesChecked}");
    Console.WriteLine($"Finished matches: {result.FinishedMatches}");
    Console.WriteLine($"Reliable finished matches: {result.ReliableFinishedMatches}");
    Console.WriteLine($"Unreliable finished matches: {result.UnreliableFinishedMatches}");
    Console.WriteLine($"Seasons included: {(result.SeasonsIncluded.Count == 0 ? "none" : string.Join(", ", result.SeasonsIncluded))}");
    Console.WriteLine($"Goal rows written: {result.GoalRowsWritten}");
    Console.WriteLine($"Output: {result.OutputPath}");
    Console.WriteLine($"Warnings: {result.Warnings.Count}");

    if (result.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Warnings:");
        foreach (string warning in result.Warnings.Take(options.MaxExamples + 1))
            Console.WriteLine($"- {warning}");

        if (result.Warnings.Count > options.MaxExamples + 1)
            Console.WriteLine($"... {result.Warnings.Count - options.MaxExamples - 1} more");
    }

    return 0;
}


static async Task<int> RunFitWeibull(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    var options = new WeibullFitOptions
    {
        InputPath = parsed.RequiredString("input"),
        OutputPath = parsed.String("output", string.Empty),
        League = parsed.String("league", string.Empty),
        MaxMinute = parsed.Int("max-minute", 90),
        MinuteColumn = parsed.String("minute-column", "GoalMinuteForModel"),
        GroupByColumn = parsed.String("group-by", string.Empty),
        MinGroupGoals = parsed.Int("min-group-goals", 30),
        MaxIterations = parsed.Int("max-iterations", 100),
        Tolerance = parsed.Double("tolerance", 1e-9),
        BlendWeibullWeight = parsed.Double("blend-weibull-weight", 0.30)
    };

    var fitter = new WeibullModelFitter(options);
    WeibullFitResult result = await fitter.FitAsync(CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("Weibull fit done.");
    Console.WriteLine($"Input: {result.InputPath}");
    Console.WriteLine($"Output: {result.OutputPath}");
    Console.WriteLine($"League: {(string.IsNullOrWhiteSpace(result.League) ? "unknown" : result.League)}");
    Console.WriteLine($"Seasons included: {(result.SeasonIds.Count == 0 ? "unknown" : string.Join(", ", result.SeasonIds))}");
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


static async Task<int> RunBacktestTimingModel(string[] args)
{
    var parsed = ArgsParser.Parse(args);

    var options = new TimingBacktestOptions
    {
        League = parsed.String("league", string.Empty),
        MaxModelMinute = parsed.Int("max-model-minute", 90),
        MinTrainingSnapshots = parsed.Int("min-training-snapshots", 20),
        IncludeUnreliableMatches = parsed.Bool("include-unreliable", false),
        WalkForward = parsed.Bool("walk-forward", false),
        OutputPath = parsed.String("output", string.Empty)
    };

    AddRequiredIntList(options.TrainingSeasonIds, parsed, "training-season-ids");
    AddRequiredIntList(options.BacktestSeasonIds, parsed, "backtest-season-ids");
    AddOptionalIntList(options.SnapshotMinutes, parsed, "minutes", clearExisting: true);

    if (parsed.Has("round") || parsed.Has("from-round") || parsed.Has("to-round"))
        AddRounds(options.Rounds, parsed);

    IConfiguration configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .Build();

    await using LiveTotalsDbContext dbContext = CreateDbContext(configuration);
    var backtester = new TimingModelBacktester(dbContext, options);
    TimingBacktestResult result = await backtester.RunAsync(CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine("Timing model backtest done.");
    Console.WriteLine($"League: {(string.IsNullOrWhiteSpace(options.League) ? "all" : options.League)}");
    Console.WriteLine($"Training seasons: {string.Join(", ", result.TrainingSeasonIds)}");
    Console.WriteLine($"Backtest seasons: {string.Join(", ", result.BacktestSeasonIds)}");
    Console.WriteLine($"Training matches checked: {result.TrainingMatchesChecked}");
    Console.WriteLine($"Training reliable matches: {result.TrainingReliableMatches}");
    Console.WriteLine($"Backtest matches checked: {result.BacktestMatchesChecked}");
    Console.WriteLine($"Backtest reliable matches: {result.BacktestReliableMatches}");
    Console.WriteLine($"Training snapshots: {result.TrainingSnapshots}");
    Console.WriteLine($"Walk-forward: {result.WalkForward}");
    if (result.WalkForward)
        Console.WriteLine($"Walk-forward prior-season snapshots added across rounds: {result.WalkForwardTrainingSnapshotsAdded}");
    Console.WriteLine($"Backtest snapshots: {result.BacktestSnapshots}");
    if (!string.IsNullOrWhiteSpace(result.OutputPath))
        Console.WriteLine($"Prediction CSV: {result.OutputPath}");

    PrintBacktestTable("Overall", result.OverallRows);
    PrintBacktestTable("By minute", result.ByMinuteRows);
    PrintBacktestTable("By score state", result.ByStateRows);
    PrintBacktestTable("By minute and score state", result.ByMinuteAndStateRows);

    if (result.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Warnings:");
        foreach (string warning in result.Warnings)
            Console.WriteLine($"- {warning}");
    }

    return 0;
}

static void PrintBacktestTable(string title, IReadOnlyList<TimingBacktestSummaryRow> rows)
{
    Console.WriteLine();
    Console.WriteLine(title + ":");
    Console.WriteLine("Group                         N   PredRem   ActRem     Bias      MAE     RMSE");
    foreach (TimingBacktestSummaryRow row in rows)
    {
        Console.WriteLine($"{row.Group,-28} {row.Count,4}  {row.AvgPredictedRemainingGoals,8:0.000}  {row.AvgActualRemainingGoals,7:0.000}  {row.Bias,7:0.000}  {row.MeanAbsoluteError,7:0.000}  {row.RootMeanSquaredError,7:0.000}");
    }
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
