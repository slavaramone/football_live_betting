# After-goal continuation analysis patch

Added command:

```bash
dotnet run --project src/LiveTotalsHelper.Tools -- analyze-after-goal-continuation --profile npl-queensland --validation true
```

Default outputs from the calibration dataset path:

- `*-after-goal-continuation.csv` row-level goal continuation rows
- `*-after-goal-continuation-summary.csv` aggregate buckets

What it measures:

- next goal after each AfterGoal row
- minutes to next goal
- next goal within configurable windows, default `5,10,15,20`
- goal effect: `FirstGoal`, `Equalizer`, `CreatesOneGoalLead`, `CutsDeficit`, `ExtendsToTwoGoalLead`, `ExtendsToThreePlusLead`
- goal side and score before/after
- open Over lines for configured target lines
- summary buckets by goal effect, minute band, side, goal number, score state, and exact score

Useful options:

```bash
--windows 5,10,15,20
--target-lines 2.5,3.5,4.5
--summary-output <path>
--min-summary-rows 5
```
