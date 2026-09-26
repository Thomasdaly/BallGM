# BallGM simulation audit

Batteries: all (1–7). Era target: `nba_2018_2024` per `targets.json`. Harness recipe recorded at
`.claude/skills/sim-audit/harness.md`.

## A. VERDICT

**ship-blocking**

Not primarily because the possession-by-possession scoring model is wrong — isolated and measured
correctly (see the note on a labeling bug I found and fixed in my own harness, §methodology), its
win-probability-vs-strength response is smooth, monotonic, and fits a logistic curve at R² > 0.99.
It is ship-blocking because most of what the batteries ask for cannot be measured at all: the box
score tracks four counting stats (PTS/REB/AST/MIN) and nothing else, the player-rating model is a
single clamped scalar, none of Milestone 8's draft-generation/development/retirement rules are wired
into the season loop, and — the most concrete symptom — a league advanced through the real
`LeagueSession` stack with no human re-signing free agents **cannot survive past season 3–4**: a team
runs out of players it can field. A game whose season boundary cannot be advanced unattended has not
yet reached "long-term simulation quality," which `docs/vision.md` states as a pillar.

## B. FINDINGS TABLE

| ID | Test | Sim value | Target | Band | Delta | Severity | Root-cause hypothesis | Confidence |
|---|---|---|---|---|---|---|---|---|
| 1.1/1.2 | PTS additivity (player lines sum to team score) | 0 mismatches / 495,596 team-games | 0 | 0 | 0 | — (pass) | `BoxScore.Create` refuses a mismatched total by construction; this is a type invariant, not a measured property of the simulation. | CONFIRMED |
| 1.1/1.2 (cont.) | FGM/3PM/FTM breakdown of PTS | not tracked | 0 mismatches | 0 | n/a | **P1 (missing mechanism)** | No field for made 2s vs 3s vs free throws exists on `PlayerStatLine`/`BoxScore` (`src/BallGM.Domain/Seasons/BoxScore.cs`). The engine internally knows 2 vs 3 (`AwardBasket(points: 2|3, ...)`) but discards the split before persisting. | CONFIRMED |
| 1.3 | AST ≤ FGM per player; team AST/FGM band | not tracked | in band | — | n/a | **P1 (missing mechanism)** | No FGM field per player at all (only points, which conflates makes and value). Cannot be checked even indirectly. | CONFIRMED |
| 1.4 | Rebound conservation (ORB/DRB vs opponent misses) | not tracked | 0 leak/dup | 0 | n/a | **P1 (missing mechanism)** | `PlayerStatLine.Rebounds` is a single undifferentiated total; there is no ORB/DRB split, and misses/attempts are not exposed at all outside the engine's private `Side` class. | CONFIRMED |
| 1.5 | Possession parity, `\|POSS_home − POSS_away\| ≤ 1` | 0/N (exact equality, not merely ≤1) | 100% within 1 | — | 0 | pass, but structurally trivial | `PossessionMatchEngine.Play` draws **one** `possessions` value and hands it unchanged to both `PlayPeriod(home,...)` and `PlayPeriod(away,...)` — the two sides literally cannot differ pre-overtime. Real basketball's last-possession asymmetry does not exist here. Not a bug; a minor over-idealization worth naming. | CONFIRMED (read from `PossessionMatchEngine.cs:87-92`) |
| 1.6 | MP conservation, `sum(MP) == 5×48×(1+OT)` | 484,200/495,596 exact at 240; remaining 11,396 (2.30%) are exact positive multiples of 25 | 0 unexplained violations | 0 | 0 once OT-adjusted | pass | The 25-minute steps are `OvertimeMinutes(5) × MinutesOnFloor(5)` from `AwardOvertimeMinutes` — i.e. real, correctly-conserved overtime, not a leak. No genuine violation found. | CONFIRMED |
| 1.7 | Σ USG% == 100% per stint | not tracked | 100% | — | n/a | **P1 (missing mechanism)** | No USG% field anywhere; usage exists only as an internal scoring weight (`Side.UsageWeights`) never surfaced. | CONFIRMED |
| 2 | 8 of 13 `rate_stats` keys (`efg_pct, ts_pct, tov_pct, orb_pct, ft_per_fga, fg3a_per_fga, ast_per_fgm, pf_per_game, blk_per_game, stl_per_game`) | not tracked | — | — | n/a | **P1 (missing mechanism)** | Same root cause as 1.1–1.7: the box score has no shots, free throws, turnovers, steals, blocks, or fouls in it at all — there is no free-throw mechanic, no turnover mechanic, no steal/block mechanic, and no foul mechanic anywhere in `PossessionMatchEngine`. A possession is only ever "make" or "miss." | CONFIRMED |
| 2 | `pace_poss_per_48` | 98.01 (sd 5.46), n=20,000 controlled games | 99.0 | 96.0–101.0 | −0.99 (0.4× half-width) | P3 (in band) | Matches `BasePossessionsPerGame=98`. Measured via RNG-replication (see harness note), not from real `LeagueSession` games — the per-game seed used there is not reproducible from outside the session. | CONFIRMED (controlled matchups) / untested in real league games |
| 2 | `ortg` (72v72, both sides pooled, with home-court) | 109.23 | 115.0 | 112.0–117.0 | −5.77 (2.3× half-width) | **P1** | `BaseOffensiveEfficiency=10,800` (=108.0) is the dominant term at equal strength; +2.0 home/away average lifts it to ~109.2, ~5.8 points under target. A flat constant, not a bug, but calibrated low against the stated era target. | CONFIRMED |
| 2 | `hca_points` | 1.88 (n=20,000, 72v72) | 2.5 | 1.5–3.5 | in band | P3 (in band) | `HomeCourtEfficiencyBonus=200` (2.0 raw) nets to ~1.9 after possession variance; consistent with the constant's own doc comment ("roughly three points... about what home advantage is worth" — measures closer to two). | CONFIRMED |
| 3.1 | `sd_team_win_pct` | 0.0866 (n=6,000 team-seasons, SE≈0.0008) | 0.150 | 0.135–0.165 | −0.0634 (4.2× half-width) | **P1** | League standings are far tighter than target — see 4.4/root-cause: measured on the 6-team shipped fixture league, whose roster-quality spread may itself be modest by design (a vertical-slice content pack, not tuned for parity realism). The win-probability curve itself (Battery 6, below) is not flat, so this is at least partly a content/data-pack property, not purely an engine defect. | CONFIRMED, cause partly confounded (see note) |
| 3.1 | `noll_scully` | 1.57 | 2.70 | 2.50–3.00 | −1.13 | **P1** | Formula applied exactly as given (`sd_observed / (0.5/sqrt(82))`); this league plays 78 games, not 82 — a second-order mismatch on top of the primary win%-spread finding above. | CONFIRMED |
| 3.2 | `talent_share_of_var` | 0.593 | 0.84 | 0.80–0.88 | −0.247 (8.2× half-width) | **P1** | Same root cause as 3.1: ~41% of win-total variance is luck vs. the target's ~16%. | CONFIRMED |
| 3.3 | Player-value skew/kurtosis/Hill exponent (VORP-equivalent) | not tracked | skew 1.20, kurt 2.50, Hill 2.60 | — | n/a | **P1 (missing mechanism)** | No value/impact metric of any kind exists on a player (no VORP, no win-share analogue, no plus-minus). Cannot be computed from anything the codebase persists. | CONFIRMED |
| 3.3 (anti-pattern check) | Talent-**generator** shape (`ProspectGenerator`, n=50,000) | skew 0.0023, excess kurtosis −0.601, Hill(top 5%) ≈ 30.7 | skew 1.20, kurt 2.50, Hill 2.60 | — | large | **P1 — named anti-pattern present** | `ProspectGenerator.GenerateOverall` averages two independent uniform draws: a textbook triangular distribution (theoretical excess kurtosis of a triangular distribution is exactly −0.6, matching the measurement to 3 significant figures). Symmetric, thin-tailed, and **bounded on compact support** — there is no possible outlier past `MaximumRating`, so a Hill tail-index is close to meaningless here (reported only because the battery asks for it; the honest reading is "no tail exists"). This is exactly Battery-3.3's named anti-pattern: **Gaussian-shaped (here, triangular) talent generation** — no franchise-bending outlier is possible by construction. | CONFIRMED |
| 3.4 | `teams_60plus_wins_per_30` | 0.03 (6 raw / 6,000 team-seasons, rescaled) | 1.2 | 0.6–2.2 | −1.17 | **P1** | Consequence of 3.1/3.2: standings too tight for a truly dominant team to emerge over a season. | CONFIRMED |
| 3.4 | `teams_20orfewer_per_30` | 0.01 (2 raw / 6,000) | 1.4 | 0.7–2.5 | −1.39 | **P1** | Same cause, mirrored at the bottom of the table. | CONFIRMED |
| 3.5 | `sd_game_margin` | 17.44 (n=247,798 real games, mixed matchups) | 13.5 | 12.5–14.5 | +3.94 (3.9× half-width) | **P1** | Larger than target despite the *tighter*-than-target win% spread above — i.e. individual games are noisier than real basketball even though season-long records regress closer to .500 than real basketball. Consistent with a possession model whose make/miss draw is closer to i.i.d. Bernoulli than real shot-quality-correlated scoring runs. | CONFIRMED |
| 3.5 | `sd_team_game_score` | not tracked | 12.0 | 11.0–13.0 | n/a | **P1 (missing mechanism)** | "Game score" needs FGA/FGM/FTA/FTM/TOV/STL/BLK/PF, none of which exist (see Battery 1/2). | CONFIRMED |
| 3.6 | `rotation_size_at_90pct_mp` | 8.87 (median 9, sd 0.37, n=495,596 team-games) | 9.0 | 8.2–10.0 | in band | pass | Consistent with `MaximumRotationSize=10` and `MinimumRotationMinutes=6` in `MinutesAllocationBounds`. One of the few well-calibrated, fully-measurable numbers in the audit. | CONFIRMED |
| 4.1 | Rating correlation matrix / `expected_rating_couplings` | not computable — 1 attribute | matrix over ≥6 attributes | — | n/a | **P1 — structural, named anti-pattern** | `PlayerRating` (`src/BallGM.Domain/Players/PlayerRating.cs`) carries a single `int Overall`. Height, speed, strength, passing, lateral quickness — every attribute the six named couplings (height↔block, height↔three-point, speed↔steal, strength↔ORB, passing↔turnover) require — do not exist. Every one of the six expected couplings is "the generator draws independently" by vacuous default, because there is nothing to correlate. | CONFIRMED |
| 4.2 | First-PC variance share of ratings covariance | 100% (trivial: 1×1 "matrix") | 0.45 | 0.20–0.70 | +0.30–0.80 | **P1 — the exact named anti-pattern** | Directly the ">0.70 means every player is a scalar overall in a trench coat" case the target file itself names — except it is not merely over 0.70, it is total, because there is no second dimension to hold any remaining variance. | CONFIRMED |
| 4.3 | Usage–efficiency slope (TS% on USG%, within-player) | not tracked | −0.60 | −0.90 to −0.30 | n/a | **P1 (missing mechanism)** | Neither TS% nor USG% is a stat this codebase records anywhere (see Battery 2). | CONFIRMED |
| 4.4 | Stacking slopes (ORB%, AST%, USG%, BLK%, DRTG vs. summed on-court rates) | not directly computable — none of these rates exist per-player | 0.40–0.80 per key | — | n/a | **P1 — named anti-pattern, confirmed by source and by data** | `Side.Strength` (`PossessionMatchEngine.cs:330-335`) is *literally* `Σ(Overall[i]×Minutes[i]) / Σ(Minutes[i])` — an exact minutes-weighted **linear** aggregation of the rotation's single talent scalar, and this is the only rating that enters `EfficiencyFor` for both offense and defense. Corroborating regression: roster mean `Overall` (1,000 seasons × 6 teams = 6,000 team-seasons) vs. season win% gives slope 0.0224, R²=0.577; vs. points-for/game gives slope 0.491, R²=0.461 — a materially linear relationship, exactly what "acquire max total rating" being optimal predicts. This is Battery 4.4's named failure mode #1, present by construction, not merely by measurement. | CONFIRMED |
| 5.1–5.3 | YoY team-win correlation; split-half reliability; YoY player-stat correlations (FT%, 3P%, USG%, REB%, TS%) | not computable | — | — | n/a | **P1 (missing wiring + missing stats)** | None of FT%/3P%/USG%/TS% are tracked (Battery 2). Team continuity across seasons is separately blocked — see 5.4/5.5/5.6. | CONFIRMED |
| 5.4 | Aging curves by skill (net impact, scoring volume, 3P%, REB%, BLK% — peak ages must differ) | 1 curve total, on 1 attribute | 5 distinct peak ages | — | n/a | **P1 — structural, named anti-pattern** | `PlayerDevelopmentModel.Develop` (`src/BallGM.Rules/Development/PlayerDevelopmentModel.cs`) ages exactly one number, `PlayerRating.Overall`, against one growth curve and one decline curve. There is no per-skill breakdown to have differing peak ages — the exact case the target file calls out ("identical peaks mean one aging curve is driving every skill"), except here it is not merely identical peaks, there is only one skill to have a peak. | CONFIRMED |
| 5.5 | Rating inflation over 50 seasons | not measurable beyond ~3 seasons (see below); trivially 0.0 where measured | 0.0 | −1.0 to 1.0 | in band, vacuously | pass, for the wrong reason | `PlayerDevelopmentModel.Develop` / `RetirementModel.Assess` / `ProspectGenerator.Generate` / `DraftLottery.Run` have **zero call sites** anywhere in `BallGM.Application`, `BallGM.Infrastructure`, or `BallGM.Simulation` — confirmed by a full-repo grep. Nothing ever ages a rating, so of course it never drifts. The target is met because the mechanism does not run, not because it is calibrated. | CONFIRMED |
| 5.6 | Dynasty concentration (Gini/HHI, 50 seasons) | **run failed at season 4 of 50**: "Team ... has nobody available, so it cannot be put on the floor for this game." | Gini 0.55 (0.42–0.68) | — | n/a | **P0-adjacent structural finding** | A `LeagueSession` chained across seasons with `StartSeason→AdvanceToEndOfSeason→ConcludeSeason` releases every player whose contract expires to free agency (as designed) but nothing re-signs them — there is no automated free-agency resolution loop and no AI general manager anywhere in the codebase. Roster sizes shrink every season until a team cannot field five players. The game cannot currently be advanced through more than ~3 unattended seasons. | CONFIRMED (reproduced directly; see `dynasty.csv`) |
| 6.1 | Reliability table / Brier score / calibration slope on the engine's *implied pregame win probability* | **no such value exists** on the engine's output at all | Brier 0.205 (0.190–0.225); slope 1.00 (0.93–1.07) | — | n/a | **E (untestable as specified)** | `IMatchEngine.Play` returns only a played result, never a probability. What I measured instead (below) is the *empirical* win frequency from 4,000-game Monte Carlo replays at each rating gap — informative about the model's shape, but not a test of a stated probability against outcomes (that comparison is circular by construction and is reported separately, not as a real calibration slope). | CONFIRMED gap in the API surface |
| 6.1 (substitute) | Empirical win-probability-vs-rating-gap curve, logit-linear fit | slope 0.0565/point, intercept 0.208, R²=0.9975 (n=84,000 controlled games, 11 gap values ±40) | — | — | — | pass (well-behaved) | Smooth, monotonic, close to logistic in shape. Reported instead of 6.1 proper because no stated probability exists to calibrate against. | CONFIRMED |
| 6.2 | Logistic-scale stability across bottom/middle/top terciles of the rating range | bottom 0.0628 (R²=0.998), middle 0.0512 (R²=0.997), top 0.0664 (R²=0.996) | stable | — | ~25% spread, non-monotonic | P3 (mild, not tail-compression) | The three slopes are close and the middle is if anything *lower*, not the tails — i.e., no evidence of the tail-compression failure mode this check exists to catch, within the resolution of an 11-point grid. | CONFIRMED, low resolution |
| 6.3 | P(better team wins a 7-game series) at ≈+5 net rating | 0.70–0.86 depending on the (uncertain) net-rating→Overall-point conversion used | 0.80 | 0.74–0.86 | plausibly in band | P3, low confidence | Converted via `EfficiencyPerRatingPoint=60` → a +5 net-rating gap maps to roughly a 4–12 Overall-point gap depending on assumptions; single-game favorite win probability at gap=4 is 0.594 (series 0.698), at gap=8 is 0.654 (series 0.807), at gap=12 is 0.686 (series 0.856). The mapping, not the engine, is the source of the spread — I do not have a defensible way to pin the conversion tighter from what the codebase states. | PLAUSIBLE |
| 6.4 | P(best regular-season team wins the title) | 0.387 (n=1,000 seasons; SE≈0.015) | 0.25 | 0.18–0.32 | +0.067 (0.96× half-width) | P2 | Above band but under 2× — real, not noise (z≈8.9 SE). Plausibly a small-bracket artifact: the fixture league seeds only 2 teams per conference into a 4-team bracket, giving the favorite fewer rounds in which to be upset than a real 16-team playoff field. Not conclusively an engine defect; a larger bracket was not tested. | CONFIRMED value, PLAUSIBLE cause |
| 7.1–7.4 | $/win curve; rookie-scale surplus decay; AI cap utilization/archetype entropy; trade-valuation intransitivity | not computable — no mechanism exists | various | — | n/a | **P1 (missing mechanism, whole battery)** | No AI general manager, no automated free-agency or trade-proposal loop, and no scalar "trade value" or "surplus value" function anywhere in `BallGM.Rules.Trades`/`BallGM.Rules.Negotiations` (confirmed by grep — only legality/cap-math validators exist, e.g. `TradeValidator` checks salary matching, never compares "value"). `docs/negotiation-mechanisms.md`/CLAUDE.md record AI front offices as owed to a later milestone; this battery is entirely premature to run against this build. | CONFIRMED |

