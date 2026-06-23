# Patch notes — state correction shrinkage

Changed state-correction fitting so fitted correction factors are shrunk toward 1.0 before being saved and used by evaluation/pricing.

Formula:

```text
weight = matches / (matches + shrinkMatches)
factor = clamp(1 + weight * (rawFactor - 1), minFactor, maxFactor)
```

Default:

```text
--shrink-matches 300
```

Use `--shrink-matches 0` to disable shrinkage and reproduce the old raw factor behavior.

Updated:

- `fit-live-total-state-correction`
- state-correction JSON output
- profile config default
- help text

The saved bucket still contains `RawFactor`; new `ShrinkWeight` shows how much of the raw correction was retained.
