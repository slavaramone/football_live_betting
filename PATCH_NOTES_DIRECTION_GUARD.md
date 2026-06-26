# Direction guard patch

Changes:

- Added state-correction direction guard.
- Default mode is `up-only`.
- In `up-only` mode, usable correction buckets with `factor < 1.0` are gated out and resolved as factor `1.0`.
- Upward correction factors remain active.
- `--state-correction-direction both` disables the direction guard and uses both upward and downward factors.
- Existing `StateCorrectionGatedRows` counters now include correction-scope gated rows and direction-guarded rows.
- Avalonia live pricing uses the same direction guard as evaluation.
- Profile default `stateCorrectionShrinkMatches` changed to `25`.

Commands:

```bash
dotnet run --project src/LiveTotalsHelper.Tools -- evaluate-live-total-performance --profile china-super-league --validation true --compare-scopes true --state-correction-scope fixed-minute --state-correction-direction up-only
```

Disable direction guard:

```bash
dotnet run --project src/LiveTotalsHelper.Tools -- evaluate-live-total-performance --profile china-super-league --validation true --compare-scopes true --state-correction-scope fixed-minute --state-correction-direction both
```
