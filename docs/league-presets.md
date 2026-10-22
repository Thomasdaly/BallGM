# League presets

The built-in ruleset packs in `data/packs/builtin.ruleset.*`, what each is modelled on, every figure
it carries, and — rule by rule — how faithfully the ruleset format can express it. The mechanics of
packs, setups and the checker are in `docs/packs.md`.

## Ground rules

- **Rules are mechanics, not branding.** A salary cap of 164,961,000 is a fact about how a league
  is run; a league's name, logo, teams and players are its expression. The presets carry the first
  and none of the second: they are named for the *shape* of league they describe ("North American
  major league"), and every setup that ships with them plays a **generated fictional league**. This
  is `CLAUDE.md` → "Safety and legal boundaries" and the content-neutrality position in
  `docs/competitive-feature-review.md`, applied to rules: configurable, generic, never hard-coded.
- **Figures are a season's published figures, not projections**, with the source below. A preset is
  versioned by the season it describes (`2026.0.0` is the 2026–27 figures); next season's figures
  are a new version, so a setup pinned to `^2026.0.0` keeps the rules it was started with.
- **Every gap is written down twice**: as a fidelity note in the pack's manifest, which the checker
  repeats in every report, and in the tables below. `approximated` means the rule is there in a
  different form; `not modelled` means the format has nowhere to put it yet, with the milestone that
  will give it one where `docs/negotiation-mechanisms.md` or `docs/roadmap.md` names one.
- **Every preset is played.** `PackFormatTests.Presets_EveryBuiltInSetupPlaysAWholeSeasonCleanly`
  runs each built-in setup through the checker, including a full season, and requires a clean
  `Stable` verdict.

Money is in each league's own currency's smallest whole unit as the ruleset format requires (US
dollars for the two North American presets, Australian dollars for the Australasian one); the format
has no currency field, and nothing in the rules converts.

## North American major league (`builtin.ruleset.na-major`, 2026–27)

The 2023 collective bargaining agreement's structure (in effect 2023–24 to 2029–30) at its
2026–27 figures. This is the deepest preset, because the format was designed around this shape of
league and because it is the league players most want to mimic.

### Cap, tax and aprons

| Rule | 2026–27 | In the preset | Status |
|---|---|---|---|
| Salary cap (soft) | 164,961,000 | `softCap` 164,961,000 | Exact |
| Minimum team salary (90% of cap) | 148,465,000 | `payrollFloor` 148,465,000 | Exact (reported; the shortfall penalty is M10) |
| Luxury tax line | 200,428,000 | `luxuryTax` 200,428,000 | Exact |
| First apron | 209,015,000 | `firstApron` 209,015,000 | Exact |
| Second apron | 221,686,000 | `secondApron` 221,686,000 | Exact |
| Tax bracket width (grows with the cap) | 6,064,000 | `capMechanics.taxBracketSize` | Exact |
| Tax rates, first four brackets | 1.00, 1.25, 3.50, 4.75 per 1 | `taxRatesPercent` [100, 125, 350, 475] | Exact |
| Repeater rates | 3.00, 3.25, 5.50, 6.75 | `repeaterTaxRatesPercent` [300, 325, 550, 675] | Exact |
| Each further bracket | +0.50 | `taxRateIncrementPercent` 50 | Exact |
| Repeater status (taxpayer in 3 of the previous 4 seasons) | — | Stated per team by a league pack (`taxRepeater`) | Approximated: not yet earned in play |
| Hard caps triggered at an apron by certain transactions | — | Allowances withdrawn above the aprons; no flat `hardCap` | Approximated |
| Second-apron sanctions (frozen future first, no cash, no sign-and-trade) | — | — | Not modelled |

### Contracts

| Rule | 2026–27 | In the preset | Status |
|---|---|---|---|
| Maximum salary by service: 0–6 / 7–9 / 10+ years | 25% / 30% / 35% of cap (41,240,250 / 49,488,300 / 57,736,350) | `compensationCeilingTiers` 0→25, 7→30, 10→35 | Exact |
| Minimum salary, eleven steps by service | 1,357,763 (0) … 3,876,529 (10+) | `compensationFloorScale`, all eleven | Exact |
| Longest contract: re-signing own player / elsewhere | 5 / 4 seasons | `maximumIncumbentContractSeasons` 5, `maximumContractSeasons` 4 | Exact |
| Annual raises: own player / elsewhere | 8% / 5% of first season | `maximumAnnualEscalationPercent` 8 | Approximated (one limit) |
| Annual decreases | up to the raise limit | `maximumAnnualDeescalationPercent` 8 | Exact for own players |
| Extensions of a current contract | — | Built (`ContractExtension`) | Modelled |
| Rookie scale; designated-player maximum criteria; veteran extension at 140% of average | — | — | Not modelled |
| Two-way and exhibit contracts (two-way salary 678,882) | — | — | Not modelled (M8 auxiliary slots) |
| 10-day contracts | 10 days | `shortTermContractDays` 10 | Exact length; the January start date is not modelled |

### Exceptions (over-the-cap allowances)

| Rule | 2026–27 | In the preset | Status |
|---|---|---|---|
| Non-taxpayer mid-level, withdrawn above the first apron | 15,044,000 | `standardOverCapAllowance`, `…UnavailableAbove` FirstApron | Exact |
| Taxpayer mid-level, withdrawn above the second apron | 6,064,000 | `capMechanics.reducedOverCapAllowance`, `…UnavailableAbove` SecondApron | Exact |
| Allowance may be split between signings | yes | `allowanceMaySplitAcrossPlayers` | Exact |
| Room exception | 9,366,000 | — | Not modelled (M9 post-room allowance) |
| Bi-annual exception | 5,477,000 | — | Not modelled (M9 periodic allowance) |
| Retention rights over a team's own free agents (full, early and non-retention) | — | — | Not modelled (M9 incumbent retention allowance) |
| Cap holds for departing free agents | — | Roster-slot holds only | Not modelled (M9 pending-departure hold) |
| Traded-player exceptions | — | — | Not modelled (M9 traded-salary credit) |
| Restricted free agency: offer sheets and matching | — | — | Not modelled (M9) |

### Trades

| Rule | 2026–27 | In the preset | Status |
|---|---|---|---|
| Matching below the first apron: up to ~8.85M out, 200% + 250,000 back; to ~35.38M, out + 9,096,000; above, 125% + 250,000 | three bands | `capMechanics.salaryMatchingBands` | Exact (band edges are where the formulas meet) |
| Matching above the first apron | 100% | `aboveFirstApronMatchPercent` 100 | Exact |
| No aggregation above the second apron | — | `secondApronBlocksAggregation` | Exact |
| No taking back extra salary above the second apron | — | `secondApronBlocksSalaryIncrease` | Exact |
| Picks tradable seven drafts ahead | 7 | `tradableFutureDraftHorizon` 7 | Exact |
| Never without a first-round pick in consecutive future drafts | — | `retainedRoundNumber` 1, `retainedRoundInterval` 2 | Exact |
| Cash in trades | 8,495,000 a season | — | Not modelled (M9) |
| Trade deadline | early February | — | Not modelled |
| Injured players | tradable, subject to the receiving team's physical | `AllowedWithWarning` | Approximated |

### Rosters, calendar, schedule, postseason

| Rule | Real | In the preset | Status |
|---|---|---|---|
| Roster | 14–15 in season (13 for up to two weeks), up to 21 in the offseason, plus 3 two-way | 14–15 | Approximated |
| Regular season | 82 games, late October to mid April | 82 games over 174 days after a 21-day preseason | Exact count |
| Schedule weighting | 4 v division, 3 or 4 v conference, 2 v other conference | Balanced round robin | Approximated: one weighting per group cannot reach 82 in a 30-team league |
| Postseason | 16 teams (8 per conference), four best-of-seven rounds, 2-2-1-1-1 | Same | Exact |
| Play-in tournament (seeds 7–10) | yes | — | Not modelled |
| Postseason eligibility for waived players | waived by 1 March | `playoffEligibilityCutoffDay` 152 | Exact (a warning at signing) |
| Tie-breaks | head-to-head, division leader, division record, conference record, records v playoff teams, point differential | head-to-head, division record, conference record, point differential | Approximated |
| In-season cup | yes | — | Not modelled |
| Draft | 2 rounds; 14-team lottery at 14%, 14%, 14%, 12.5% … 0.5%; top four drawn | 2 rounds; the fourteen real weights | Approximated: every lottery slot is drawn by weight, not only the top four |
| Game rules | 6 fouls; bonus on the 5th team foul a quarter, 4th in overtime | `gameRules` 6 / 4 / 3 | Exact |
| Awards | voted | Scoring, rebounding and assists titles | Approximated (voted awards need M9 AI voters) |

## North American women's league (`builtin.ruleset.na-womens`, 2026)

The 2026 agreement (2026–2032): a **hard-capped** league — cap 7,000,000 with no exception above it,
expressed as `softCap` equal to `hardCap`, no over-cap allowance and no salary matching (the hard cap
governs every trade). Maximum 1,400,000 (20% of the cap, one tier); minimums 270,000 to 300,000 by
service (stepped once, at three seasons — approximated); 12-player rosters; 44 games; eight playoff
teams seeded league-wide, playing best-of-three, best-of-five, best-of-seven; three draft rounds with
a four-team lottery at 44.2 / 27.6 / 17.8 / 10.4%. Not modelled: the **40-minute game** (the match
engine plays 48), the two developmental roster spots outside the cap, lottery odds from two seasons'
combined record (this season's only), revenue-linked cap growth, core designation, the in-season cup.
Approximated: one home-court pattern for every round (the first round's 1-1-1 becomes 2-2-1-1-1), and
a draft-retention rule this build requires wherever there is a draft (the loosest one: one first-round
pick kept across the four tradable drafts). The bundled setup's fictional league is one flat table of
fifteen, because seeding is league-wide.

## European continental club competition (`builtin.ruleset.euro-continental`, 2025–26)

Twenty clubs in a 38-round double round robin; top eight to best-of-five quarter-finals (2-2-1); a
Final Four of single games. **No salary cap of any kind and no draft** — every cap field and the draft
are absent, which loads as an open market (`docs/negotiation-mechanisms.md` → "Ruleset genericity").
FIBA game rules: five fouls, the bonus from the fifth team foul. Not modelled: the 40-minute game; the
play-in for places 7–10; financial stability and fair-play rules; squad registration and nationality
rules; the domestic league alongside. Approximated: the Final Four's neutral venue (the higher seed is
home); overtime team fouls (FIBA carries the fourth quarter's count over).

## Iberian domestic league (`builtin.ruleset.iberian-domestic`, 2025–26)

Eighteen clubs, a 34-round double round robin, eight-team playoffs (best of three, then best of five,
final best of five, fixed bracket); no cap, no draft, FIBA rules. Not modelled: **promotion and
relegation** (league membership as an event is a recorded architectural change in
`docs/architecture.md`, not yet built), the homegrown-player quota, the domestic cup and super cup, the
40-minute game. Approximated: home advantage alternating game by game (1-1-1-1-1) in every round.

## Australasian league (`builtin.ruleset.australasian`, 2026–27)

Ten clubs; soft cap 2,167,359 and floor 1,950,643 (Australian dollars), with tax owed on any spend
above the cap (`luxuryTax` equal to `softCap`; no rate is stated, so the bill is reported rather than
charged — approximated); three-season maximum term; top four to best-of-three semi-finals and a
best-of-five grand final; FIBA rules; no draft. Approximated: **36 games rather than 33**, because ten
teams cannot share 33 games evenly between nine opponents; no play-in for places 3–6. Not modelled:
marquee-player cap discounts (the first marquee counts 267,560.09), the combined import and marquee
limit, development and next-star contracts, the 40-minute game.

## What the presets asked of the format

Writing the presets is the cheapest honest measurement of the format's genericity, the same reason
`data/rulesets/conformance` exists. The measurements, worst first:

1. **Game length is fixed at 48 minutes.** Four of five presets are 40-minute leagues. The largest
   single gap; it is a match-engine change with recalibration, not a ruleset field.
2. **No play-in.** Three presets want one.
3. **No promotion and relegation.** The defining feature of most leagues outside North America.
4. **One raise limit, one home-court pattern, one weighting per opponent group.** Each forced an
   approximation in a preset that is otherwise exact.
5. **A draft requires a retained-round rule**, which forced the women's preset to invent the loosest
   one.
6. **Single-table postseasons were broken** at the cap ledger — fixed, see `docs/packs.md` → "Found by
   the checker on its first run".

## Sources (read 2026-10-22)

- 2026–27 cap, tax, aprons, floor, maxima, exceptions, two-way, trade cash, matching thresholds:
  [Hoops Rumors, "Salary Cap, Tax Line Set For 2026/27 NBA Season"](https://www.hoopsrumors.com/2026/06/salary-cap-tax-line-set-for-2026-27-nba-season.html);
  [Spotrac](https://www.spotrac.com/news/_/id/3413/nba-officially-sets-2026-27-salary-cap-at-164961-million);
  [NBA.com](https://www.nba.com/news/nba-salary-cap-2026-27-season).
- Exceptions and their later-season values:
  [Hoops Rumors, "Values Of 2026/27 Mid-Level, Bi-Annual Exceptions"](https://www.hoopsrumors.com/2026/07/values-of-2026-27-mid-level-bi-annual-exceptions.html).
- Minimum salary scale: [Yardbarker, "NBA minimum salaries for 2026/27"](https://www.yardbarker.com/nba/articles/nba_minimum_salaries_for_202627/s1_14822_44016395).
- Tax brackets and rates: [Hoops Rumors Glossary: Luxury Tax Penalties](https://www.hoopsrumors.com/2026/08/hoops-rumors-glossary-luxury-tax-penalties-5.html);
  the change from the 2023–25 rates: [Salary Swish tax tracker](https://www.salaryswish.com/luxury-tax/2025).
- Salary-matching bands: [Over the Apron](https://overtheapron.com/terms/salary-matching).
- Women's league 2026 agreement: [WNBA.com](https://www.wnba.com/news/wnba-wnbpa-tentative-cba-deal-2026);
  [ESPN](https://www.espn.com/wnba/story/_/id/48243808/wnba-cba-2026-collective-bargaining-agreement-news-line-salaries-schedule);
  [CBS Sports on schedule length](https://www.cbssports.com/wnba/news/wnba-regular-season-50-games-2027-schedule-salaries-records/).
- Continental competition format: [EuroLeague competition format 2025–26](https://www.euroleaguebasketball.net/euroleague/news/format-el/).
- Iberian league format: [2026 ACB Playoffs](https://en.wikipedia.org/wiki/2026_ACB_Playoffs); [Liga ACB](https://en.wikipedia.org/wiki/Liga_ACB).
- Australasian cap and marquee figures: [ESPN, "NBL salary cap rises 7%"](https://www.espn.com/nbl/story/_/id/48487342/nbl-salary-cap-rises-7-five-teams-overspend).

The tax rates are the least certain figure here: the source's own tables disagree with its totals for
the repeater schedule, and the 2025–26 change to the first two brackets is reported second-hand. They
are a preset field, so correcting them is a one-line change to `ruleset.json` and a patch version.
