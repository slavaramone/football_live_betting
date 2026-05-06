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
        Console.WriteLine();
        PrintDownloadSofaScore();
        Console.WriteLine();
        PrintImportSofaScore();
        Console.WriteLine();
        PrintValidateDb();
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
