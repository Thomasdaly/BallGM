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
| F3 | Re-signing, the market (§5), the rookie scale and option decisions (§7) done; restricted free agency next — contract structure: rookie scale with team options, option decisions, qualifying offers and restricted free agency with matching, incumbent rights over the cap, room allowance | |
| F4 | Exits: waivers and claim order, buyouts with consent and guardrails, stretch provision, the post-cutoff rule, the re-sign wait, apron restrictions | Buyouts and their guardrails done (§6); waivers and stretch to come |
| F5 | Trades: multi-asset with picks, trade restrictions on new signings, acceptance margins, direction-aware valuation | Summer market done (§8): one trade a team, nobody flipped, both sides must gain; picks and in-season trades to come |
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

## 5. F3 (first half) as built: keep your core, then a market

- **Re-signing window** (`OffseasonModel.ReSignings`, `IFrontOfficeAdvisor.PlanReSignings`): before a
  season is concluded, every AI team keeps the expiring players it wants and can afford — rotation
  players, and young players whose ceiling beats its fifth-best — at the player's asking price, as an
  extension (which needs no cap room: the incumbent's advantage, the generic form of the real
  agreement's veteran re-signing exception). The budget is the team's direction: a contender to the
  first apron (the second for one of its three best), a retooling team to the tax line, a rebuilding
  one to the cap and never past thirty. Terms by age: five years for a young star down to one past 31.
- **Free-agency market** (`OffseasonModel.MarketOffers`/`ResolveRound`, `PlanMarketRound`): up to eight
  rounds; each team bids on the free agents who beat its eighth man (or fill a short roster) at the
  round's price — the asking price, 12% lower each round, never below the minimum — re-checked by the
  signing validator at the price, its over-cap allowance or the minimum, and inside its budget. Each
  player takes the most money, weighted slightly toward a winning team; a team adds at most one player
  a round. Every signing executes through the signing engine.
- Boundary order is now: re-signing window → conclude (archive, awards, service, expiries) → career
  history → age and retire → draft → market → AI trades → floor fill.

Calibration league, eight chained seasons: about 100 re-signings and 15–35 market signings a summer,
**no top-30 player changing teams in any summer** (6–19 a summer before), rosters around 14, a
different champion most years, scoring leaders 30–36.

## 6. F4 (buyouts) as built

- **The rule** (`BuyoutRules`, ruleset schema 14, optional `buyouts` section): a buyout ends a contract
  with the player giving back up to `maximumGiveBackPercent` of what he is still owed; the rest stays on
  the releasing team as dead money (`Contract.BuyOut`, `RetainedPercent`, contract schema 2, save schema 8).
- **The guardrails** (`SigningValidator`): the releasing team may not sign the player again for
  `releasingTeamWaitSeasons`; a team that would finish over `highEarnerBarAbove` (the first apron in the
  shipped leagues) may not sign a player released this season or last whose salary before release was
  above the standard over-cap allowance. Both apply to every signing — the market, the floor fill and a
  human's offer screen.
- **Who buys out** (`BuyoutModel`): only a team that is not contending; only a veteran (30+) outside its
  eight best players; only one clearly overpaid (asking price under 60% of this season's salary); at
  most one a team a summer, the most overpaid first. He gives back about what he will earn elsewhere,
  never more than the cap, so consent is what the terms are built to give. Executed by
  `ISigningEngine.ExecuteBuyout` (`BuyoutExecutor`) before the market opens.

Calibration league, eight summers: 0–2 buyouts a summer, all of fringe veterans, no top-30 player
moving, and (locked by `BuyoutMarketTests`) no bought-out player back with the team that released him.
The post-cutoff rule (a player released after the playoff eligibility cutoff cannot play in the
postseason) needs in-season AI transactions, which do not exist yet: the cutoff is enforced at the
signing, and buyouts happen in the offseason.

## 7. F3 (second half) as built: the rookie scale and options

- **Rookie scale** (`RookieScaleRules`, ruleset schema 15, optional `rookieScale` section): a first-round
  pick's first-season salary is a share of the soft cap by selection (the shipped leagues: 8.5% for the
  first pick down to 1.85% at the end of the round, rising 5% a season), for four seasons of which the
  last two are the team's options; a second-round pick signs for two seasons at the minimum. Executed by
  `RookieScaleExecutor` with no signing route — a team over the cap can always sign its own pick, as the
  real agreement's rookie exception allows — but never into a full roster (`SigningRouteKind.RookieScale`
  on the ledger).
- **Who gets signed** (`OffseasonModel.SignsDraftPick`): every first-round pick with a roster spot; a
  second-round pick only if a spot is left for the market afterwards and he is at least as good as the
  worst player carried (or his ceiling beats the eighth man). The rest are free agents.
- **Options** (`OffseasonModel.OptionDecisions`, `ISigningEngine.DecideOption`): before the re-signing
  window, every option on next season is decided — a team keeps its rotation players and high-ceiling
  youngsters and declines the rest; a player takes up his option when it pays at least his asking price
  and walks away when the market would pay more. A declined option now ends the contract at the domain:
  `Contract.TermFor` and `LastSeason` stop before it, so every "is he under contract?" question agrees.

Calibration league, twelve chained seasons: 35–50 picks signed a summer, a different champion most
seasons, scoring leaders 31–35, the top of the league stable (94–97) and the tenth percentile of rostered
players stable (~80). Known follow-up: the median rostered player still drifts up (64 → 70) and the
league runs young (24.5) — the market and re-signing favour ceilings over veterans more than a real
league does.

## 8. F5 (first step) as built: a summer trade market, not a carousel

Before: 21–29 one-for-one trades a summer on the calibration league, one team in six of them, the same
player flipped two or three times. Now the boundary's AI turn is a summer market
(`LeagueSession.SummerTradeMarket`, `TradeTargetingOptions`):

- each team is part of at most one trade a summer, as either side, and nobody is moved twice;
- a trade must leave **both** teams better by their own direction — stronger as they would field the
  players (each position's best counts twice, the next three bench players once), or, for a rebuilding
  team, a player at least three years younger coming back whose ceiling is no lower than the current
  level of the one going out;
- the market takes the trade best for both first; free-agent signings are the market's job, not the
  trade turn's.

Calibration league: 3–4 trades a summer, every one between two different teams, no player flipped
(`SummerTradeMarketTests`). Picks as assets, multi-player trades and the in-season deadline market are
the next steps; the "Run AI turn" button outside a summer keeps its old one-trade-or-one-signing shape.
