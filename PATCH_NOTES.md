# Patch notes - market-total base

Implemented market-total expected final goals as the offline live-total base.

## Changed

- `build-live-total-calibration-dataset` now reads imported Flashscore Over/Under odds and selects a market expected final-goals anchor per match.
- Calibration CSV now includes:
  - `MarketTotalLine`
  - `MarketTotalSource`
  - `MarketExpectedFinalGoals`
  - `ExpectedFinalGoals`
  - `ExpectedFinalGoalsSource`
- State correction fitting now uses row-level market expected final goals:
  - `baselineRemaining = ExpectedFinalGoals * TimingRemainingShare`
  - rows without `ExpectedFinalGoals` are skipped.
- Calibration analysis now uses market expected final goals instead of league average final goals.
- Model evaluation now uses market expected final goals instead of league average final goals.
- Betting metrics now uses market expected final goals instead of league average final goals.

## Market total selection

For each match:

1. Clean complete Over/Under pairs are built from `FlashscoreOdds`.
2. Suspicious overround pairs are ignored.
3. Each line/odds pair is converted to expected final goals using the existing market-total estimator.
4. The match anchor is the median expected final goals across clean pairs.

## Missing odds

Rows without market total remain in the calibration CSV but are skipped by fitting/evaluation commands.

## Correction gating patch

- Added `--state-correction-scope fixed-minute|all|none` to `evaluate-live-total-performance` and `price-live-total`.
- Default scope is `fixed-minute`.
- When correction is gated out, factor is forced to `1.0` and the row remains eligible instead of being blocked as an unsupported sparse bucket.
- `AfterGoal` and `AfterRedCard` are therefore baseline-only by default.
- Added `StateCorrectionAppliedRows` and `StateCorrectionGatedRows` to model evaluation, betting metrics, and probability-move bucket CSV output.
