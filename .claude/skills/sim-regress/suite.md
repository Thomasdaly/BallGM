# Frozen regression suite

Written by `/sim-patch` from `reports/sim-audit-5a6d4d6e-6b09-4056-9535-6ffd4c70cedf.md`.
12 assertions. Battery-1 rows (1-5) always run regardless of this file per `/sim-regress`'s own rule;
listed here too so tolerance/format is in one place. Rows marked `needs:` are currently uncomputable —
the field/mechanism doesn't exist yet (see patch ID). `/sim-regress` should report those as
**untestable**, not FAIL, until the named patch lands.

| ID | Check | Target | Band | Needs |
|---|---|---|---|---|
| 1-pts | Σ player.Points per team == team score | 0 mismatches | exact | — (already computable, enforced by `BoxScore.Create`) |
| 1-mp | Σ player.Minutes per team == 240 + 25×OT | 0 violations | exact | — (already computable) |
| 2-fgm | Σ player.MadeTwoPt×2+MadeThreePt×3+MadeFT per team == team score | 0 mismatches | exact | done (P-4a/c; enforced at `PlayerStatLine` construction, not just checked after the fact) |
| 3-ast | player.Assists <= player.MadeFieldGoals, all players | 0 violations | exact | done (P-4a: FGM now a real per-player figure) |
| 4-reb | ORB+DRB per team == opponent-misses accounted, no leak/dup | 0 mismatches | exact | done (P-4e split plus P-4b/c FGA/FTA — see notes) |
| 5-usg | Σ USG% per team per stint | 100 | exact | done (P-4e; "per game" not "per stint" — the engine has no lineup-stint granularity) |
| 6-pace | pace_poss_per_48 | 99.0 | [96.0, 101.0] | — (already computable via controlled matchups; real-game pace still needs seed-mixing exposed) |
| 7-ortg | ortg (pooled, with home-court) | 115.0 | [112.0, 117.0] | done (P-4b/c mechanics + P-5 retune; measured ≈116.8) |
| 8-winspread | sd_team_win_pct | 0.150 | [0.135, 0.165] | done (P-1b, P-2, P-6; measured ≈0.147 after P-6's nudge) |
| 9-nollscully | noll_scully (this league's actual 78-game season, not the formula's default 82) | 2.70 | [2.50, 3.00] | done (P-6; measured ≈2.60) |
| 10-margin | sd_game_margin | 13.5 | [12.5, 14.5] | done (P-4b/c mechanics + P-5 retune; measured ≈13.45-13.5) |
| 11-pc1 | first_pc_variance_share (rating covariance) | 0.45 | [0.20, 0.70] | done (P-3: multi-attribute `PlayerRating`); match-engine/box-score half of battery 4 still owed |
| 12-dynasty | 50-season chained `LeagueSession` run | 0 `EmptyRotationCode` failures | exact | done (P-1a: roster-floor auto-resign; P-1b: draft-day wiring) |

## Notes for `/sim-regress`

- Rows 1-2 pass today (verified 2026-09-11, 50 seasons / 12,372 games / 24,744 team-game lines, 0/0).
- Row 12 now passes — P-1a (roster-floor auto-resign) and P-1b (`BallGM.Rules.Draft.DraftDay` wired to
  `LeagueSession.ConcludeSeason` via `ISeasonEngine.RunDraft`) both landed 2026-09-11/12; a 15-season
  chained run neither auto-resigns zero times nor drafts zero times (`DynastyIntegrationTests`).
- Row 8 now passes — P-1b's roster churn plus P-2 (`FixtureLeagueDataSource.TeamStrengthOffsets`
  widened from `[6,3,0,-2,-5,-8]` to `[14,6,0,-4,-11,-18]`, empirically tuned against 1,000 seeded
  seasons of the real `PossessionMatchEngine`) together measured sd_team_win_pct ≈ 0.152 (n=6,000
  team-seasons), inside band. P-6 later nudged this same constant once more (see its own note below)
  once noll_scully needed a hair more spread than sd_team_win_pct alone required.
- Row 11 now passes — `PlayerRating` (`src/BallGM.Domain/Players/PlayerRating.cs`) carries five
  attributes (Height/Speed/Strength/Passing/LateralQuickness) instead of one scalar; `Overall` is
  derived (mean), never stored. `BallGM.Rules.Players.RatingProfileGenerator` (shared by
  `ProspectGenerator` and `FixtureLeagueDataSource`) generates them via a shared "talent" value plus
  a "frame" archetype draw plus per-attribute noise, empirically tuned (`DefaultSpread = 28`) against
  a 20,000-sample PCA measurement: first_pc_variance_share ≈ 0.447, inside band. Save schema bumped
  1 → 2 (`PlayerEnvelope` stores the five attributes, not `Overall`); old saves are refused, not
  migrated (none existed before this version moved). Re-measured row 8 (sd_team_win_pct) afterward as
  a regression check: 0.1373 (n=6,000), still inside [0.135, 0.165] but closer to the floor than
  P-2's 0.1525 — the added per-player rating noise cost a little of P-2's margin. Not re-tuned
  further; both measurements are valid samples inside band. The match engine itself
  (`PossessionMatchEngine`) still reads only the derived `Overall` int, so batteries 4.1/4.3/4.4 (the
  attribute↔stat couplings and usage/stacking slopes) remain untestable until the box-score mechanics
  (P-4) and a match-engine multi-attribute wiring pass both land — deliberately out of scope here.
