# Patch notes

Cleaned comparison/prototype code and removed parametric goal-pricing fallback.

## Removed
- Prototype baseline-comparison command and help text.
- Baseline/correction comparison implementation file.
- Obsolete app-level `BettingModelService` and `IBettingModelService` wiring.
- Parametric probability helper file and remaining parametric settlement fallback paths.

## Kept as base
- Empirical remaining-goals settlement fitting and resolving.
- Empirical settlement pricing from remaining-goals distributions.
- Existing validation, calibration, timing, state-correction and live-pricing commands.

## Behavior change
- `price-live-total` now requires a configured empirical settlement JSON.
- If empirical settlement is missing or unsupported for the current state, pricing fails instead of falling back to a parametric distribution.

## Build fix after empirical-only cleanup

- Added explicit `Compile Remove` entries to `LiveTotalsHelper.Tools.csproj` for removed prototype files.
- This prevents stale files left in an existing working tree from being compiled after extracting/copying the patch over the current project.
- Explicitly excluded: `GoalModelComparisonEvaluator.cs`, `LiveTotalBaselineComparer.cs`, compare-goal-models/compare-live-total-baselines prototypes, and old Poisson files.

If those stale files are still physically present in your local folder, they can be deleted manually; they are no longer part of the project build.

