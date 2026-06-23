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
