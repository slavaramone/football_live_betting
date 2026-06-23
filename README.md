Patched live-total model with first late-game attack attempt.

Run validation:

```bash
dotnet run --project src/LiveTotalsHelper.Tools -- evaluate-live-total-performance --profile china-super-league --validation true --compare-scopes true --state-correction-scope fixed-minute --state-correction-direction up-only --late-game-correction boost-up
```

Compare against previous baseline:

```bash
dotnet run --project src/LiveTotalsHelper.Tools -- evaluate-live-total-performance --profile china-super-league --validation true --compare-scopes true --state-correction-scope fixed-minute --state-correction-direction up-only --late-game-correction off
```

Main new output row to inspect:

```text
FixedMinuteLateGame
```


## Patch: late-game boost only for 2.5 line

Late-game boost is now line-gated. It applies only when all are true:

```text
stateTrigger = FixedMinute
minute >= 70
target line <= 2.5
correction factor > 1.0
```

Use `--late-game-max-line 2.5` to keep the current behavior, or lower/raise it for experiments.

## Patch: better market-total selector

`build-live-total-calibration-dataset` now selects the main market total from the most balanced O/U line instead of taking the median expected goals across all available total lines. Alternative lines are ignored after the selected balanced line is chosen.
