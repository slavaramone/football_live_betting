# Patch notes

- Removed goal-model comparison commands and implementation.
- Removed all Poisson-related model/pricing code from the project.
- Removed old app model service wiring that depended on Poisson calculations.
- Kept only these modeling commands:
  - `fit-weibull`
  - `build-live-total-calibration-dataset`
  - `analyze-live-total-calibration`
  - `fit-live-total-state-correction`
  - `evaluate-live-total-performance`
- Replaced separate model and betting evaluation commands with `evaluate-live-total-performance`.
- Betting metrics now use empirical remaining-goals settlement fitted internally from training seasons during evaluation.
- Removed duplicate/old league profile files; only `config/league-profiles.json` remains.
- Updated profile loading defaults to `config/league-profiles.json` and made relative profile resolution search upward from the app/tool output directory.
