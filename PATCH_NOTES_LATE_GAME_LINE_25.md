# Late-game boost limited to 2.5 line

Patch goal: keep the late-game boost only where validation showed benefit: FixedMinute, minute >= 70, positive correction factor, target total line <= 2.5.

Changes:
- Added `LateGameMaxLine` to `LiveTotalLateGameCorrectionOptions`.
- Added profile field `stateCorrectionLateGameMaxLine` with default `2.5`.
- Added CLI option `--late-game-max-line`.
- Betting evaluation now resolves state correction per target line, so line 2.5 can be boosted while line 3.5 is not.
- Price-live-total now resolves state correction per target line for the displayed line prices.
- Goal-count evaluation does not apply line-specific late boost, because it has no target betting line.

Expected default:
- `late-game-correction=boost-up`
- `late-game-start-minute=70`
- `late-game-max-line=2.5`
- `late-game-factor-multiplier=1.15`
- `late-game-max-factor=2.5`
