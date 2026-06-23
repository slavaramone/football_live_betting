# Market total selector patch

Changed calibration dataset market-total selection from median of all clean O/U pairs to a balanced-main-line selector.

## Previous behavior
- Build clean Over/Under pairs per match/bookmaker/line.
- Select representative line by closest fair Over probability to 50%.
- But use median expected goals across all clean candidate lines.

This could mix main line and alternative lines.

## New behavior
- Build clean latest Over/Under pairs per match/bookmaker/line.
- Group pairs by total line.
- For each line compute:
  - pair count
  - median no-vig Over probability
  - median expected final goals
  - median overround
  - latest timestamp
- Select the main line by:
  1. median fair Over probability closest to 50%
  2. more bookmaker pairs
  3. overround closest to 1.0
  4. latest timestamp
- Use selected line median expected final goals.
- MarketTotalSource now reports selected line, selected-pair count, total clean-pair count, median fairOver, expected goals, representative odds, and ignored alternative-line pairs.

## Affected command
- build-live-total-calibration-dataset

Downstream commands use the new MarketExpectedFinalGoals from the regenerated calibration dataset.
