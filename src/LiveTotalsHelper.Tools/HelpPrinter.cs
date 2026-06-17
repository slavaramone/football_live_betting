namespace LiveTotalsHelper.Tools;

public static class HelpPrinter
{
    public static void Print()
    {
        Console.WriteLine("LiveTotalsHelper.Tools");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  download-flashscore  Download rendered Flashscore calendar, incidents, stats and odds JSON.");
        Console.WriteLine("  download-sofascore   Download SofaScore calendar, incidents and team statistics JSON only.");
        Console.WriteLine("  import-sofascore     Import saved SofaScore JSON into PostgreSQL and apply pending migrations.");
        Console.WriteLine("  validate-db          Validate imported PostgreSQL data quality for modelling.");
        Console.WriteLine("  build-live-total-calibration-dataset  Build correction rows from the shared live-total timing core.");
        Console.WriteLine("  analyze-live-total-calibration        Compare correction factors by trigger/state.");
        Console.WriteLine("  fit-live-total-state-correction       Fit trigger/state correction factors.");
        Console.WriteLine("  fit-live-total-empirical-settlement   Fit empirical remaining-goals settlement tables.");
        Console.WriteLine("  evaluate-live-total-model             Evaluate baseline vs exact trigger/state correction.");
        Console.WriteLine("  evaluate-live-total-betting-metrics   Evaluate line-specific Brier/log-loss/direction metrics.");
        Console.WriteLine("  fit-weibull                           Fit a league-wide Weibull timing model from imported DB events.");
        Console.WriteLine("  price-live-total                      Price live Over totals from starting odds, score state and fitted timing model.");
        Console.WriteLine();
        PrintDownloadFlashscore();
        Console.WriteLine();
        PrintDownloadSofaScore();
        Console.WriteLine();
        PrintImportSofaScore();
        Console.WriteLine();
        PrintValidateDb();
        Console.WriteLine();
        PrintBuildLiveTotalCalibrationDataset();
        Console.WriteLine();
        PrintAnalyzeLiveTotalCalibration();
        Console.WriteLine();
        PrintFitLiveTotalStateCorrection();
        Console.WriteLine();
        PrintFitLiveTotalEmpiricalSettlement();
        Console.WriteLine();
        PrintEvaluateLiveTotalModel();
        Console.WriteLine();
        PrintEvaluateLiveTotalBettingMetrics();
        Console.WriteLine();
        PrintFitWeibull();
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

    public static void PrintDownloadFlashscore()
    {
        Console.WriteLine("Download Flashscore usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- download-flashscore \\");
        Console.WriteLine("    --url \"https://www.flashscore.co.ke/football/china/super-league/results/\" \\");
        Console.WriteLine("    --league \"China Super League\" --tournament-id 900001 --season-id 2026 \\");
        Console.WriteLine("    --season-year 2026 --country China --country-code CHN --output data/flashscore");
        Console.WriteLine();
        Console.WriteLine("Flashscore arguments:");
        Console.WriteLine("  --url                Flashscore results page URL.");
        Console.WriteLine("  --league             League name used in folder structure and calendar JSON.");
        Console.WriteLine("  --tournament-id      Numeric id stored in SofaScore-compatible fields. Use a stable project-local id.");
        Console.WriteLine("  --season-id          Numeric season id stored in SofaScore-compatible fields. Use a stable project-local id.");
        Console.WriteLine("  --season-name        Optional season name. Defaults to --season-year/default year.");
        Console.WriteLine("  --season-year        Optional season year string. Defaults to --default-year.");
        Console.WriteLine("  --country            Optional country name.");
        Console.WriteLine("  --country-code       Optional country code.");
        Console.WriteLine("  --round              Optional single round filter.");
        Console.WriteLine("  --round-from         Optional first round filter. Alias: --from-round.");
        Console.WriteLine("  --to-round           Optional last round filter.");
        Console.WriteLine("  --output             Output root. Default: data/flashscore");
        Console.WriteLine("  --overwrite          true/false. Default: false");
        Console.WriteLine("  --incidents          true/false. Download match-summary incidents into incidents.json. Default: true");
        Console.WriteLine("  --statistics         true/false. Download match stats into statistics.json. Default: true");
        Console.WriteLine("  --skip-stat          Shortcut for --statistics false.");
        Console.WriteLine("  --odds               true/false. Download odds markets into odds.json. Default: true");
        Console.WriteLine("  --delay-ms           Delay between detail page requests. Default: 450");
        Console.WriteLine("  --headless           true/false. Default: true");
        Console.WriteLine("  --show-browser       Debug shortcut. Runs Chromium visible.");
        Console.WriteLine("  --render-wait-ms     Delay after opening page before parsing. Default: 8000");
        Console.WriteLine("  --detail-wait-ms     Delay after opening each match detail page before parsing. Default: 3000");
        Console.WriteLine("  --show-more-wait-ms  Delay after each Show more click. Default: 2000");
        Console.WriteLine("  --max-show-more-clicks Maximum Show more clicks. Default: 40");
        Console.WriteLine("  --default-year       Year used for Flashscore dates that omit year. Default: current UTC year.");
        Console.WriteLine();
        Console.WriteLine("Output layout matches the SofaScore downloader: <output>/<league-slug>/season-<season-id>/round-XX/calendar.json.");
        Console.WriteLine("Calendar and statistics JSON are SofaScore-compatible for the current importer; odds are stored as Flashscore-specific odds.json.");
    }

    public static void PrintDownloadSofaScore()
    {
        Console.WriteLine("Download usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- download-sofascore \\");
        Console.WriteLine("    --league \"NPL NSW\" --tournament-id 1274 --season-id 88562 --round 2");
        Console.WriteLine();
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- download-sofascore \\");
        Console.WriteLine("    --league \"NPL NSW\" --tournament-id 1274 --season-id 88562 --round-from 1 --to-round 30 \\");
        Console.WriteLine("    --output data/sofascore --delay-ms 600 --overwrite false");
        Console.WriteLine();
        Console.WriteLine("Download arguments:");
        Console.WriteLine("  --league             League name used in folder structure, for example \"NPL NSW\".");
        Console.WriteLine("  --tournament-id      SofaScore unique tournament id.");
        Console.WriteLine("  --season-id          SofaScore season id.");
        Console.WriteLine("  --round              Single round to download.");
        Console.WriteLine("  --round-from         First round when downloading a range. Alias: --from-round.");
        Console.WriteLine("  --to-round           Last round when downloading a range.");
        Console.WriteLine("  --calendar-mode      SofaScore calendar path segment: round or last. Default: round");
        Console.WriteLine("  --output             Output root. Default: data/sofascore");
        Console.WriteLine("  --delay-ms           Delay between event endpoint calls. Default: 450");
        Console.WriteLine("  --overwrite          true/false. Default: false");
        Console.WriteLine("  --incidents          true/false. Default: true");
        Console.WriteLine("  --statistics         true/false. Default: true");
        Console.WriteLine("  --skip-stat          Shortcut for --statistics false. Use when league stats endpoint is unavailable.");
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
        Console.WriteLine("    --league \"NPL NSW\" --tournament-id 1274 --season-id 88562 --round-from 1 --to-round 30 --input data/sofascore");
        Console.WriteLine();
        Console.WriteLine("Import arguments:");
        Console.WriteLine("  --league             League name used in folder structure.");
        Console.WriteLine("  --tournament-id      SofaScore unique tournament id fallback. Optional, default: 0.");
        Console.WriteLine("  --season-id          SofaScore season id.");
        Console.WriteLine("  --round              Single round to import.");
        Console.WriteLine("  --round-from         First round when importing a range. Alias: --from-round.");
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


    public static void PrintBuildLiveTotalCalibrationDataset()
    {
        Console.WriteLine("Build live total calibration dataset usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- build-live-total-calibration-dataset --profile allsvenskan");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- build-live-total-calibration-dataset --profile allsvenskan --validation true");
        Console.WriteLine();
        Console.WriteLine("Profile defaults:");
        Console.WriteLine("  production: league, modelPath, calibrationDatasetPath, trainingSeasonIds, snapshotMinutes, includeEventTriggers");
        Console.WriteLine("  validation: validationModelPath, validationCalibrationDatasetPath, validationTrainingSeasonIds + validationTestSeasonIds");
        Console.WriteLine();
        Console.WriteLine("Overrides remain available: --model, --league, --season-id/--season-ids, --minutes, --include-event-triggers, --empirical-weight, --output.");
    }

    
    public static void PrintAnalyzeLiveTotalCalibration()
    {
        Console.WriteLine("Analyze live total calibration usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- analyze-live-total-calibration --profile allsvenskan");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- analyze-live-total-calibration --profile allsvenskan --validation true");
        Console.WriteLine();
        Console.WriteLine("Production mode uses profile calibrationDatasetPath and writes calibrationAnalysisPath.");
        Console.WriteLine("Validation mode uses profile validation dataset/analysis paths and profile validation train/test seasons.");
        Console.WriteLine("Overrides remain available: --input, --output, --training-season-ids, --test-season-ids.");
    }

    
    public static void PrintFitLiveTotalStateCorrection()
    {
        Console.WriteLine("Fit live total state correction usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-live-total-state-correction --profile allsvenskan");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-live-total-state-correction --profile allsvenskan --validation true");
        Console.WriteLine();
        Console.WriteLine("Production mode uses profile calibrationDatasetPath, stateCorrectionPath, trainingSeasonIds.");
        Console.WriteLine("Validation mode uses profile validation dataset/path and validationTrainingSeasonIds.");
        Console.WriteLine("Overrides remain available: --input, --training-season-ids, --output, --min-bucket-matches, --min-factor, --max-factor.");
    }

    public static void PrintFitLiveTotalEmpiricalSettlement()
    {
        Console.WriteLine("Fit empirical settlement usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-live-total-empirical-settlement --profile allsvenskan");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-live-total-empirical-settlement --profile allsvenskan --validation true");
        Console.WriteLine();
        Console.WriteLine("Fits remaining-goals distributions from the calibration dataset and writes empiricalSettlementPath / validationEmpiricalSettlementPath.");
        Console.WriteLine("Overrides remain available: --input, --training-season-ids, --output, --min-bucket-rows, --min-bucket-matches, --max-remaining-goals, --smoothing.");
    }

    
    public static void PrintEvaluateLiveTotalModel()
    {
        Console.WriteLine("Evaluate live total model usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- evaluate-live-total-model --profile allsvenskan --validation true");
        Console.WriteLine();
        Console.WriteLine("Validation mode uses profile validationCalibrationDatasetPath, validationStateCorrectionPath, validationTestSeasonIds, validationModelEvaluationPath.");
        Console.WriteLine("Scope comparison:");
        Console.WriteLine("  --compare-scopes true             Print/write FullModel, AfterGoalOnly and SecondHalfAfterGoalOnly in one run.");
        Console.WriteLine("  --scope full-model|after-goal-only|2h-after-goal-only");
        Console.WriteLine("Overrides remain available: --input, --state-correction, --test-season-ids, --output.");
    }

    

    public static void PrintEvaluateLiveTotalBettingMetrics()
    {
        Console.WriteLine("Betting metrics evaluation usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- evaluate-live-total-betting-metrics \\");
        Console.WriteLine("    --profile eliteserien --validation true");
        Console.WriteLine();
        Console.WriteLine("Betting metrics arguments:");
        Console.WriteLine("  --profile / --profiles-file      Use profile paths and validation split.");
        Console.WriteLine("  --validation true                Use validation input/state-correction/test seasons from profile.");
        Console.WriteLine("  --input                          Calibration dataset CSV override.");
        Console.WriteLine("  --state-correction               State correction JSON override.");
        Console.WriteLine("  --test-season-ids                Comma-separated test season ids.");
        Console.WriteLine("  --target-lines                   Optional comma-separated lines. Defaults to profile targetLines.");
        Console.WriteLine("  --output                         Summary CSV path.");
        Console.WriteLine("  --edge-output                    Edge-bucket CSV path.");
        Console.WriteLine("  --compare-scopes true            Print/write FullModel, AfterGoalOnly and SecondHalfAfterGoalOnly in one run.");
        Console.WriteLine("  --scope full-model|after-goal-only|2h-after-goal-only");
        Console.WriteLine();
        Console.WriteLine("Output metrics:");
        Console.WriteLine("  Per scope, line and trigger: Brier, log loss, direction accuracy, actual over rate.");
        Console.WriteLine("  Edge bucket CSV groups by scope and corrected probability move vs baseline probability.");
    }

    public static void PrintFitWeibull()
    {
        Console.WriteLine("Fit Weibull usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-weibull --profile allsvenskan");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-weibull --profile allsvenskan --validation true");
        Console.WriteLine();
        Console.WriteLine("Profile defaults:");
        Console.WriteLine("  production: league, modelPath, trainingSeasonIds, maxMinute, groupByColumn, minGroupGoals, blendWeibullWeight");
        Console.WriteLine("  validation: validationModelPath, validationTrainingSeasonIds");
        Console.WriteLine();
        Console.WriteLine("Overrides remain available: --league, --season-id/--season-ids, --output, --max-minute, --group-by, --min-group-goals, --max-iterations, --tolerance, --blend-weibull-weight, --include-unreliable.");
        Console.WriteLine("The fitting sample is built directly from imported database matches and goal events; no input CSV is required.");
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


    public static void PrintPriceLiveTotal()
    {
        Console.WriteLine("Price live total usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- price-live-total \\");
        Console.WriteLine("    --profile allsvenskan \\");
        Console.WriteLine("    --starting-line 2.5 --starting-over 1.90 --starting-under 1.90 \\");
        Console.WriteLine("    --state-trigger fixed-minute --minute 60 --home-goals 1 --away-goals 0 \\");
        Console.WriteLine("    --before-round 10 \\");
        Console.WriteLine("    --live-over-odds \"2.5=2.30,3.5=4.20\" --live-under-odds \"2.5=1.65,3.5=1.98\"");
        Console.WriteLine();
        Console.WriteLine("With a complete profile, live pricing only needs match-specific information:");
        Console.WriteLine("  starting market, trigger, minute, score, current live odds, and usually --before-round unless defaultBeforeRound is kept current in the profile.");
        Console.WriteLine();
        Console.WriteLine("Price live total arguments:");
        Console.WriteLine("  --profile            League profile key/name from league-profiles.json.");
        Console.WriteLine("  --state-trigger      Optional: fixed-minute, after-goal, or after-red-card. Default: fixed-minute.");
        Console.WriteLine("                       Betting uses only exact usable trigger/minute-band/state buckets; sparse buckets are NO BET.");
        Console.WriteLine("  --starting-line      Required starting/pre-match total line.");
        Console.WriteLine("  --starting-over      Required starting/pre-match over odds.");
        Console.WriteLine("  --starting-under     Required starting/pre-match under odds.");
        Console.WriteLine("  --minute             Required current match minute.");
        Console.WriteLine("  --home-goals         Required current home goals.");
        Console.WriteLine("  --away-goals         Required current away goals.");
        Console.WriteLine("  --live-over-odds / --live-under-odds Optional bookmaker live odds, needed for edge/bet decisions.");
        Console.WriteLine("  --empirical-settlement Optional empirical remaining-goals settlement JSON. Defaults to profile empiricalSettlementPath.");
        Console.WriteLine("  --use-probability-move-filter true|false Optional; require probability move threshold before BET/LEAN.");
        Console.WriteLine("  --min-over-probability-move 0.10      Optional global Over move threshold.");
        Console.WriteLine("  --min-under-probability-move -0.12    Optional global Under move threshold.");
        Console.WriteLine("  --under-signals-betting-allowed true|false Optional; default false.");
        Console.WriteLine("  --before-round       Needed for current-season volume unless defaultBeforeRound is maintained in the profile.");
        Console.WriteLine("  --home-red-cards / --away-red-cards Optional; red-card states remain warning/manual-review states.");
        Console.WriteLine("  --last-goal-minute   Optional; fixed-minute checks shortly after a goal become WAIT.");
        Console.WriteLine("  --decision-mode      Optional override: FullModel, AfterGoalOnly, SecondHalfAfterGoalOnly.");
        Console.WriteLine("  --min-minute / --min-line / --allowed-lines Optional overrides for profile decision rules.");
        Console.WriteLine("  --require-goal-trigger true|false and --fallback-betting-enabled true|false override profile gates.");
        Console.WriteLine();
        Console.WriteLine("All stable league/model settings should live in the profile: modelPath, stateCorrectionPath, league, seasons, empirical weight, target lines, current-season volume settings, thresholds.");
        Console.WriteLine("Profile decision-rule fields are also honored: decisionMode, minMinute, requireGoalTrigger, minLine, allowedLines, fallbackBettingEnabled.");
    }

    
}
