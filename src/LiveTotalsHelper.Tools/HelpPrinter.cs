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
        Console.WriteLine("  fit-competing-hazard-curves         Fit v3 curves: total state-Weibull hazard split by directional scorer share plus after-goal, goal-draw and market-baseline settings.");
        Console.WriteLine("  debug-next-goal-side                Resolve P(home/away next goal) for one live score/minute.");
        Console.WriteLine("  simulate-live-total                 Run v2 single-fixture Monte Carlo live total simulation.");
        Console.WriteLine("  simulate-live-total-v3              Run v3 competing-hazard single-fixture MC simulation with after-goal, goal-draw and pregame market-baseline factors.");
        Console.WriteLine("  evaluate-monte-carlo-model         Build historical live states in memory and write MC validation summary JSON; use --model-version v3 for competing hazards.");
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
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-competing-hazard-curves --profile npl-victoria");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- debug-next-goal-side --profile npl-victoria --score 2-0 --minute 49");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- simulate-live-total --profile npl-victoria --score 2-0 --minute 49 --line 3.5 --under-odds 2.20");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- simulate-live-total-v3 --profile npl-victoria --score 2-0 --minute 49 --last-goal-minute 48 --last-goal-side home --line 3.5 --under-odds 2.20 --pregame-total-line 3.5 --pregame-over-odds 1.95 --pregame-under-odds 1.85");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- evaluate-monte-carlo-model --profile npl-victoria --seasons 2026 --sims 5000");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- evaluate-monte-carlo-model --profile npl-victoria --model-version v3 --seasons 2026 --sims 5000");
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
        Console.WriteLine("  --curves                          Optional override for profile fitted state Weibull curves JSON path, or v3 competing curves in simulate-live-total-v3.");
        Console.WriteLine("  --competing-curves                Optional override for profile fitted v3 competing-hazard curves JSON path.");
        Console.WriteLine("  --model                           Optional override for profile fitted next-goal-side JSON path.");
        Console.WriteLine("  --out / --output                  Optional output path for fitted/debug/evaluation commands.");
        Console.WriteLine("  --side-model                      Optional override for profile fitted next-goal-side JSON path.");
        Console.WriteLine("  --until                           End minute for debug-state-weibull-clock output. Default: last fitted bucket end.");
        Console.WriteLine("  --step                            Minute step for debug/simulation commands. Debug default: 1; MC default from profile.");
        Console.WriteLine("  --sims                            Monte Carlo simulation count. Default from profile/global config.");
        Console.WriteLine("  --seed                            Monte Carlo random seed. Default from profile/global config.");
        Console.WriteLine("  --line                            Live total line for simulate-live-total.");
        Console.WriteLine("  --last-goal-minute                Optional last scoring goal minute for after-goal hazard factors.");
        Console.WriteLine("  --last-goal-side                  Optional last scorer side: home/away.");
        Console.WriteLine("  --model-version                   Evaluation/simulation model version. Use v3 for competing-hazard mode.");
        Console.WriteLine("  --disable-after-goal-factors       Fit v3 competing curves without after-goal factors.");
        Console.WriteLine("  --disable-goal-draw-suppression   Fit v3 competing curves without draw_1_1_plus suppression factors.");
        Console.WriteLine("  --goal-draw-score-bucket          Score bucket to suppress. Default: draw_1_1_plus.");
        Console.WriteLine("  --pregame-total-line             Pregame total line for v3 market-baseline multiplier.");
        Console.WriteLine("  --pregame-over-odds / --pregame-under-odds  Pregame O/U odds used with --pregame-total-line.");
        Console.WriteLine("  --pregame-total                  Direct pregame expected total override for v3 market baseline.");
        Console.WriteLine("  --disable-pregame-market-baseline Disable automatic DB pregame-odds baseline in v3 evaluation.");
        Console.WriteLine("  --pregame-odds-bookmaker         Optional bookmaker filter for v3 evaluation pregame total odds.");
        Console.WriteLine("  --lines                           Comma-separated lines for evaluate-monte-carlo-model. Default: profile targetLines.");
        Console.WriteLine("  --minutes                         Comma-separated historical state minutes for evaluation. Default: 45,50,55,60,65,70,75,80,85.");
        Console.WriteLine("  --assumed-odds                    Assumed flat odds for evaluation betting metrics. Default: 1.85.");
        Console.WriteLine("  --assumed-over-odds / --assumed-under-odds  Side-specific assumed odds for evaluation metrics.");
        Console.WriteLine("  --min-edge                        Minimum edge for evaluation betting metrics. Default: profile edgeThreshold.");
        Console.WriteLine("  --max-states                      Optional cap on evaluated rows for quick tests.");
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