## C. ROOT-CAUSE CLUSTERING

1. **Talent is one clamped scalar (`PlayerRating.Overall`, 0–100), generated by a symmetric,
   compact-support distribution.** Drives 3.3 (generator shape), 4.1 (no attributes to correlate), 4.2
   (trivial 100% first-PC share), 4.3 (no way to separate "shot creation" from "efficiency"), and 5.4
   (one aging curve because there is one thing to age). The codebase's own doc comments on
   `PlayerRating` and in `docs/architecture.md` already flag this as the anticipated next step; the
   audit corroborates that it is not a cosmetic gap but the single largest blocker to correlation- and
   distribution-shape validation.

2. **The box score records four counting stats (PTS, REB, AST, MIN) and nothing else.** No FGA, FGM,
   3PA, 3PM, FTA, FTM, TOV, STL, BLK, or PF exists anywhere in `PlayerStatLine`/`BoxScore`, even though
   the engine internally knows makes-vs-misses and 2s-vs-3s before discarding the split. Drives 5 of 7
   Battery-1 checks, 8 of 13 Battery-2 rate stats, `sd_team_game_score` (3.5), the usage/efficiency
   slope (4.3), and every YoY per-skill player correlation (5.3).

3. **Milestone 8's own rules (`ProspectGenerator`, `DraftLottery`, `PlayerDevelopmentModel`,
   `RetirementModel`) have zero call sites outside their own definitions and unit tests.** Confirmed by
   a full-repo grep. Consequence beyond "ratings never drift" (5.5, vacuously in-band): combined with
   free agency having no automated resolution, a chained `LeagueSession` run cannot survive past season
   3–4 without a human re-signing every departing free agent — reproduced directly (`dynasty.csv`).
   This blocks essentially all of Battery 5 outright, not just the aging-curve sub-checks.

