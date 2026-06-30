namespace LiveTotalsHelper.Tools;

public static class HelpPrinter
{
    public static void Print()
    {
        Console.WriteLine("LiveTotalsHelper.Tools");
        Console.WriteLine();
        Console.WriteLine("Downloading/importing commands:");
        Console.WriteLine("  download-flashscore                 Download rendered Flashscore calendar, incidents, stats and odds JSON.");
        Console.WriteLine("  download-flashscore-fixtures        Download nearest visible Flashscore fixture round only.");
        Console.WriteLine("  parse-flashscore-fixtures           Alias for download-flashscore-fixtures.");
        Console.WriteLine("  download-sofascore                  Download SofaScore calendar, incidents and team statistics JSON.");
        Console.WriteLine("  import-flashscore                   Import saved Flashscore JSON into PostgreSQL and apply migrations.");
        Console.WriteLine("  import-flashscore-fixtures          Import saved Flashscore fixture calendars only.");
        Console.WriteLine("  build-after-goal-events             Build a strict after-goal event CSV from imported historical matches.");
        Console.WriteLine("  analyze-after-goal-angles           Analyze after-goal CSV into league/team angle reports.");
        Console.WriteLine("  build-after-goal-team-profiles      Build stable team profile reports from after-goal angle reports.");
        Console.WriteLine("  validate-db                         Validate imported PostgreSQL data quality.");
        Console.WriteLine("  db-validate                         Alias for validate-db.");
        Console.WriteLine();
        Console.WriteLine("Profile file:");
        Console.WriteLine("  Default: config/league-profiles.json");
        Console.WriteLine("  Override with --profiles-file <path>.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- download-flashscore --url <results-url> --league superettan --tournament-id 1 --season-id 2026");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- download-flashscore-fixtures --profile superettan --show-browser");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- import-flashscore --league superettan --season-id 2026");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- import-flashscore-fixtures --profile superettan");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- build-after-goal-events --profile superettan --from-season 2023 --to-season 2025");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- analyze-after-goal-angles --input C:\\football_data\\models\\superettan\\after-goal-events.csv --output-dir C:\\football_data\\models\\superettan\\after-goal-angles");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- build-after-goal-team-profiles --angles-dir C:\\football_data\\models\\superettan\\after-goal-angles --output-dir C:\\football_data\\models\\superettan\\after-goal-profiles");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- validate-db --league \"Superettan\" --season-id 2026");
        Console.WriteLine();
        Console.WriteLine("Common arguments:");
        Console.WriteLine("  --profile                         Profile key/name from config/league-profiles.json.");
        Console.WriteLine("  --url                             Flashscore fixtures/results URL override for download commands.");
        Console.WriteLine("  --season-id                       Season id override; profile currentSeasonId is fallback for fixtures.");
        Console.WriteLine("  --season / --from-season / --to-season");
        Console.WriteLine("                                    Season filters for build-after-goal-events.");
        Console.WriteLine("  --min-minute / --max-minute       Optional elapsed-minute filters for build-after-goal-events.");
        Console.WriteLine("  --input / --output-dir            Input CSV and report directory for analyze-after-goal-angles.");
        Console.WriteLine("  --train-from-season / --train-to-season / --test-season");
        Console.WriteLine("                                    Optional train/test split for analyze-after-goal-angles.");
        Console.WriteLine("  --angles-dir                      Input report directory for build-after-goal-team-profiles.");
        Console.WriteLine("  --tournament-id                   Tournament id override; deterministic profile fallback is used for fixtures.");
        Console.WriteLine("  --round / --rounds                Single round or comma-separated rounds.");
        Console.WriteLine("  --from-round / --to-round         Inclusive round range.");
        Console.WriteLine("  --output                          Download output root, default data/flashscore or data/sofascore.");
        Console.WriteLine("                                    For build-after-goal-events, default C:\\football_data\\models\\{league}\\after-goal-events.csv.");
        Console.WriteLine("  --input                           Import input root, default data/flashscore.");
        Console.WriteLine("  --skip-playoffs true              Skip Flashscore Play Offs/Relegation sections. Default: true.");
        Console.WriteLine("  --include-playoffs true           Include Flashscore Play Offs/Relegation sections.");
        Console.WriteLine("  --render-wait-ms                  Initial Flashscore page wait. Default: 3000.");
        Console.WriteLine("  --detail-wait-ms                  Flashscore detail page wait. Default: 1000.");
    }

    public static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine();
        Print();
        return 2;
    }
}
