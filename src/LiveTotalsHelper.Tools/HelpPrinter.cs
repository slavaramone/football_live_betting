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
        Console.WriteLine("  validate-db                         Validate imported PostgreSQL data quality.");
        Console.WriteLine("  db-validate                         Alias for validate-db.");
        Console.WriteLine("  debug-effective-end                 Estimate effective match end/remaining time for a live state.");
        Console.WriteLine("  build-state-weibull-exposures       Build score/time exposure CSV for fitting state Weibull curves.");
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
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- validate-db --league \"Superettan\" --season-id 2026");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- build-state-weibull-exposures --profile npl-victoria --seasons 2023,2024,2025 --out outputs/calibration/npl-victoria-state-weibull-exposures.csv");
        Console.WriteLine();
        Console.WriteLine("Common arguments:");
        Console.WriteLine("  --profile                         Profile key/name from config/league-profiles.json.");
        Console.WriteLine("  --url                             Flashscore fixtures/results URL override for download commands.");
        Console.WriteLine("  --season-id                       Season id override; profile currentSeasonId is fallback for fixtures.");
        Console.WriteLine("  --tournament-id                   Tournament id override; deterministic profile fallback is used for fixtures.");
        Console.WriteLine("  --round / --rounds                Single round or comma-separated rounds.");
        Console.WriteLine("  --from-round / --to-round         Inclusive round range.");
        Console.WriteLine("  --output                          Download output root, default data/flashscore or data/sofascore.");
        Console.WriteLine("  --input                           Import input root, default data/flashscore.");
        Console.WriteLine("  --skip-playoffs true              Skip Flashscore Play Offs/Relegation sections. Default: true.");
        Console.WriteLine("  --include-playoffs true           Include Flashscore Play Offs/Relegation sections.");
        Console.WriteLine("  --render-wait-ms                  Initial Flashscore page wait. Default: 3000.");
        Console.WriteLine("  --detail-wait-ms                  Flashscore detail page wait. Default: 1000.");
        Console.WriteLine("  --minute                          Live minute for debug-effective-end.");
        Console.WriteLine("  --score                           Current score as home-away, for example 2-0.");
        Console.WriteLine("  --hr / --ar                       Home/away red-card counts for debug-effective-end.");
        Console.WriteLine("  --seasons                         Comma-separated season years/ids for calibration commands.");
        Console.WriteLine("  --time-buckets                    Comma-separated buckets, for example 45-55,55-65,65-75.");
        Console.WriteLine("  --out / --output                  Optional output path for debug/calibration commands.");
    }

    public static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine();
        Print();
        return 2;
    }
}