4. **No AI decision-making layer exists anywhere** — no automated free agency, no automated trades, no
   scalar trade-value or draft-surplus function. `LeagueSession` is exclusively human-driven. This is
   the sole cause of Battery 7 being entirely untestable, and is consistent with CLAUDE.md's own note
   that AI front offices are owed to a later milestone.

5. **Residual numeric miscalibration in what is actually measurable.** Independent of causes 1–4: the
   6-team shipped fixture league's win% spread, Noll-Scully, talent-share-of-variance, and extreme-team
   counts are all well below target, while game-margin SD is well above target — real, sampling-noise-
   survives findings, but confounded by this being a small "vertical slice" content pack rather than a
   tuned 30-team league; the underlying win-probability-vs-strength-gap response, isolated in a
   controlled setting, is itself smooth and reasonably sharp (R²>0.99 logistic fit), so this cluster is
   at least partly a content/tuning issue layered on top of causes 1–4, not proof the core probability
   model is broken.

## D. THE ONE THING

**Wire an automated season-boundary loop into `LeagueSession`/`SeasonEngine`** — at minimum an
automated free-agency resolution pass at `ConcludeSeason`, ideally also `ProspectGenerator` →
`DraftLottery` → roster entry and `PlayerDevelopmentModel`/`RetirementModel` per player. This is
higher leverage than the multi-attribute rating expansion (cause 1) because it is the one gap that
currently makes the game **unplayable past three seasons without manual intervention on every single
free agent, every single year** — a harder blocker than any miscalibration, since nothing else can be
validated on a league that cannot run.

