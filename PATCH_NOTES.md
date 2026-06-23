# Late-game attack patch

Implemented first late-game special-handling attempt on top of the current baseline:

- MarketTotal base
- FixedMinute-only state correction
- shrinkMatches = 25
- up-only direction guard

## New late-game correction mode

Added a configurable late FixedMinute boost:

```text
--late-game-correction boost-up|off
--late-game-start-minute 70
--late-game-factor-multiplier 1.15
--late-game-max-factor 2.5
```

Default profile values enable the first attacking attempt:

```json
"stateCorrectionLateGameMode": "boost-up",
"stateCorrectionLateGameStartMinute": 70,
"stateCorrectionLateGameFactorMultiplier": 1.15,
"stateCorrectionLateGameMaxFactor": 2.5
```

Behavior:

```text
FixedMinute, minute >= 70, factor > 1.0:
    factor = min(maxFactor, 1 + (factor - 1) * multiplier)

All other rows:
    unchanged
```

The existing up-only direction guard still runs first, so downward factors are still gated to 1.0 before late-game logic.

## Reporting

Added late-game diagnostics:

- `LateGameBoostedRows` column in model evaluation CSV
- `LateGameBoostedRows` column in betting metrics CSV
- `LateGameBoostedRows` column in probability move bucket CSV
- `FixedMinuteLateGame` summary rows in model and betting metrics outputs

## Price command

`price-live-total` now uses the same late-game boost logic and prints the active late-game correction settings.
