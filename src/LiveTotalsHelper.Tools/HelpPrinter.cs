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
        Console.WriteLine("  backtest-timing-model Backtest score-state timing model using explicit training and test seasons.");
        Console.WriteLine("  price-live-total     Price live Over totals from starting odds, score state and fitted timing model.");
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
        Console.WriteLine();
        PrintBacktestTimingModel();
        Console.WriteLine();
        PrintPriceLiveTotal();
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
        Console.WriteLine("Output is one row per reliable goal event. It includes score-before and score-state fields for state-aware timing models.");
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
        Console.WriteLine("  --group-by           Optional CSV column for state-aware fits, e.g. DetailedScoreStateBefore, ScoreStateBefore, GoalTeamStateBefore, LeadingTeamBefore.");
        Console.WriteLine("  --min-group-goals    Minimum goals required to fit a group model. Default: 30");
        Console.WriteLine("  --max-iterations     Maximum MLE iterations. Default: 100");
        Console.WriteLine("  --tolerance          MLE convergence tolerance. Default: 1e-9");
        Console.WriteLine("  --blend-weibull-weight Weight for blended model. Default: 0.30, so blend = 30% Weibull + 70% empirical.");
        Console.WriteLine();
        Console.WriteLine("Output JSON stores three timing models: pure Weibull, empirical bucket curve, and blended Weibull+empirical model.");
        Console.WriteLine("Use --group-by DetailedScoreStateBefore after rebuilding the dataset to produce NilNil/LevelWithGoals/OneGoalMargin/TwoGoalMargin/ThreePlusGoalMargin models. Use ScoreStateBefore only for legacy broad-state models.");
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


    public static void PrintBacktestTimingModel()
    {
        Console.WriteLine("Backtest timing model usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- backtest-timing-model \\");
        Console.WriteLine("    --league \"NPL New South Wales\" \\");
        Console.WriteLine("    --training-season-ids 48254,57783 \\");
        Console.WriteLine("    --backtest-season-ids 71036 \\");
        Console.WriteLine("    --minutes 15,30,45,60,75 \\");
        Console.WriteLine("    --walk-forward true \\");
        Console.WriteLine("    --use-current-season-volume-calibration true \\");
        Console.WriteLine("    --prior-strength-matches 100 \\");
        Console.WriteLine("    --output data/backtests/npl-nsw-2023-2024-vs-2025.csv");
        Console.WriteLine();
        Console.WriteLine("Backtest timing model arguments:");
        Console.WriteLine("  --league                 Optional league name or slug filter.");
        Console.WriteLine("  --training-season-ids    Required comma-separated base training season ids, e.g. 48254,57783.");
        Console.WriteLine("  --backtest-season-ids    Required comma-separated backtest season ids, e.g. 71036.");
        Console.WriteLine("  --minutes                Snapshot minutes. Default: 15,30,45,60,75.");
        Console.WriteLine("  --walk-forward           true/false. If true, each test round trains on base seasons plus earlier rounds from the tested season. Default: false.");
        Console.WriteLine("  --use-current-season-volume-calibration true/false. Requires walk-forward. Applies shrunk current-season goals-per-match factor from prior rounds. Default: false.");
        Console.WriteLine("  --use-score-state-volume-calibration true/false. Requires walk-forward. Applies score-state-specific current-season volume factors where enough data exists. Default: false.");
        Console.WriteLine("  --test-empirical-weights Comma-separated empirical weights for blend testing. 1.0 = pure empirical, 0.0 = pure Weibull. Default: 1.0.");
        Console.WriteLine("  --prior-strength-matches Prior strength for current-season volume shrinkage. Default: 100.");
        Console.WriteLine("  --round                  Optional single test round filter. In walk-forward mode, earlier rounds from that season can still be used as prior data.");
        Console.WriteLine("  --from-round             Optional first test round filter.");
        Console.WriteLine("  --to-round               Optional last test round filter.");
        Console.WriteLine("  --min-training-snapshots Minimum exact minute+state training snapshots before fallback. Default: 20.");
        Console.WriteLine("  --max-model-minute       Cap goal minutes at this value. Default: 90.");
        Console.WriteLine("  --include-unreliable     true/false. Include matches where final score does not match goal events. Default: false.");
        Console.WriteLine("  --output                 Optional CSV path for per-snapshot predictions.");
        Console.WriteLine();
        Console.WriteLine("This is not a betting/odds backtest. It tests whether score-state timing estimates trained on selected seasons predict actual remaining goals in held-out seasons.");
    }


    public static void PrintPriceLiveTotal()
    {
        Console.WriteLine("Price live total usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- price-live-total \\");
        Console.WriteLine("    --profile npl-nsw \\");
        Console.WriteLine("    --starting-line 2.5 --starting-over 1.85 --starting-under 1.95 \\");
        Console.WriteLine("    --minute 60 --home-goals 1 --away-goals 0 --before-round 10 \\");
        Console.WriteLine("    --live-over-odds \"2.5=2.30,3.5=4.20\" --live-under-odds \"2.5=1.65,3.5=1.98\"");
        Console.WriteLine();
        Console.WriteLine("Without profile:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- price-live-total \\");
        Console.WriteLine("    --model data/models/weibull/npl-nsw-score-state.json \\");
        Console.WriteLine("    --starting-line 2.5 --starting-over 1.85 --starting-under 1.95 \\");
        Console.WriteLine("    --minute 60 --home-goals 1 --away-goals 0 \\");
        Console.WriteLine("    --use-current-season-volume true --league \"NPL New South Wales\" \\");
        Console.WriteLine("    --base-season-ids 48254,57783,71036 --current-season-id 88562 --before-round 10 \\");
        Console.WriteLine("    --prior-strength-matches 100 \\");
        Console.WriteLine("    --live-over-odds \"1.5=1.25,2.0=1.80,2.5=2.30,3.0=3.60,3.5=4.20\"");
        Console.WriteLine("    --live-under-odds \"1.5=3.80,2.0=2.05,2.5=1.65,3.0=1.40,3.5=1.98\"");
        Console.WriteLine();
        Console.WriteLine("Price live total arguments:");
        Console.WriteLine("  --profile            Optional league profile key/name from league-profiles.json, e.g. npl-nsw or wa-state-league-1.");
        Console.WriteLine("  --profiles-file      Optional profiles JSON path. Default: league-profiles.json copied beside the tool executable.");
        Console.WriteLine("  --model              Fitted timing model JSON. Required unless provided by profile.");
        Console.WriteLine("  --starting-line      Required starting/pre-match total line.");
        Console.WriteLine("  --starting-over      Required starting/pre-match over odds.");
        Console.WriteLine("  --starting-under     Required starting/pre-match under odds.");
        Console.WriteLine("  --minute             Required current match minute.");
        Console.WriteLine("  --home-goals         Required current home goals.");
        Console.WriteLine("  --away-goals         Required current away goals.");
        Console.WriteLine("  --empirical-weight   Optional override. Default comes from profile, otherwise 0.80.");
        Console.WriteLine("  --target-lines       Optional comma-separated target lines. Default comes from profile, otherwise 1.5,2.0,2.5,3.0.");
        Console.WriteLine("  --live-over-X        Optional bookmaker live Over X odds. Examples: --live-over-3.5 4.20, --live-over-4.0 6.50.");
        Console.WriteLine("  --live-under-X       Optional bookmaker live Under X odds. Examples: --live-under-3.5 1.98, --live-under-4.0 1.35.");
        Console.WriteLine("                       Two-digit compact aliases also work: --live-over-35 / --live-under-35 for 3.5. Use decimal form for quarter lines like --live-over-4.25.");
        Console.WriteLine("  --live-over-odds     Optional generic format: \"1.5=1.40,2.0=1.85,2.5=2.45,3.5=4.20\".");
        Console.WriteLine("  --live-under-odds    Optional generic format: \"1.5=3.60,2.0=2.10,2.5=1.65,3.5=1.90\".");
        Console.WriteLine("  --edge-threshold     Optional override. Default comes from profile, otherwise 0.10 = 10%.");
        Console.WriteLine("  --volume-factor      Optional manual multiplier for remaining xG. Overrides automatic current-season volume.");
        Console.WriteLine("  --use-current-season-volume true/false. Default comes from profile, otherwise false.");
        Console.WriteLine("  --league             Required for automatic volume if not provided by profile.");
        Console.WriteLine("  --base-season-ids    Required for automatic volume if not provided by profile.");
        Console.WriteLine("  --current-season-id  Required for automatic volume if not provided by profile.");
        Console.WriteLine("  --before-round       Required for automatic volume. Uses current-season matches with RoundNumber < this.");
        Console.WriteLine("  --prior-strength-matches Optional override. Default comes from profile, otherwise 100.");
        Console.WriteLine("  --home-red-cards     Optional current home red cards. Red cards produce warning only.");
        Console.WriteLine("  --away-red-cards     Optional current away red cards. Red cards produce warning only.");
        Console.WriteLine("  --last-goal-minute   Optional latest goal minute. If within recent-goal-minutes, decision is WAIT.");
        Console.WriteLine("  --recent-goal-minutes Optional cooldown threshold. Default: 2.");
    }


}
