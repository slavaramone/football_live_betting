# FLB shrink correction patch

Run state-correction fit after this patch:

```bash
dotnet run --project src/LiveTotalsHelper.Tools -- fit-live-total-state-correction --profile china-super-league --validation true --shrink-matches 300
```

Then evaluate:

```bash
dotnet run --project src/LiveTotalsHelper.Tools -- evaluate-live-total-performance --profile china-super-league --validation true --compare-scopes true --state-correction-scope fixed-minute
```

To test stronger shrink:

```bash
--shrink-matches 500
```

To disable shrink:

```bash
--shrink-matches 0
```