Expected effect on the three worst-hit metrics: **5.6 (dynasty concentration)** goes from "run failed"
to computable at all; **5.5 (rating inflation)** goes from a vacuous pass (nothing ever runs) to an
actual test of `DevelopmentRules`' growth/decline curves against the 50-season target band; **6.4 (P
best regular-season team wins the title)** becomes testable across a league whose roster quality
actually shifts year to year via the draft lottery and player development, rather than staying frozen
except for attrition — which may itself move it further from the observed 0.387 in either direction,
since a static-talent league is a poor proxy for one with real inter-season variance in team strength.

## E. UNTESTABLE

- **True box-score categories** (FGA/FGM/3PA/3PM/FTA/FTM/TOV/STL/BLK/PF, and anything derived from
  them: eFG%, TS%, TOV%, ORB%/DRB% split, FT rate, 3PA rate, AST/FGM, PF/game, BLK/game, STL/game,
  USG% sum-to-100%, game score). **Needed**: new fields on `BallGM.Domain.Seasons.PlayerStatLine` and
  the corresponding attribution logic in `BallGM.Simulation.Seasons.PossessionMatchEngine` (which
  already computes makes/misses and 2s/3s internally in its private `Side` class — the data exists for
  one possession-loop iteration and is discarded before `ToStatLines()`).
