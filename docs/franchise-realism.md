# Franchise realism: a multi-season save that holds up

The design of record for run 2's goal (2026-10-23): a franchise save that can be played for many
seasons and stays believable — stat lines, awards, contracts, trades and team direction — with the
current real-world top-league agreement as the reference for how player movement is constrained.
Companion to `docs/attributes-plan.md` (the engine work), `docs/negotiation-mechanisms.md` (the
signing inventory, which this does not replace) and `docs/competitive-feature-review.md`.

## 1. The low baseline: what the market leader's franchise mode gets wrong

The licensed market leader's franchise mode is the bar players compare against, and its long-save
failures are well documented by its own community. They are the floor this game has to clear, so each
is written down as a failure mode with the mechanism that prevents it here.

| Failure reported | Why it happens (as far as the reports show) | What prevents it here |
|---|---|---|
| Flat simulated stat lines: few or no 30-point scorers, leaders far below a real league's | Ratings feed one blended overall; usage is not a decision anybody makes | A ball handler who shoots or passes, usage and touches as differences from the rotation, role-based minutes; leaders locked against the real league's own bands (`LeagueShapeCalibrationTests`) |
| A league star wins the bench award year after year | Award picked from production with no role test | Award eligibility is a rule: the bench award needs more games off the bench than started, and every award needs a games-played and minutes floor (slice F2) |
| A post-buyout market where top players change teams every season | Buyouts priced as a cap device, with no consent model and no guardrails | A buyout is a negotiation the player must accept, priced against what he could earn elsewhere; the waiving team cannot re-sign him for a year; a player released after the eligibility cutoff cannot play in the postseason; teams over the first apron cannot sign a released player whose salary was above the over-cap allowance (slice F4) |
| CPU teams trade too often; half the league's rosters unrecognisable after one season | No cost to a trade, no organisational direction, no memory | Trades need a reason (direction, need, value), carry an acceptance margin, and a team's direction persists for seasons (slices F5–F6); churn measured per season (F7) |
| Stars almost impossible to acquire whatever is offered | Valuation is a cliff on overall | Convex valuation with contract and age, and a rebuilding team that genuinely sells (F5) |
| Veterans decline too sharply and all at the same age; first-round busts never happen and second-rounders never bloom | One ageing curve | Per-attribute development by age, potential and personality, with variance (slice F2, attributes A9) |
| Generated draft classes thin at the top | One distribution per class | Class strength itself varies (F2) |
| Restricted free agency mishandled: no qualifying offer still restricts, original team cannot match | Missing state machine | Qualifying offer → restricted status → offer sheet → match window, each a stated rule (F3) |
| Retirements too early; unsigned players retire after one year | Age threshold, no market check | Retirement reads age, decline, the market's interest and personality (F2) |
| Second apron has no teeth | Thresholds reported, not enforced | Already enforced here: no aggregation above the second apron, strict matching above the first (cap mechanics, schema 10) |
| History not tracked over a long save | No archive | Season history, awards and career stats archived every season (F2) |
| Only six assets per trade | Hard UI limit | Configurable caps stated as validation bounds, generous by default (F5) |

