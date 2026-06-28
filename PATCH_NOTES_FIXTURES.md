# Flashscore fixture import patch

Added nearest-round fixture workflow:

- `download-flashscore-fixtures`
- `parse-flashscore-fixtures` alias
- `import-flashscore-fixtures`

Fixture download uses the profile `flashscoreFixturesUrl`, skips Show more, writes only the nearest visible fixture round, and does not download incidents/statistics/odds.

Fixture import imports calendar files only, preserving existing incident/stat/odds rows.

Profile fixture URLs added for:

- China Super League
- Norway 1st Division / OBOS-ligaen
- NPL Victoria
