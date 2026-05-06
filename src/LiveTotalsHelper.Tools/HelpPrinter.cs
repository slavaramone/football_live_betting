namespace LiveTotalsHelper.Tools;

public static class HelpPrinter
{
    public static void Print()
    {
        Console.WriteLine("LiveTotalsHelper.Tools");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  download-sofascore   Download SofaScore calendar, incidents and team statistics JSON only.");
        Console.WriteLine("  import-sofascore     Import saved SofaScore JSON into PostgreSQL and apply pending migrations.");
        Console.WriteLine("  validate-db          Validate imported PostgreSQL data quality for modelling.");
        Console.WriteLine("  build-weibull-dataset Export reliable goal-minute rows to CSV for Weibull fitting.");
        Console.WriteLine("  fit-weibull           Fit a league-wide Weibull timing model from a goal-minute CSV.");
        Console.WriteLine();
        PrintDownloadSofaScore();
        Console.WriteLine();
        PrintImportSofaScore();
        Console.WriteLine();
        PrintValidateDb();
        Console.WriteLine();
        PrintBuildWeibullDataset();
        Console.WriteLine();
        PrintFitWeibull();
    }

    public static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine();
        Print();
        return 2;
    }

    public static void PrintDownloadSofaScore()
    {
        Console.WriteLine("Download usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- download-sofascore \\");
        Console.WriteLine("    --league \"NPL NSW\" --tournament-id 1274 --season-id 88562 --round 2");
        Console.WriteLine();
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- download-sofascore \\");
        Console.WriteLine("    --league \"NPL NSW\" --tournament-id 1274 --season-id 88562 --from-round 1 --to-round 30 \\");
        Console.WriteLine("    --output data/sofascore --delay-ms 600 --overwrite false");
        Console.WriteLine();
        Console.WriteLine("Download arguments:");
        Console.WriteLine("  --league             League name used in folder structure, for example \"NPL NSW\".");
        Console.WriteLine("  --tournament-id      SofaScore unique tournament id.");
        Console.WriteLine("  --season-id          SofaScore season id.");
        Console.WriteLine("  --round              Single round to download.");
        Console.WriteLine("  --from-round         First round when downloading a range.");
        Console.WriteLine("  --to-round           Last round when downloading a range.");
        Console.WriteLine("  --calendar-mode      SofaScore calendar path segment: round or last. Default: round");
        Console.WriteLine("  --output             Output root. Default: data/sofascore");
        Console.WriteLine("  --delay-ms           Delay between event endpoint calls. Default: 450");
        Console.WriteLine("  --overwrite          true/false. Default: false");
        Console.WriteLine("  --incidents          true/false. Default: true");
        Console.WriteLine("  --statistics         true/false. Default: true");
        Console.WriteLine("  --skip-details-for-not-started true/false. Default: true");
        Console.WriteLine("  --strict-event-details true/false. Default: false. If true, incidents/statistics errors fail the run.");
        Console.WriteLine("  --headless           true/false. Default: true");
        Console.WriteLine("  --show-browser       Debug shortcut. Runs Chromium visible.");
        Console.WriteLine("  --warmup-delay-ms    Delay after opening sofascore.com before API calls. Default: 1000");
    }

    public static void PrintImportSofaScore()
    {
        Console.WriteLine("Import usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- import-sofascore \\");
        Console.WriteLine("    --league \"NPL NSW\" --tournament-id 1274 --season-id 88562 --round 2 --input data/sofascore");
        Console.WriteLine();
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- import-sofascore \\");
        Console.WriteLine("    --league \"NPL NSW\" --tournament-id 1274 --season-id 88562 --from-round 1 --to-round 30 --input data/sofascore");
        Console.WriteLine();
        Console.WriteLine("Import arguments:");
        Console.WriteLine("  --league             League name used in folder structure.");
        Console.WriteLine("  --tournament-id      SofaScore unique tournament id fallback. Optional, default: 0.");
        Console.WriteLine("  --season-id          SofaScore season id.");
        Console.WriteLine("  --round              Single round to import.");
        Console.WriteLine("  --from-round         First round when importing a range.");
        Console.WriteLine("  --to-round           Last round when importing a range.");
        Console.WriteLine("  --input              Input root where JSON was downloaded. Default: data/sofascore");
        Console.WriteLine("  --debug-import       true/false. Prints every file before importing and returns detailed DB errors. Default: false");
        Console.WriteLine();
        Console.WriteLine("Database:");
        Console.WriteLine("  Connection string: src/LiveTotalsHelper.Tools/appsettings.json");
        Console.WriteLine("  Pending migrations are applied automatically only when import-sofascore starts.");
        Console.WriteLine();
        Console.WriteLine("Before first run, install Playwright browser binaries for download-sofascore:");
        Console.WriteLine("  dotnet build src/LiveTotalsHelper.Tools/LiveTotalsHelper.Tools.csproj");
        Console.WriteLine("  powershell -ExecutionPolicy Bypass -File src/LiveTotalsHelper.Tools/bin/Debug/net8.0/playwright.ps1 install chromium");
    }


    public static void PrintBuildWeibullDataset()
    {
        Console.WriteLine("Build Weibull dataset usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- build-weibull-dataset \\");
        Console.WriteLine("    --league \"NPL NSW\" --season-ids 57783,88562 --from-round 1 --to-round 30 \\");
        Console.WriteLine("    --output data/weibull/npl-nsw-multi-season-goals.csv");
        Console.WriteLine();
        Console.WriteLine("Build Weibull dataset arguments:");
        Console.WriteLine("  --league             Optional league name or league slug filter.");
        Console.WriteLine("  --season-id          Optional single SofaScore season id filter.");
        Console.WriteLine("  --season-ids         Optional comma-separated season ids, e.g. 57783,88562. Can be used instead of --season-id.");
        Console.WriteLine("  --round              Optional single round filter.");
        Console.WriteLine("  --from-round         Optional first round filter.");
        Console.WriteLine("  --to-round           Optional last round filter.");
        Console.WriteLine("  --output             Output CSV path. Default: data/weibull/{league}-{season selection}-goals.csv");
        Console.WriteLine("  --max-model-minute   Cap GoalMinuteForModel at this value. Default: 90. Use 0 for no cap.");
        Console.WriteLine("  --include-unreliable true/false. Include matches where final score does not match goal events. Default: false");
        Console.WriteLine("  --max-examples       Maximum warning examples printed. Default: 20");
        Console.WriteLine();
        Console.WriteLine("Output is one row per reliable goal event, designed for league-wide and opponent-wide Weibull fitting.");
    }


    public static void PrintFitWeibull()
    {
        Console.WriteLine("Fit Weibull usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-weibull \\");
        Console.WriteLine("    --input data/weibull/npl-nsw-multi-season-goals.csv \\");
        Console.WriteLine("    --league \"NPL NSW\" \\");
        Console.WriteLine("    --output data/models/weibull/npl-nsw.json");
        Console.WriteLine();
        Console.WriteLine("Fit Weibull arguments:");
        Console.WriteLine("  --input              Required input CSV produced by build-weibull-dataset.");
        Console.WriteLine("  --output             Output JSON model path. Default: data/models/weibull/{league}-{season selection}.json");
        Console.WriteLine("  --league             Optional league name stored in output model metadata.");
        Console.WriteLine("  --max-minute         Normalize CDF/remaining share to this match minute. Default: 90");
        Console.WriteLine("  --minute-column      CSV column to fit. Default: GoalMinuteForModel");
        Console.WriteLine("  --max-iterations     Maximum MLE iterations. Default: 100");
        Console.WriteLine("  --tolerance          MLE convergence tolerance. Default: 1e-9");
        Console.WriteLine("  --blend-weibull-weight Weight for blended model. Default: 0.30, so blend = 30% Weibull + 70% empirical.");
        Console.WriteLine();
        Console.WriteLine("Output JSON stores three timing models: pure Weibull, empirical bucket curve, and blended Weibull+empirical model.");
    }

    public static void PrintValidateDb()
    {
        Console.WriteLine("Validate DB usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- validate-db");
        Console.WriteLine();
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- validate-db \\");
        Console.WriteLine("    --league \"NPL NSW\" --season-id 88562 --from-round 1 --to-round 30");
        Console.WriteLine();
        Console.WriteLine("Validate DB arguments:");
        Console.WriteLine("  --league             Optional league name filter.");
        Console.WriteLine("  --season-id          Optional SofaScore season id filter.");
        Console.WriteLine("  --round              Optional single round filter.");
        Console.WriteLine("  --from-round         Optional first round filter.");
        Console.WriteLine("  --to-round           Optional last round filter.");
        Console.WriteLine("  --fail-on-warnings   true/false. Return exit code 1 when warnings exist. Default: false");
        Console.WriteLine("  --max-examples       Maximum examples printed per check. Default: 20");
        Console.WriteLine();
        Console.WriteLine("Validation checks include score vs goal events, goal timing ranges, score progression, future fixtures with details, missing model stats, duplicated incidents and red-card stat consistency.");
    }

}