- **Multi-attribute player ratings and their correlation structure** (height, speed, strength, passing,
  lateral quickness, block/steal/turnover tendencies). **Needed**: the multi-attribute expansion of
  `BallGM.Domain.Players.PlayerRating` that its own doc comment and `docs/architecture.md` already
  anticipate, plus a generator (in `BallGM.Rules.Draft.ProspectGenerator`) that draws attributes with
  the stated couplings rather than independently.
- **Any true "player value" metric** (VORP-equivalent for skew/kurtosis/Hill-exponent testing).
  **Needed**: some derived per-player impact statistic — none exists; `PlayerSeasonStatLine` carries
  only raw totals (`src/BallGM.Domain/Seasons/PlayerSeasonStatLine.cs`).
- **Everything in Battery 7** (economy & AI). **Needed**: an AI general-manager decision loop (roster
  construction, free-agency bidding, trade proposals) and a scalar trade/pick-value function — both
  explicitly deferred per `docs/negotiation-mechanisms.md` and CLAUDE.md to a milestone requiring
  "Milestone 9's AI front offices," which has not shipped.
- **Battery 5 beyond ~3 seasons of a chained league** (YoY correlations at scale, split-half
  reliability across many seasons, 50-season rating drift, 50-season dynasty concentration).
  **Needed**: the season-boundary wiring named in §D — without it, a chained `LeagueSession` run
  cannot be advanced far enough to gather the data the battery specifies.
