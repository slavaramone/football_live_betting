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

Pending migrations are applied automatically at the start of the utility app.

Current schema is intentionally flat and contains only three tables:

- `Matches`
- `MatchEvents`
- `MatchTeamStats`

For local development, create the database first:

```sql
CREATE DATABASE livetotalshelper;
```

Then run the tool. It will apply the initial migration automatically.
