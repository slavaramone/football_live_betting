# Database

The tools project uses PostgreSQL via EF Core/Npgsql.

Connection string is stored in `src/LiveTotalsHelper.Tools/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "LiveTotalsDb": "Host=localhost;Port=5432;Database=livetotalshelper;Username=postgres;Password=postgres"
  }
}
```

Pending migrations are applied automatically only by the dedicated import command:

```bash
dotnet run --project src/LiveTotalsHelper.Tools -- import-sofascore \
  --league "NPL NSW" \
  --tournament-id 1274 \
  --season-id 88562 \
  --round 2 \
  --input data/sofascore
```

`download-sofascore` only writes raw JSON files and does not touch PostgreSQL.

Current schema is intentionally flat and contains only three tables:

- `Matches`
- `MatchEvents`
- `MatchTeamStats`

For local development, create the database first:

```sql
CREATE DATABASE livetotalshelper;
```

Then run `import-sofascore`. It will apply the initial migration automatically.
