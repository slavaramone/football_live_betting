# LiveTotalsHelper

Clean live football totals helper.

Modeling commands kept in this cleanup:

- fit-weibull
- build-live-total-calibration-dataset
- analyze-live-total-calibration
- fit-live-total-state-correction
- evaluate-live-total-performance

League profiles are stored only in `config/league-profiles.json`.

## State correction gating

Default evaluation/pricing now applies state correction only to `FixedMinute` rows:

```bash
dotnet run --project src/LiveTotalsHelper.Tools -- evaluate-live-total-performance --profile china-super-league --validation true --compare-scopes true --state-correction-scope fixed-minute
```

Supported values:

- `fixed-minute` - apply correction only to fixed-minute rows. This is the default.
- `all` - old behavior, apply correction to every trigger when a usable bucket exists.
- `none` - disable state correction everywhere.

When correction is gated out, corrected probability equals baseline probability and output rows are counted in `StateCorrectionGatedRows`.
