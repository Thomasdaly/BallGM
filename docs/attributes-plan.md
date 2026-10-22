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
| A3a | Tendencies on the sheet (2K-style, 0–100: touches, shot, drive, post-up, shot close/mid/three, pull-up, pass, crash the glass, gamble, block shot, foul), derived from attributes when a pack does not state them; usage from touches and the shot diet from the location tendencies; automatic rotation by position rating with NBA-like minutes and stamina → minutes, usage and scoring tails. |
| A3b | Rotation editor: the user sets starters and minutes per player (a Rules validator: 240 a game, per-player bounds, five starters at least covering the floor); saved with the league; the season engine plays it; AI teams keep the automatic rotation. |
| A4 | Matchups; shooting by attribute against the defender; contests; defended FGA/FGM; free throws and fouls drawn; the shot tendency (shoot or move the ball). |
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

## A1: the sheet, as built

37 visible attributes, nine hidden, potential, body, familiarity and traits on `PlayerRating`
(the type kept its name; `Overall` is Current Ability). Latent-factor generator with position
archetypes; first principal component pinned in band. Pack schema 3 with legacy expansion, save
schema 5. The engine reads the same three shapes, now from the sheet — but they are structural now
(a centre is always tall and slow, a point guard always the handler), so the fixture league moved
within its bands: win spread 0.163, talent share 0.879, league margin SD 14.42 (ceilings 0.165, 0.88,
14.5) at 1,000 seasons. Re-centred in A3. Every flat-sheet rate row held.

Battery 8 after A1 is no better — the calibration league's scoring leader averages about 22, the
rebounding and assists leaders about 7 and 4.4 — because nothing in the engine reads the new
attributes yet. The new generator spreads skill more evenly across a sheet than five attributes
did, which flattens the tails a little further while the engine still reads only Overall. A3 onward
is where the tails come from.

The local real-league pack loads (schema 2, expanded). Deriving its full sheet from real per-player data is
scheduled with the engine slices, where each attribute group first matters.

## A2: the sheet on screen

Attributes tab (three columns of grouped attributes in tier badges, with descriptions), scouting report
on the Overview (stars, personality, body, positions, traits), POT on the roster. Change arrows wait
for A9. The real-league pack still shows sheets expanded from five attributes — several 100s for its stars —
until its sheets are derived from real per-player data alongside the engine slices.

## Amendment (user request, 2026-10-10): tendencies and a rotation editor

Added to A3 at the user's request: NBA-2K-style touch and shot tendencies, and setting minutes for the
lineup. Tendencies are the engine's per-player choice inputs (how often he touches the ball, how often
he shoots it, where from, how he gets there); attributes stay the skill that decides how well. They
are part of the sheet, so they ride the same pack and save shape (save schema 6). The rotation editor
makes minutes a decision the GM owns instead of a formula.

## A3a–A6 (engine half): the tails, as built

Built together, because each one moves the others' distributions and calibrating them one at a time
re-tuned the same constants four times. The rotation editor (A3b) and the off-ball stats (A7) are still
to come.

- **Rotation (A3a).** `DepthChartBuilder` picks the starting five by position fit — the best five and
  arrangement among the top seven by Current Ability plus each position's best natural, scored by
  `PositionRatings` less familiarity, an awkward fit (or any off-position spot on a flat sheet) a last
  resort — and gives minutes by role (`MinutesAllocationBounds.RoleMinutes`: 33/31/30/28/27 for the
  starters, 25/22/18/14/12 off the bench), then ±0.2 a point of Current Ability against the rotation
  and a capped stamina shape, inside a 38-minute planned ceiling.
- **Usage and the ball (A3a/A6).** A possession has a handler (playmaking against the rotation, by
  position and touch tendency) who either shoots it himself (his shot and pass tendencies) or passes to
  a team-mate picked by usage; a make off his pass is usually his assist, a make he made himself rarely
  anyone's. Usage is Current Ability and the touch tendency as differences from the rotation's mean
  (`MatchModelBounds.RelativeWeight`, a weight that doubles every N points — a difference, so a pack on
  its own scale plays the same game). Turnovers split between the handler and the receiver.
- **Shooting (A4, first half).** Make percentage moves with the zone's skill shape (finishing, close
  range, mid-range, three) and free-throw percentage with the free-throw shape. A self-created shot
  leans to the rim and the pull-up, a caught one to the spot-up three. Shooting fouls and and-ones are
  drawn by zone (the rim draws contact) and by an attack tendency and Current Ability, so the stars live
  at the line. Contests, matchups and defended shots are the second half of A4.
- **Rebounds (A5) and blocks.** Positional weights now multiplied by rebounding, boxing out, offensive
  rebounding and blocking against the rotation; an offensive rebounder may put it back himself.
- **Availability (part of A8).** Injury chance moves with injury proneness; the base rate rose from
  1.2% to 1.8% a full game (bounded under 2%) so a season has the missed games a real one does.
- **The calibration league** gives its top three roster slots a wide triangular spread, so one team has
  two stars and another none — which is where a league's leaders come from.

Measured (10 seasons of the calibration league, battery 8): scoring leader 31.7 (real 32.7),
rebounds 14.7 (13.5), assists 10.9 (10.9), minutes 38.2 (37.8), free-throw attempts 11.4 (11.1),
threes made 5.0 (4.6), turnovers 4.4 (4.3), fouls 3.8 (3.6) — every leader row in band except blocks
(2.4 against 3.3). Team rates all in band (ortg, pace, eFG, FT/FGA 0.23, 3PA/FGA 0.43, AST/FGM 0.62,
fouls, steals, blocks); fixture league 1,000 seasons: win SD 0.157, Noll-Scully 2.78, talent share
0.870, margin SD 14.0; season sim time 16.0 ms (budget 16.9) after caching each depth chart's derived
side profile and stint plans. Locked by `LeagueShapeCalibrationTests` (six seeded seasons).

Still out of band, and why: the percentile and count rows (p90 scorers, 8+ rebounders, 3+ threes) run
high because a team's season minutes still go to ten players — the real league spreads them over
twelve (rest, minor knocks, garbage time). That is the rest of A8, with the blocks leader (an elite
rim protector should also raise his team's block rate, not only take a bigger share of it).