Sources for the reported failures: community write-ups and long-save reports —
[ssn-sports.com](https://www.ssn-sports.com/post/untitled),
[giancvm.substack.com](https://giancvm.substack.com/p/nba-2k-player-overalls-and-myleague),
[Operation Sports forums](https://forums.operationsports.com/forums/forum/basketball/nba-2k-basketball/nba-2k-basketball-rosters/26863851-nba-2k26-coy-roster-the-smartest-ai-ever/page2),
and the user's own report (a superstar winning the bench award repeatedly; a buyout market that moves
top players every season; sub-30 scoring leaders). The game's internals are not public; the right-hand
column is what this game does, not a claim about how the competitor works.

## 2. The reference agreement, generically

The current real-world agreement (2023, in force through 2029–30) is the reference for *what kinds* of
rules a league needs. Every mechanism is modelled generically and configured by the ruleset; nothing is
hard-coded to one league. The shipped content pack states figures in this agreement's shape so the
default league behaves like a modern one.

| Mechanism (generic name) | Reference shape | Here |
|---|---|---|
| Soft cap, tax line, first and second apron | Cap set from revenue; tax and aprons above it; cap growth limited year on year | Built (`CapThresholds`, cap mechanics schema 10) |
| Bracketed luxury tax, repeater rates | Rates rise by bracket; a repeater pays more | Built |
| Salary matching bands; strict above the first apron; no aggregation above the second | Tiered matching for teams under the apron | Built |
| Compensation ceiling by service | 25% / 30% / 35% of the cap by years of service | Built (`CompensationCeilingScale`) |
| Minimum salary scale by service | Rises with service | Built |
| Term limits | Longer for the incumbent team | Built |
| Raise limits | Larger for the incumbent team | Built (escalation limit; incumbent tier F3) |
| Over-cap allowances | A standard allowance; a reduced one for taxpayers; a room allowance | Standard and reduced built; room allowance F3 |
| Incumbent re-signing over the cap | The incumbent may exceed the cap to keep its own player after enough seasons with it | F3 |
| Rookie scale | First-round picks on a fixed scale, two guaranteed seasons then team options | F3 |
| Qualifying offer and restricted free agency | A qualifying offer keeps a young player restricted; the incumbent may match an offer sheet | F3 |
| Team and player options | Decided before the new league year | Domain shape built; the decision is F3 |
| Extensions | Limited raise over current salary; eligible windows | Built |
| Release, dead money, waivers | Released salary stays on the books; claims by reverse standings | Release built; waivers F4 |
| Stretch provision | Dead money spread over twice the remaining seasons plus one, capped share of the cap | F4 |
| Buyout | Negotiated give-back for release | F4 (with the guardrails in §1) |
| Post-cutoff release | A player released after the cutoff cannot play in the postseason | F4 (the cutoff itself is built at signing) |
| Re-signing a released player | The releasing team must wait a year | F4 |
| Apron restrictions on signing released players | Over the first apron, no released player whose salary exceeded the over-cap allowance | F4 |
| Trade restrictions on new signings | A newly signed player cannot be traded for a set period | F5 |
| Award eligibility | A games-played and minutes floor for the major awards; the bench award for players who came off the bench more than they started | F2 |
| Roster size, two-way slots | 15 standard, up to 3 auxiliary | Standard built; auxiliary deferred (M8 list) |

## 3. Slices

| Slice | Content | State |
|---|---|---|
| F1 | Engine tails: role minutes, ball handler, skill-driven shares, shooting and free-throw skill, foul drawing, injury proneness (attributes A3a–A6) | Done |
| F2 | The season boundary for a long save: development, ageing and retirement wired every season; awards with eligibility rules (games and minutes floors, bench award, rookie, most improved, defensive, all-league teams) archived with the season | Done (§4) |
| F3 | Next — contract structure: rookie scale with team options, option decisions, qualifying offers and restricted free agency with matching, incumbent rights over the cap, room allowance | |
| F4 | Exits: waivers and claim order, buyouts with consent and guardrails, stretch provision, the post-cutoff rule, the re-sign wait, apron restrictions | |
| F5 | Trades: multi-asset with picks, trade restrictions on new signings, acceptance margins, direction-aware valuation | |
| F6 | Franchise direction that persists (contend / retool / rebuild) with hysteresis, owner expectations if modelled, and AI behaviour that follows it | |
| F7 | A multi-season audit: twenty chained seasons reporting scoring leaders, award winners and their roles, star movement per season, buyouts, retirements by age, rating drift and title concentration, with seeded tests on the headline rows | |
| F8 | Surfaces: awards and history screens, transaction feed, contract options and restricted free agency in the client | |

## 4. F2 as built

- **Awards** (`SeasonAwardCalculator`, ruleset schema 13): each award states a kind and an eligibility
  floor (`minimumGamesPercent` of the season's games at `minimumMinutesPerGame`+ minutes). Most
  valuable: game score a game, a little less for missed games, plus 15 game-score hundredths per point
  of win percentage above .500. Defensive: steals, blocks and a third of a defensive board a game,
  scaled by minutes, plus team points allowed against the league. Bench: game score, **only for players
  who came off the bench in more games than they started**. Rookie: first season of service. Most
  improved: points + rebounds + assists a game against the previous season (25%+ of a season played).
  All-league and all-defensive: position-less teams of five. Every winner carries the figures that
  decided him and the eligibility count. Decided in `SeasonConclusion` before service is credited,
  archived on `SeasonHistoryEntry.Awards`, saved (save schema 7), read through
  `LeagueSession.SeasonHistory()` and `SeasonConclusionSummary.Awards`. The shipped rulesets state the
  real agreement's shape: 65 of 82 games (79%) at 20+ minutes for every award.
- **Development** (`AttributeDevelopmentModel`): the league's curve says how many points of Current
  Ability a typical player gains or loses at each age; a young player's gain scales with his drive
  (professionalism, work rate, ambition) and with his headroom to Potential Ability (12 points of
  headroom is the typical rate, up to 2.2× for a high-ceiling prospect), and never passes his potential;
  an old player's loss is softened by drive. The change is spread by career phase — legs first after
  the peak, the jump shot last, understanding still growing — and normalised so Current Ability moves
  by the decided amount. Injury proneness rises after thirty.
- **Retirement** (`RetirementModel.Assess` with context): the age odds scaled by contract status
  (unsigned ×2) and by how good he still is (top of the league half the odds of the fringe). Only free
  agents retire at the boundary; a player under contract plays it out.
- **Order at the boundary**: conclude (archive, awards, service, expiries) → career history → age and
  retire → draft → AI turn → floor fill.

Multi-season baseline (calibration league, before F3): scoring leaders 30–36 a season, sensible award
winners (the bench award to 0–30-start bench scorers), but ~330 players reach free agency every
summer and refill on one-year minimum deals, so rosters churn and 6–19 of the top 30 players change
teams a year. That is F3's problem.
