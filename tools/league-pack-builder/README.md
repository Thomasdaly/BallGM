# League pack builder

Turns one season of per-player box-score stats into a BallGM league pack (`pack.json` + `ruleset.json`),
then tunes it so the match engine reproduces that season's table.

The tool itself is content-neutral. It ships no league data and knows no league.

> **Real-world data stays local.** A pack built from a real league is for private use on your own machine.
> Build it under `local-packs/` (gitignored). Never commit, bundle, or distribute it — see
> `docs/vision.md` (content neutrality) and the safety section of `CLAUDE.md`. You are responsible for
> complying with the terms of whatever source you take stats from.

## Inputs

| flag | what |
|---|---|
| `--players` | CSV of per-player season stats. Column names from common exports are recognised (see `ALIASES` in `build_pack.py`): name, team, age, GP, MIN, FGM, FGA, 3PM, 3PA, FTM, FTA, OREB, DREB, AST, STL, BLK, TOV, PF, PTS; optional position, height (inches), weight, experience, salary. Traded players: an aggregate row (`TOT`/`2TM`) is used for stats if present, otherwise team rows are summed; the last team row decides the roster. |
| `--per-mode` | `totals` (default) or `per-game`. |
| `--teams` | CSV of team records: a key column (abbreviation, alias, or full team name) plus `W`, `L`. |
| `--alignment` | JSON: `name`, `conferences` → `divisions` → team keys, `teams` (`key`, `name`, `franchiseName`, optional `aliases`, `logo`, `taxRepeater`, and `colours` — `{"primary": "#RRGGBB", "secondary": "#RRGGBB"}`, secondary optional; packs are written at schema version 2), and `rulesetOverrides` applied to `data/rulesets/default-league.json` (`null` removes a rule). |
| `--season` | the year the season starts in (2024 for 2024-25). |

## Build, tune, play

```bash
# fictional smoke test — no real data involved
python3 sample/make_sample.py
python3 tune.py --players sample/generated/players.csv --teams sample/generated/teams.csv \
  --alignment sample/generated/alignment.json --season 2040 --out ../../local-packs/sample --fit-records

BALLGM_LEAGUE_PACK=$PWD/../../local-packs/sample/pack.json dotnet run --project ../../src/BallGM.Client.Avalonia
```

## Output format

Pack schema version 4: every player's **full sheet** (all visible attributes, hidden attributes, potential, body,
position familiarity, traits, tendencies) is written by `sheet.py`, so a load reads ratings as stated instead of
generating them. Contracts may also state per-season `options` and `guaranteed` amounts, `signedSeason` and
`teamSince` (see `docs/architecture.md` → "League packs"); this generic builder writes salaries with an optional
final-season option, and a pipeline with contract data adds the rest.

## How ratings are derived

1. Production: Game Score per 36 minutes, shrunk toward replacement level by minutes played (`--shrink-minutes`,
   `--replacement-gmsc36`), blended with minutes per game (`--rate-weight`).
2. Overall: that composite is percentile-ranked and mapped through a quantile table anchored to the shipped
   fixture's scale, stretched around the median by `--overall-scale`.
3. The full sheet (`sheet.py`): each attribute is the Overall plus `--attribute-spread` (default 15) times a
   standardised stat composite, on a logistic curve so the best players keep their shape, re-centred so the
   sheet's weighted Current Ability (`PlayerRating.Overall`) is exactly the target. Optional per-player extras
   (`row.extras`: usage, assist/rebound percentages, true shooting, PIE, shot-diet shares, hustle per 36)
   sharpen the composites when a pipeline supplies them.
4. Salaries: the CSV's salary column if present, otherwise synthesised from Overall and service against the
   ruleset's compensation floor and ceiling.

## Tuning (`tune.py`)

The match engine reads only `Overall`, and team strength enters only as a difference, so how far ratings
stretch decides how spread out the simulated table is. `tune.py`:

- **Phase 1** sweeps `--overall-scale`, simulating each candidate with `PackCheck`, and keeps the one whose
  simulated win% spread and per-team win% best match the real table.
- **Phase 2** (`--fit-records`) fits a per-team rating offset (clamped to ±15) over a few iterations, moving
  each team by the gap between its real and simulated record. This captures what a box score can't — defence,
  scheme, fit — and pins it to the players who produced the record. It makes the opening season realistic;
  those offsets travel with the players if they move.

Output: `tuning-report.json` (every step), `team-offsets.json`, `build-report.json`, `records.csv`.

`PackCheck/` is a small console harness, deliberately outside `BallGM.slnx`:
`dotnet PackCheck/bin/Release/net10.0/PackCheck.dll <pack.json> [records.csv|-] [seasons]`.