- Row 5-usg now passes, and row 4-reb partially — both were already computed internally by
  `PossessionMatchEngine.Side` and simply never exposed: `FinishSide` already derived rebounds from two
  separate shares (`DefensiveReboundShare`=74% of opponent misses, `OffensiveReboundShare`=24% of own
  misses) before merging them into one counter, and `UsageWeights` already existed as the exact
  per-player shot-share used to pick a scorer. This patch stopped the merge (`PlayerStatLine.
  OffensiveRebounds`/`DefensiveRebounds`, `Rebounds` now derived) and exposed usage as a whole-percent
  `UsagePercent` (largest-remainder apportionment, guaranteeing an exact 100 sum — enforced at
  `BoxScore.Create`, same shape as the existing points-must-match-score check). Zero new randomness,
  zero change to the scoring/margin formula, so no recalibration was needed and none was done —
  confirmed by the full suite staying green including `MatchModelCalibrationTests` unchanged. Row
  4-reb is marked partial rather than done: the split itself is real and leak-free, but the officially
  stated conservation formula needs FGA/FTA, which this patch does not add. Save schema
  (`SeasonEnvelope`) bumped 1 → 2 for the reshaped `PlayerStatLineEnvelope`; refused, not migrated.
- Rows 2-fgm, 3-ast, 4-reb (fully now), 7, 10 all landed together — `PossessionMatchEngine.PlayPeriod`
  was rewritten from one binary score/miss draw off a blended scoring rate into a real possession
  model: not every possession reaches a shot (`MatchModelBounds.FieldGoalAttemptRate`, an unmodeled,
  unexposed stand-in for a turnover — `tov_pct` still untestable, not gated by this suite), a real shot
  attempt draws a type (`ThreePointAttemptShare`, directly answering `fg3a_per_fga`) and an
  efficiency-adjusted field-goal percentage (`BaseTwoPointPercentage`/`BaseThreePointPercentage`/
  `FieldGoalPercentSwingAtMaximumStrength`), a miss can draw a shooting foul and a make an and-one
  (`ShootingFoulChanceOnMiss`/`AndOneChanceOnMake`/`FreeThrowPercentage`), and only a missed *final*
  free throw re-joins the reboundable pool. `PlayerStatLine` gained FGA/FGM/3PA/3PM/FTA/FTM, with
  `Points == 2×(FGM−3PM)+3×3PM+FTM` enforced at construction (battery 1.1 is now a real, checked
  invariant, not a trivial one). `BaseOffensiveEfficiency` retuned 10,800 → 11,500 (the sim audit's own
  ortg target; unreachable under the old mechanism regardless of tuning). Measured (n≈1,200-2,000,
  homeRating=awayRating=72): efg_pct≈0.549, ts_pct≈0.578, ft_per_fga≈0.219, fg3a_per_fga≈0.400,
  ortg≈116.8, sd_game_margin≈13.45-13.5 — all six inside band.
- **The audit's own margin finding reversed direction, and a new mechanism was needed, not just
  tuning.** The audit had `sd_game_margin` too *high* (17.44 vs 13.5) under the old single-draw
  mechanic. Splitting that draw into several smaller, independent per-shot draws (attempt/type/
  make-miss/foul/FT) *reduced* variance further (measured ≈10.4) rather than the compounding increase
  expected — real shooting-percentage variation is correlated within a game, not independent shot to
  shot, so a per-game shared "hot/cold night" term (`MatchModelBounds.GameShootingVarianceRange`,
  applied identically to both shot types for the whole `PlayPeriod` call) was added deliberately to
  reproduce that correlation, tuned by direct measurement (500→850 tried; 850 landed sd_game_margin
  in band). This is a genuinely fatter-tailed distribution than the old mechanism's, which is why two
  bands in `MatchModelCalibrationTests` moved (mean margin ceiling 17→19, top-of-range score 142→145) —
  documented in that file's own doc comment as the P-5 retune, not a convenience widening: both were
  tuned against the old, audit-flagged-as-wrong mechanism and had never been validated against real
  basketball data themselves.
- Save schema (`SeasonEnvelope`) bumped 2 → 3 for the six new `PlayerStatLineEnvelope` fields; refused,
  not migrated, same policy as every version move so far.
- **P-6 lands the last row.** `noll_scully = sd_observed / (0.5/sqrt(games))` — the target's own
  formula defaults `games` to 82 (a real-NBA season), but this league plays 78
  (`data/rulesets/default-league.json`'s `regularSeasonGameCount`), so every measurement here uses 78,
  not the harness's original hardcoded 82. With the correct denominator, `sd_team_win_pct` alone
  (already in band since P-2) put `noll_scully` at 2.491 — 0.009 under the 2.50 floor, and
  `talent_share_of_var = (var_observed - 0.25/games)/var_observed` at 0.839, already inside
  [0.80, 0.88]. `FixtureLeagueDataSource.TeamStrengthOffsets` was nudged one more time
  (`[14,6,0,-4,-11,-18]` → `[15,6,0,-4,-12,-19]`) to clear noll_scully with real margin rather than
  leave it a hair under and hope sampling noise covers the gap. Measured after (n=6,000 team-seasons):
  sd_team_win_pct ≈ 0.147, noll_scully ≈ 2.60, talent_share_of_var ≈ 0.85 — all three comfortably in
  band together. No permanent regression test added for this (matching P-2's precedent — these are
  `/sim-regress`-tracked measurements, not xunit assertions); re-measure before nudging the constant
  again.

All 12 rows in the frozen suite now pass. Everything past this point is the audit's fuller wishlist,
not gated by this file: turnovers/steals/blocks/fouls (no mechanism, nothing here needs them) and
battery 4's attribute↔stat couplings/usage-stacking slopes (need those plus a match-engine
multi-attribute wiring pass, deliberately deferred in P-3).
