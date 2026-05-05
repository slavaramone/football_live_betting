namespace LiveTotalsHelper.Tools;

public static class HelpPrinter
{
    public static void Print()
    {
        Console.WriteLine("LiveTotalsHelper.Tools");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  download-sofascore   Download SofaScore calendar, incidents and team statistics JSON, then import model data into PostgreSQL.");
        Console.WriteLine();
        PrintDownloadSofaScore();
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
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- download-sofascore \\");
        Console.WriteLine("    --league \"NPL NSW\" --tournament-id 1274 --season-id 57783 --round 25");
        Console.WriteLine();
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- download-sofascore \\");
        Console.WriteLine("    --league \"NPL NSW\" --tournament-id 1274 --season-id 57783 --from-round 1 --to-round 30 \\");
        Console.WriteLine("    --output data/sofascore --delay-ms 600 --overwrite false");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
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
        Console.WriteLine();
        Console.WriteLine("Database:");
        Console.WriteLine("  Connection string: src/LiveTotalsHelper.Tools/appsettings.json");
        Console.WriteLine("  Pending migrations are applied automatically when the command starts.");
        Console.WriteLine();
        Console.WriteLine("Before first run, install Playwright browser binaries:");
        Console.WriteLine("  dotnet build src/LiveTotalsHelper.Tools/LiveTotalsHelper.Tools.csproj");
        Console.WriteLine("  powershell -ExecutionPolicy Bypass -File src/LiveTotalsHelper.Tools/bin/Debug/net8.0/playwright.ps1 install chromium");
    }
}