- **`pace`/`ortg`/possession-derived numbers for real `LeagueSession` games** (as opposed to the
  controlled matchups reported above). **Needed**: either exposing possessions on `GameResult`/
  `BoxScore`, or exposing/replicating `SeedMixer.Mix(seasonSeed, gameId)` from outside the session so
  the harness can recover the per-game seed and replay the RNG trick described in `harness.md`.
- **6.1's literal ask** (bin the engine's *implied pregame win probability*). **Needed**: `IMatchEngine`
  (or a sibling port) would need to expose a probability before a game is played; currently it only
  returns a played result. The Monte-Carlo substitute reported above is the closest available proxy.

## Methodology notes

- Monte Carlo: controlled two-team matchups used 4,000 games per rating-gap cell (11 cells, gaps of
  −40..+40 in steps of 4, seeded via a hash of `(gap, i)`), 20,000 games for the isolated home-court
  measurement, and 50,000 draws for the talent-generator shape test. Real-league statistics used 1,000
  independent season replays of the shipped 6-team `FixtureLeagueDataSource` league (78 games/season,
  season seeds 1..1000) — **not** 30 teams × 30 seasons or a synthetic 30-team league, because the only
  league content this build ships is 6 teams; where that smallness plausibly confounds a result (3.1,
  3.2, 3.4, 6.4) it is called out in the table. All seeds are recorded in the harness CSVs
  (`scratch/audit/harness/bin/Release/net10.0/out/*.csv` at audit time; not committed — regenerate via
  `.claude/skills/sim-audit/harness.md`).
- **A labeling bug in my own analysis (not the engine) initially produced a false "win probability is
  flat" reading.** Bucketing controlled matchups by `abs(delta)` combined a "high/low" internal label
  whose meaning flips sign when `delta<0` — this averaged the true effect with its mirror image into
  ~50% at every gap. Re-derived directly from `homeRating − awayRating` on each row, the curve is
  cleanly monotonic (R²>0.99 in logit space). Recorded in `harness.md` so a future run does not repeat
  it. I flag this here per the audit's own instruction to distinguish sampling noise/bias from
  measurement error, and because a finding I could not support (the original flat curve) would have
  been the single worst, most misleading line in this report had it survived to the findings table.
- No numpy/scipy were available and the sandbox has no package-install network access; all statistics
  (mean, sd, population skew/excess-kurtosis, Pearson r, OLS, a Hill tail estimator, and a logit-OLS
  logistic fit) were implemented directly in Python against the harness's CSV output.
