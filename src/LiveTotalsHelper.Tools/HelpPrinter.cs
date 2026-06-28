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
        Console.WriteLine("  fit-state-weibull-curves            Fit state/time Weibull curves from exposure CSV.");
        Console.WriteLine("  debug-state-weibull-clock           Export fitted state Weibull curve rows for one live score/minute.");
        Console.WriteLine("  fit-next-goal-side-model            Fit next-goal scorer-side probabilities with fallback hierarchy.");
        Console.WriteLine("  debug-next-goal-side                Resolve P(home/away next goal) for one live score/minute.");
        Console.WriteLine("  simulate-live-total                 Run single-fixture Monte Carlo live total simulation.");
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
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- build-state-weibull-exposures --profile npl-victoria");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-state-weibull-curves --profile npl-victoria");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- debug-state-weibull-clock --profile npl-victoria --score 2-0 --minute 49 --until 96 --out outputs/debug/npl-victoria-2-0-49-clock.csv");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-next-goal-side-model --profile npl-victoria");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- debug-next-goal-side --profile npl-victoria --score 2-0 --minute 49");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- simulate-live-total --profile npl-victoria --score 2-0 --minute 49 --line 3.5 --under-odds 2.20");
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
        Console.WriteLine("  --seasons                         Optional override for profile calibrationSeasonIds.");
        Console.WriteLine("  --time-buckets                    Optional override for profile stateWeibullTimeBuckets.");
        Console.WriteLine("  --in / --input                    Optional override for profile input CSV path.");
        Console.WriteLine("  --summary                         Optional override for profile summary CSV path.");
        Console.WriteLine("  --curves                          Optional override for profile fitted state Weibull curves JSON path.");
        Console.WriteLine("  --model                           Optional override for profile fitted next-goal-side JSON path.");
        Console.WriteLine("  --side-model                      Optional override for profile fitted next-goal-side JSON path.");
        Console.WriteLine("  --until                           End minute for debug-state-weibull-clock output. Default: last fitted bucket end.");
        Console.WriteLine("  --step                            Minute step for debug/simulation commands. Debug default: 1; MC default from profile.");
        Console.WriteLine("  --sims                            Monte Carlo simulation count. Default from profile/global config.");
        Console.WriteLine("  --seed                            Monte Carlo random seed. Default from profile/global config.");
        Console.WriteLine("  --line                            Live total line for simulate-live-total.");
        Console.WriteLine("  --over-odds / --under-odds        Optional book odds for edge calculation.");
        Console.WriteLine("  --paths-out                       Optional CSV trace of early simulated goal paths.");
        Console.WriteLine("  --trace-paths                     Number of simulated paths to trace when --paths-out is provided. Default: 200.");
        Console.WriteLine("  --min-mu-full-exposures           Optional override for profile stateWeibullCurveFit.minMuFullBucketExposures.");
        Console.WriteLine("  --min-mu-goals                    Optional override for profile stateWeibullCurveFit.minMuGoals.");
        Console.WriteLine("  --min-k-full-exposures            Optional override for profile stateWeibullCurveFit.minKFullBucketExposures.");
        Console.WriteLine("  --min-k-goals                     Optional override for profile stateWeibullCurveFit.minKGoals.");
        Console.WriteLine("  --min-exact-goals                 Optional override for profile nextGoalSideFit.minExactGoals.");
        Console.WriteLine("  --prior-weight-goals              Optional override for profile nextGoalSideFit.priorWeightGoals.");
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
