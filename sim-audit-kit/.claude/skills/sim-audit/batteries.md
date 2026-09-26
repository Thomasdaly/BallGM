# Battery specification

Numeric targets live in `targets.json`. This file defines what to compute.
Run only the batteries named in `$ARGUMENTS`; `all` runs 1-7.

---

## Battery 1 — Conservation & integrity

Exact checks. Any nonzero violation rate is P0 and halts the rest of the audit.

| ID | Check |
|---|---|
| 1.1 | `PTS == 2*(FGM-3PM) + 3*3PM + FTM` per player, per team, per game |
| 1.2 | `sum(player X) == team X` for every counting stat |
| 1.3 | `AST <= FGM` per player; team `AST/FGM` inside band |
| 1.4 | Rebound conservation: `ORB_team + DRB_opp == (FGA-FGM) + (FTA_last - FTM_last) + team_rebounds`. No leaked or duplicated boards. |
| 1.5 | Possession parity: `abs(POSS_home - POSS_away) <= 1` in 100% of games |
| 1.6 | `sum(player MP) == 5 * 48 * (1 + OT_fraction)` exactly |
| 1.7 | `sum(player USG%) == 100%` per lineup-stint |

Report the violation *rate* per check, not a pass/fail. A 0.02% rate is a real bug
with a narrow trigger and is more informative than "fail".

## Battery 2 — League aggregates

Compute the simulated mean for every key under `rate_stats`. Report sim value,
target, band, and the z-score of the sim mean against the band half-width.

## Battery 3 — Distribution shape

Where most sims actually fail. Compute every key under `distribution_shape`.

- 3.1 SD of team win%; derive Noll-Scully with the formula in targets.json
- 3.2 Variance decomposition: `var_talent = var_observed - 0.25/82`; report talent share
- 3.3 Player value (VORP-equivalent): skewness, excess kurtosis, and a Hill
  estimator of the Pareto tail exponent on the top 5%. State explicitly whether
  the top end is Gaussian.
- 3.4 Count of 60+ win and 20-or-fewer-win teams per 30 team-seasons
- 3.5 SD of single-team game score and of game margin
- 3.6 Rotation size where cumulative MP reaches 90%

## Battery 4 — Correlation structure

- 4.1 Full correlation matrix of generated player ratings. Cross-check against
  `expected_rating_couplings` in targets.json and flag any listed pair that the
  generator draws independently.
- 4.2 Eigenvalue spectrum of the ratings covariance matrix; report the first PC's
  variance share.
- 4.3 Usage-efficiency slope: regress TS% on USG% within-player across seasons.
  A zero or positive slope means shot creation is free.
- 4.4 **Diminishing returns.** Regress team rate on the sum of the five on-court
  players' individual rates, for ORB%, AST%, USG%, BLK%, and defensive rating.
  Report each slope against `stacking_slope_*`. Read the note in targets.json
  before interpreting.

## Battery 5 — Temporal dynamics

- 5.1 Year-over-year team win correlation
- 5.2 Split-half reliability of team win%: games until r = 0.5
- 5.3 Player stat YoY correlations for every key under `temporal.yoy_player`
- 5.4 Aging curves **by skill**, not by overall. Report the peak age for each key
  under `temporal.peak_age` and confirm they differ. State whether the curves were
  fit on a survivorship-biased panel, and if so, refit with an
  attrition-corrected estimator before reporting.
- 5.5 Rating inflation: 50 seasons, league mean overall by season, report the
  OLS slope
- 5.6 Dynasty concentration: Gini and HHI of titles over 50 seasons

## Battery 6 — Probabilistic calibration

- 6.1 Bin the engine's implied pregame win probability into deciles. Produce a
  reliability table, Brier score, and calibration slope.
- 6.2 Fit Bradley-Terry / Elo to sim results. Check the logistic scale parameter
  is stable across the rating range — report it separately for the bottom, middle,
  and top terciles to catch tail compression.
- 6.3 P(better team wins a 7-game series) as a function of net-rating gap
- 6.4 P(best regular-season team wins the title)

## Battery 7 — Economy & AI

- 7.1 $/win curve. Fit linear, then quadratic. Confirm a star premium exists.
- 7.2 Rookie-scale surplus value by draft pick; test power-law vs linear decay
- 7.3 AI free agency: cap space utilization, overpay rate, roster-archetype entropy
- 7.4 Trade valuation intransitivity on 10k random triples

---

## Report format

Write exactly one file. No preamble, no code fixes — diagnosis only.

**A. VERDICT** — one line: `ship-blocking` | `structurally-sound-but-miscalibrated` | `calibrated`

**B. FINDINGS TABLE**

| ID | Test | Sim value | Target | Band | Delta | Severity | Root-cause hypothesis | Confidence |

**C. ROOT-CAUSE CLUSTERING** — collapse the findings into at most 5 underlying
causes. Most symptom lists reduce to 2-3 real bugs. Do the collapse; do not skip
this section because the table looks complete.

**D. THE ONE THING** — the single highest-leverage change and its expected effect
on the three worst metrics.

**E. UNTESTABLE** — what could not be evaluated from the code as written, and the
specific file, function, or output needed to evaluate it.
