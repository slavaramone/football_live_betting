namespace LiveTotalsHelper.Tools;

public static class HelpPrinter
{
    public static void Print()
    {
        Console.WriteLine("LiveTotalsHelper.Tools");
        Console.WriteLine();
        Console.WriteLine("Data commands:");
        Console.WriteLine("  download-flashscore                 Download rendered Flashscore calendar, incidents, stats and odds JSON.");
        Console.WriteLine("  download-sofascore                  Download SofaScore calendar, incidents and team statistics JSON.");
        Console.WriteLine("  import-flashscore                   Import saved Flashscore JSON into PostgreSQL and apply migrations.");
        Console.WriteLine("  validate-db                         Validate imported PostgreSQL data quality.");
        Console.WriteLine("  db-validate                         Alias for validate-db.");
        Console.WriteLine("  price-live-total                    Price live totals with empirical settlement tables.");
        Console.WriteLine();
        Console.WriteLine("Modeling commands:");
        Console.WriteLine("  fit-weibull                         Fit goal-timing model from imported DB events.");
        Console.WriteLine("  build-live-total-calibration-dataset Build live-total calibration rows.");
        Console.WriteLine("  analyze-live-total-calibration       Analyze correction factors by trigger/state.");
        Console.WriteLine("  fit-live-total-state-correction      Fit trigger/state correction factors.");
        Console.WriteLine("  evaluate-live-total-performance      Run model MAE/RMSE and betting probability metrics together.");
        Console.WriteLine();
        Console.WriteLine("Profiles:");
        Console.WriteLine("  Default profile file: config/league-profiles.json");
        Console.WriteLine("  Override with --profiles-file <path> when needed.");
        Console.WriteLine();
        PrintModelingExamples();
        Console.WriteLine();
        PrintCommonArguments();
    }

    public static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        Console.Error.WriteLine();
        Print();
        return 2;
    }

    private static void PrintModelingExamples()
    {
        Console.WriteLine("Modeling examples:");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-weibull --profile china-super-league --validation true");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- build-live-total-calibration-dataset --profile china-super-league --validation true");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- analyze-live-total-calibration --profile china-super-league --validation true");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- fit-live-total-state-correction --profile china-super-league --validation true");
        Console.WriteLine("  dotnet run --project src/LiveTotalsHelper.Tools -- evaluate-live-total-performance --profile china-super-league --validation true --compare-scopes true --state-correction-scope fixed-minute --late-game-correction boost-up");
    }

    private static void PrintCommonArguments()
    {
        Console.WriteLine("Common modeling arguments:");
        Console.WriteLine("  --profile                         Profile key/name from config/league-profiles.json.");
        Console.WriteLine("  --validation true                 Use validation paths and validation train/test split from the profile.");
        Console.WriteLine("  --training-season-ids             Comma-separated training season ids override.");
        Console.WriteLine("  --test-season-ids                 Comma-separated test season ids override.");
        Console.WriteLine("  --input                           Calibration dataset CSV override.");
        Console.WriteLine("  --state-correction                State correction JSON override.");
        Console.WriteLine("  --state-correction-scope          fixed-minute | all | none. Default: fixed-minute.");
        Console.WriteLine("  --state-correction-direction      up-only | both. Default: up-only; down factors become 1.0.");
        Console.WriteLine("  --late-game-correction            boost-up | off. Default: boost-up from profile.");
        Console.WriteLine("  --late-game-start-minute          Default: 70.");
        Console.WriteLine("  --late-game-factor-multiplier     Extra multiplier for positive late FixedMinute factors. Default: 1.15.");
        Console.WriteLine("  --late-game-max-factor            Max factor after late boost. Default: 2.5.");
        Console.WriteLine("  --shrink-matches                  State-correction shrink strength. Profile default: 25; 0 disables.");
        Console.WriteLine("  --model-output                    Model evaluation CSV path for evaluate-live-total-performance.");
        Console.WriteLine("  --betting-output                  Betting metrics CSV path for evaluate-live-total-performance.");
        Console.WriteLine("  --edge-output                     Probability move bucket CSV path.");
        Console.WriteLine("  --target-lines                    Optional comma-separated total lines.");
        Console.WriteLine("  --compare-scopes true             Evaluate FullModel, AfterGoalOnly and SecondHalfAfterGoalOnly in one run.");
        Console.WriteLine("  --scope                           full-model | after-goal-only | 2h-after-goal-only.");
    }
}
