# After-goal pattern experiment

Added command:

```bash
dotnet run --project src/LiveTotalsHelper.Tools -- analyze-after-goal-patterns --profile npl-queensland --validation true
```

The command reads the live-total calibration dataset and writes:

```text
<calibration-file>-after-goal-patterns.csv
```

The CSV groups after-goal states by:

- minute band
- goal number
- goal effect
- score before / score after
- score state after
- goal side

It reports remaining-goals residuals and Over 2.5 / Over 3.5 baseline vs pattern probabilities, Brier, LogLoss, and fallback source.

Goal effect buckets:

- FirstGoal
- Equalizer
- CreatesOneGoalLead
- CutsDeficit
- ExtendsToTwoGoalLead
- ExtendsToThreePlusLead
- Other

Pattern probability fallback:

```text
ExactScore -> StateAfter -> GoalEffect -> AfterGoalAll
```

Also compacted `config/league-profiles.json`. Missing artifact paths and common defaults are now derived by `LeagueProfileStore` from `modelRoot`, profile key, training seasons, and current season.
