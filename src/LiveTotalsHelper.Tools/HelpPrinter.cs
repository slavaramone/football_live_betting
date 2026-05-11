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
        Console.WriteLine("  build-live-total-calibration-dataset  Build correction rows using the exact live-total pricing service.");
        Console.WriteLine("  fit-weibull                           Fit a league-wide Weibull timing model from a goal-minute CSV.");
        Console.WriteLine("  price-live-total                      Price live Over totals from starting odds, score state and fitted timing model.");
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


    public static void PrintBuildLiveTotalCalibrationDataset()
    {
        Console.WriteLine("Build live total calibration dataset usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- build-live-total-calibration-dataset \\");
        Console.WriteLine("    --profile npl-nsw \\");
        Console.WriteLine("    --season-ids 48254,57783,71036 \\");
        Console.WriteLine("    --minutes 10,15,20,25,30,35,40,45,50,55,60,65,70,75,80,85 \\");
        Console.WriteLine("    --output data/datasets/npl-nsw-live-total-calibration.csv");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  --profile              Optional league profile key/name from league-profiles.json.");
        Console.WriteLine("  --profiles-file        Optional profiles JSON path. Default: league-profiles.json.");
        Console.WriteLine("  --model                Fitted timing model JSON. Required unless provided by profile.");
        Console.WriteLine("  --league               Optional league filter. Default comes from profile when present.");
        Console.WriteLine("  --season-id/--season-ids Optional SofaScore season filter.");
        Console.WriteLine("  --minutes              Snapshot minutes. Default: 10,15,...,85.");
        Console.WriteLine("  --empirical-weight     Optional override. Default comes from profile, otherwise 0.80.");
        Console.WriteLine("  --output               Output CSV path. Default: data/datasets/<league>-<seasons>-live-total-calibration.csv.");
        Console.WriteLine();
        Console.WriteLine("No odds are required. Each row stores one historical live state, the same fitted timing-share components used by `price-live-total`, and the realised remaining goals.");
    }

    public static void PrintAnalyzeLiveTotalCalibration()
    {
        Console.WriteLine("Analyze live total calibration usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- analyze-live-total-calibration \\");
        Console.WriteLine("    --input data/datasets/norwegian-1st-division-live-total-calibration.csv \\");
        Console.WriteLine("    --training-season-ids 40407,47820,57356 \\");
        Console.WriteLine("    --test-season-ids 70186 \\");
        Console.WriteLine("    --output data/reports/norwegian-1st-division-live-total-calibration-train-test.csv");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  --input                 Required live-total calibration dataset CSV.");
        Console.WriteLine("  --output                Output bucket report CSV. Default: <input>-analysis.csv.");
        Console.WriteLine("  --training-season-ids   Optional comma-separated season ids used to estimate correction factors.");
        Console.WriteLine("  --test-season-ids       Optional comma-separated season ids used to evaluate correction factors.");
        Console.WriteLine();
        Console.WriteLine("Without train/test ids, outputs all-data correction factors by minute band and detailed live score state.");
        Console.WriteLine("With train/test ids, estimates factors on training seasons and applies them to held-out test seasons.");
    }

    public static void PrintFitLiveTotalStateCorrection()
    {
        Console.WriteLine("Fit live total state correction usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-live-total-state-correction \\");
        Console.WriteLine("    --input data/datasets/norwegian-1st-division-live-total-calibration.csv \\");
        Console.WriteLine("    --training-season-ids 40407,47820,57356 \\");
        Console.WriteLine("    --output data/models/live-total-state-correction/norwegian-1st-division-2022-2024.json");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  --input                 Required live-total calibration dataset CSV.");
        Console.WriteLine("  --training-season-ids   Required comma-separated seasons used to estimate factors.");
        Console.WriteLine("  --output                Output correction JSON path. Default: <input>-state-correction.json.");
        Console.WriteLine("  --min-bucket-matches    Minimum distinct matches for a minute-band/state bucket. Default: 100.");
        Console.WriteLine("  --min-state-matches     Minimum distinct matches for score-state fallback. Default: 200.");
        Console.WriteLine("  --min-factor            Lower clamp for factors. Default: 0.50.");
        Console.WriteLine("  --max-factor            Upper clamp for factors. Default: 2.50.");
    }

    public static void PrintFitWeibull()
    {
        Console.WriteLine("Fit Weibull usage:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-weibull \\");
        Console.WriteLine("    --league \"Norwegian 1st Division\" \\");
        Console.WriteLine("    --season-ids 2022,2023,2024,2025 \\");
        Console.WriteLine("    --group-by ScoreStateBefore \\");
        Console.WriteLine("    --output data/models/weibull/norwegian-1st-division-2022-2025.json");
        Console.WriteLine();
        Console.WriteLine("Fit Weibull arguments:");
        Console.WriteLine("  --league             Required league filter.");
        Console.WriteLine("  --season-id/--season-ids Optional SofaScore season filter.");
        Console.WriteLine("  --round/--from-round/--to-round Optional round filter.");
        Console.WriteLine("  --output             Output JSON model path. Default: data/models/weibull/{league}-{season selection}.json");
        Console.WriteLine("  --max-minute         Normalize CDF/remaining share to this match minute. Default: 90");
        Console.WriteLine("  --group-by           Optional DB-backed grouping. Currently supported: ScoreStateBefore.");
        Console.WriteLine("  --min-group-goals    Minimum goals required to fit a group model. Default: 30");
        Console.WriteLine("  --max-iterations     Maximum MLE iterations. Default: 100");
        Console.WriteLine("  --tolerance          MLE convergence tolerance. Default: 1e-9");
        Console.WriteLine("  --blend-weibull-weight Weight for blended model. Default: 0.30, so blend = 30% Weibull + 70% empirical.");
        Console.WriteLine("  --include-unreliable true/false. Default: false.");
        Console.WriteLine();
        Console.WriteLine("The fitting sample is built directly from imported database matches and goal events; no input CSV is required.");
        Console.WriteLine("Output JSON stores pure Weibull, empirical bucket, blended, and optional score-state group timing models.");
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
        Console.WriteLine("  --state-correction   Optional fitted live-total state-correction JSON. Can also come from profile.");
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
