# Player attributes at Football Manager depth, calibrated to the real league's shape

The approved plan lives in this file; it is the design of record for slices A0–A11. The deep-stats
work it builds on is in `docs/stats-audit.md`.

## Why

`PlayerRating` is five attributes and a mean. The engine reads `Overall` and three "shapes"
(attribute − Overall); rebounds and assists are dealt by position tables; usage is `Overall²`;
minutes are split linearly by Overall across ten players; there is no shooting attribute; and
development and retirement have no production caller, so ratings never change. The result is the
complaint the goal was set against: a league where almost nobody averages 25, nobody plays 36
minutes, and the best rebounder and passer look like role players — on box scores *and* on the
off-ball and defensive stats the real league counts (deflections, contests, screen assists, box-outs,
charges, loose balls).

## The yardstick: battery 8, league shape

`sim-audit` battery 8 (`.claude/skills/sim-audit/targets.json` → `league_shape`) states the real
league's **per-player distribution** — counts above thresholds, leader values and percentiles, per
game, over players with 40+ games — for scoring, rebounding, playmaking, defence, hustle, minutes and
usage. The numbers are aggregates computed by a local, uncommitted script from five seasons of
per-player totals (hustle and usage from one); no player rows or names are committed. Bands are the
five seasons' range, widened.

The six-team fixture cannot show a thirty-team league's tails, so the yardstick is a seeded,
fictional **calibration league**: `GeneratedLeagueDataSource` (Infrastructure) generates thirty
fifteen-man rosters as league-pack content and loads it through `LeaguePackDataSource.FromEnvelope`,
under `data/rulesets/calibration-league.json` (82 games, eight playoff teams a conference). The same
seed gives the same league. `scratch/calib` (local) has a `shape N [pack]` mode that plays N regular
seasons and scores every battery-8 row.

## A0 baseline (before any attribute work)

20 seasons of the calibration league, current engine: **12 pass, 49 fail, 40 not modelled, over 20 seasons of 30 teams**.

| Row | Simulated | Real league | Band | |
|---|---|---|---|---|
| `ppg.leader` | 25.14 | 32.74 | [28.88, 35.55] | ✗ |
| `ppg.count_ge_25` | 0.70 | 15.20 | [11, 21] | ✗ |
| `ppg.count_ge_20` | 10.15 | 43.80 | [34, 54] | ✗ |
| `rpg.leader` | 7.72 | 13.52 | [11.8, 15.4] | ✗ |
| `rpg.count_ge_10` | 0.00 | 12.60 | [8, 17] | ✗ |
| `orpg.leader` | 2.18 | 4.67 | [4.25, 5.35] | ✗ |
| `apg.leader` | 4.42 | 10.93 | [10.08, 12.16] | ✗ |
| `apg.count_ge_8` | 0.00 | 6.40 | [2, 10] | ✗ |
| `apg.count_ge_6` | 0.00 | 24.80 | [20, 30] | ✗ |
| `spg.leader` | 2.01 | 2.20 | [1.76, 3.16] | ✓ |
| `bpg.leader` | 1.92 | 3.26 | [2.62, 4.02] | ✗ |
| `mpg.leader` | 33.65 | 37.76 | [35.45, 39.92] | ✗ |
| `mpg.count_ge_34` | 0.50 | 32.40 | [16, 44] | ✗ |
| `mpg.count_ge_30` | 17.10 | 88.20 | [68, 101] | ✗ |
| `fta_pg.leader` | 5.45 | 11.08 | [9.47, 12.87] | ✗ |
| `fg3m_pg.leader` | 3.50 | 4.60 | [4.17, 5.12] | ✗ |
| `tov_pg.leader` | 3.49 | 4.32 | [3.75, 4.9] | ✗ |
| `pf_pg.count_ge_3` | 40.15 | 17.40 | [8, 28] | ✗ |
| `players_ge_40_games` | 312.60 | 357.20 | [330, 389] | ✗ |

The local real-league pack, one season, reads the same way (one player at 25+, assists leader 4.2, rebounds
leader 8.0, nobody at 34 minutes). The middle of every distribution is roughly right and the tails
are missing: the Gaussian-flatness signature. Its causes, in the order the slices take them:
minutes (linear in Overall, capped at 42, ten-man rotations), usage (Overall² clamped at 2.1× the
team mean), rebounds and assists (position tables, no skill), free throws (flat, no foul-drawing
skill), and no hustle events at all.

## The plan

| Slice | Content |
|---|---|
| A0 | Battery 8 targets, the calibration league, the `shape` harness, this baseline. |
| A1 | Attribute model (0–100): scoring, playmaking, defence, rebounding, mental, physical groups; body measurements; hidden attributes and personality; CA/PA; position ratings and familiarity; traits as data; latent-factor generator with a talent tail; pack schema 3 (v2 expanded), save schema 5; `IsRetired`/`Biography` saved. Engine reads the old shapes mapped from the new attributes, so calibration does not move. |
| A2 | FM-style attributes tab: grouped columns, figure always printed, sequential tint; CA/PA as coach's stars; personality; position familiarity; traits; season-on-season arrows. |
| A3 | Rotation and minutes by position rating and stamina; play types and usage concentration → minutes, usage and scoring tails. |
| A4 | Matchups; shooting by attribute against the defender; contests; defended FGA/FGM; free throws and fouls drawn. |
| A5 | Rebounding and box-outs from attributes. |
| A6 | Assists, potential assists, screen assists. |
| A7 | Deflections, steals, blocks, charges drawn, loose balls. |
| A8 | Consistency, big games, stamina and fatigue, injury proneness; traits in the engine. |
| A9 | Per-attribute development and retirement, wired to the season boundary. |
| A10 | Scouting (masked attribute ranges, scout reports, CA/PA stars); AI and contracts read CA/PA/positions. |
| A11 | Close-out: battery 8 green on the calibration league, headline rows within band on the real-league pack; docs; the user's `/sim-audit 1–8` baseline. |

Decisions (approved): attributes 0–100 with `Overall` kept as CA; attributes centred on the league
mean before the engine reads them, so a pack on its own scale still plays basketball; every new term
named and capped in `MatchModelBounds`, integer basis points, seeded; the 23 frozen `sim-regress`
rows stay green or move only with a written retune; the real-league pack's attributes are derived locally
and never committed.
