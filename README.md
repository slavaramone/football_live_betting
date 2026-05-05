# LiveTotalsHelper

Simple personal Avalonia MVVM app skeleton for live football Over/Under betting.

This version fixes the previous skeleton by adding:

- A real Visual Studio / Rider solution file: `LiveTotalsHelper.sln`
- Separate projects/modules instead of one loose project
- Required pre-match 1X2 odds inputs
- Only the first live Over targets:
  - Over 1.5
  - Over 2.0
  - Over 2.5
  - Over 3.0

## Solution structure

```text
LiveTotalsHelper.sln
src/
  LiveTotalsHelper.App/
    Avalonia UI shell
    Views/
    ViewModels/

  LiveTotalsHelper.Core/
    Domain models
    Service interfaces
    App contracts

  LiveTotalsHelper.Modeling/
    Odds-to-xG converter
    Weibull timing math
    Poisson remaining-goals pricing
    Bet decision service

  LiveTotalsHelper.Infrastructure/
    Sample match repository
    Sample Weibull parameter provider
```

## Architectural conception

The app follows the modules we discussed, but keeps them lightweight for the first version.

### Data Import / Infrastructure

Currently represented by:

```text
LiveTotalsHelper.Infrastructure
  SampleMatchRepository
  SampleWeibullParameterProvider
```

Later this should be replaced with official-site scrapers/importers, odds feed importers, CSV import, or database access.

### Core

Currently represented by:

```text
LiveTotalsHelper.Core
  MatchSnapshot
  OddsInput
  ModelSummary
  BetDecision
  WeibullParameters
  IMatchRepository
  IWeibullParameterProvider
  IBettingModelService
```

This project should stay independent from Avalonia.

### Modeling

Currently represented by:

```text
LiveTotalsHelper.Modeling
  BettingModelService
  WeibullMath
  PoissonMath
```

Current first-version flow:

```text
Pre-match O/U odds
  -> infer total xG
  -> calculate league-wide Weibull remaining share
  -> calculate opponents-wide Weibull remaining share
  -> mix the two curves using sample-size shrinkage
  -> adjust for score state and red cards
  -> calculate Over probabilities for 1.5 / 2.0 / 2.5 / 3.0
  -> compare against live book odds
```

### App / Dashboard

Currently represented by:

```text
LiveTotalsHelper.App
  MainWindow
  MainWindowViewModel
```

The screen is intentionally simple:

```text
Left:
  League selector
  Match list

Right:
  Selected match state
  Required odds inputs
  Model summary
  Betting decision table
  Notes
```

## Run

From the solution root:

```bash
dotnet restore LiveTotalsHelper.sln
dotnet run --project src/LiveTotalsHelper.App/LiveTotalsHelper.App.csproj
```

Requires .NET 8 SDK or newer.

## Next implementation steps

1. Add persistence / database project or repository implementation.
2. Replace sample match repository with real match import.
3. Fit league-wide Weibull parameters from goal minutes.
4. Fit opponent-wide Weibull parameters and store sample size.
5. Add historical backtesting module.
6. Add data freshness fields only after live import exists.

## Console utility app

The solution now includes `LiveTotalsHelper.Tools`, a console app for one-off and repeatable data utility tasks.

### Download SofaScore JSON

Single round:

```bash
dotnet run --project src/LiveTotalsHelper.Tools -- download-sofascore \
  --league "NPL NSW" \
  --tournament-id 1274 \
  --season-id 57783 \
  --round 25 \
  --output data/sofascore
```

Round range:

```bash
dotnet run --project src/LiveTotalsHelper.Tools -- download-sofascore \
  --league "NPL NSW" \
  --tournament-id 1274 \
  --season-id 57783 \
  --from-round 1 \
  --to-round 30 \
  --output data/sofascore \
  --delay-ms 600
```

The command downloads:

- Calendar JSON from `/api/v1/unique-tournament/{tournamentId}/season/{seasonId}/events/round/{round}`
- Match incidents JSON from `/api/v1/event/{eventId}/incidents`
- Team statistics JSON from `/api/v1/event/{eventId}/statistics`

Output structure:

```text
data/sofascore/
  npl-nsw/
    season-57783/
      round-25/
        calendar.json
        manifest.json
        events/
          11973303/
            event-meta.json
            incidents.json
            statistics.json
```

Useful options:

```text
--overwrite true|false     default false
--incidents true|false     default true
--statistics true|false    default true
--delay-ms 450             delay between event endpoint calls
```
