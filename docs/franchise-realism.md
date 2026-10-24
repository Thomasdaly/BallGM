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
| F3 | Re-signing, the market (§5), the rookie scale and option decisions (§7), incumbent rights, restricted free agency and rank-based asking prices (§11) done — contract structure: rookie scale with team options, option decisions, qualifying offers and restricted free agency with matching, incumbent rights over the cap, room allowance | |
| F4 | Exits: waivers and claim order, buyouts with consent and guardrails, stretch provision, the post-cutoff rule, the re-sign wait, apron restrictions | Buyouts and their guardrails done (§6); waivers and stretch to come |
| F5 | Trades: multi-asset with picks, trade restrictions on new signings, acceptance margins, direction-aware valuation | Summer market done (§8): one trade a team, nobody flipped, both sides must gain; picks and in-season trades to come |
| F6 | Franchise direction that persists (contend / retool / rebuild) with hysteresis, owner expectations if modelled, and AI behaviour that follows it | Two-season memory, ring-chasing veterans and star-weighted strength done (§9); owner expectations not modelled |
| F7 | A multi-season audit: twenty chained seasons reporting scoring leaders, award winners and their roles, star movement per season, buyouts, retirements by age, rating drift and title concentration, with seeded tests on the headline rows | Done (§9): `LongSaveRealismTests`, `scratch/calib chain` |
| F8 | Surfaces: awards and history screens, transaction feed, contract options and restricted free agency in the client | Season-end flow, review, History screen (S27) and tendencies on the profile done (§10) |

## 4. F2 as built

- **Awards** (`SeasonAwardCalculator`, ruleset schema 13): each award states a kind and an eligibility
  floor (`minimumGamesPercent` of the season's games at `minimumMinutesPerGame`+ minutes). Most
  valuable: game score a game, a little less for missed games, plus 30 game-score hundredths per point
  of win percentage above .500 (a .700 team's best player gains six points of game score: MVPs come
  from winning teams). Defensive: steals, blocks and a third of a defensive board a game,
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

## 9. F6 and F7: direction with memory, stars that carry teams, and the long save audited

- **Direction with memory** (`OffseasonModel.DirectionFor` over two seasons): a contender is a team that
  won 55% last season or over the last two together, or 62% last season alone. One bad (injury-hit)
  season does not turn a contender into a seller; one lucky one does not make a builder spend like a
  contender. Every offseason decision — re-signing, the market, buyouts, trades — reads the same direction.
- **Ring-chasing veterans** (`OffseasonModel.Choose`, 30+): winning weighs three times as much in a
  veteran's choice (a .700 team's offer counts about a quarter more than a .300 team's), so contenders
  fill benches with good veterans for less.
- **Stars carry teams** (`MatchModelBounds.StarStrengthWeightPercent`, `StarConvexityDivisor`): team
  strength weights each player's level by his share of the offence too, and points above the rotation's
  own average count extra (gap² / 12, capped at 30) — a uniform shift between teams plays exactly as
  before (the fixture league's win spread is unchanged at 0.159), but a 95 on a 70 team now carries him.
- **Prospect ceilings skewed low** (`RatingProfileGenerator.Headroom`): one draw, squared over the room —
  most prospects' ceilings sit a few points above where they are and only a few have the whole room.
  Busts are common, stars rare; this stopped the league's median player drifting upward.

**The long save, measured** (`scratch/calib chain 20`, calibration league, unattended): twenty seasons
in nine seconds; rosters 12–15; payrolls 65–103% of the cap on average with a handful of tax teams;
0–1 of the top 30 players changing teams a summer; 3–7 trades and 0–4 buyouts a summer; scoring leaders
29–37; median player stable after the first decade (64 → 67–68); best records 57–66 wins, a few repeat
champions. `LongSaveRealismTests` locks twelve seasons of it: scoring leaders averaging 30+ (no season
under 27), the bench award never to a player who started half his games, at most three of the top 30
moving in a summer, rosters in bounds, the median within six points of where it started and an average
age of 24–28.

**Still open** — competitive spread in an evolved league (win SD about 0.10 against a real league's
~0.15: the worst teams win 21–30 games, because nothing makes a rebuilding team deliberately worse),
the blocks leader, waivers and the stretch provision, picks in player-for-player trades and
in-season (deadline) moves, and the screens that show all of this.

## 10. F8 (first step): a season can end in the client

Before this, the client could start a season and play it to its last day, and then nothing: there was
no way to conclude it, so a multi-season save was impossible in the game itself. Now, when the season's
last day is played, Continue reads CONCLUDE SEASON (and the calendar shows a Conclude season button);
concluding runs the whole boundary and the calendar shows a season review — the champion, every award
with the figures behind it, the first all-league and all-defensive teams, what the summer did
(re-signings, options, buyouts, picks signed, free-agent signings, trades, retirements) and its headlines
— until Continue starts the next season. Locked by `MultiSeasonFlowTests` (headless) and
`SeasonReviewTests` (renders at the three sizes, no binding errors).

The **History** screen (League → History, S27) lists every concluded season with its champion and,
for the selected season, every award with the figures behind it and the all-league and all-defensive
teams by team. The player profile's Ratings tab now also shows the 2K-style **tendencies** the engine
plays (touches, shot, drive, post-up, shot location, pass, crash the glass, gamble, block, foul).

## 11. F3 (third part): incumbent rights, restricted free agency, salaries by rank, a real age shape

**Incumbent rights (ruleset v16, `incumbentRights`).** A team that held a player for
`minimumSeasonsWithTeam` consecutive seasons on contracts that ran their course may re-sign him over the
cap, up to the ceiling — the generic veteran re-signing exception. A release forfeits the run. It is a
signing route (`SigningRouteKind.IncumbentRights`) reported like every other: permits, refused with the
reason, or not a rule in this league.

**Restricted free agency.** A player whose expiring contract started within `restrictedServiceSeasons`
of service (a rookie deal), whom his team did not re-sign but still wants, is tendered a qualifying offer
of `qualifyingOfferPercent` of his last salary. In the market, if he accepts another team's offer, his team
matches when he is still wanted and the match keeps its payroll within the second apron; if nobody bids,
he signs the qualifying offer for one season. Shipped content: two seasons, the first contract, 125%.

**Asking prices by rank (`SalaryScale`).** An ask was a fixed curve on absolute Overall, tuned for one
pack's scale; on the generated league it under-priced everyone and league payroll sank from ~100% of the
cap to ~77% over a long save, leaving room everywhere. Asks now rank against every rostered player: the
top 2% ask the maximum for their service, the top 6% four fifths, the top 12% three fifths, a top-quarter
player about a third, the bottom 40% the minimum. Scale-invariant (a pack on its own scale gets the same
salary shape). With contenders bidding 20% (retooling 5%) over the ask, payroll holds at 91–103% of the
cap over twelve seasons with several teams in the tax.

**Depth.** A team short of a full complement (the maximum less one) signs anyone better than its weakest
player — contenders preferring veterans, rebuilding teams youth. Rosters settle at ~400–420 players.

**Draft ages.** Every prospect entered at exactly the youngest age, so the league skewed young (40% aged
19–22, average 24.7). Prospects are now drawn up to four years older (30/25/20/15/10%), and an older one
carries two rating points of polish per year with a ceiling nearer to it. Rostered ages after twelve
seasons: 22/35/26/13/2% in 19–22/23–26/27–30/31–34/35+ (real ~18/35/28/15/4), average ~26.

## 12. F5 (second step): salary dumps, and a pick window that rolls forward

**Salary dumps (`SalaryDumpModel`).** A team over the luxury tax pays a rebuilding team with cap room, in
draft picks, to take an overpaid contract: the taxpayer saves the salary and the tax on it, the rebuilding
team spends room it had no good use for and is paid in the currency it needs. The price is the overpay —
what the contract pays above the player's asking price, over every season left — at 12% of the cap of
overpay per first-round pick and 2% per second; a contract that would take more than two firsts does not
move. A team pays at most twice the tax the dump avoids this season (150% of the payroll it takes back
under the line), so a team a hair over the tax does not throw firsts at it. A team's five best players are
never dumped. Picks go latest first, spaced so a league's retained-round rule is never broken; a
taxpayer at its roster minimum takes a cheap end-of-bench player back. Every dump is re-validated by the
trade engine. It runs twice a summer — before the market, while rebuilding teams still have room, and
after it, once rosters are filled — with one dump a team. Measured: 0–2 a summer on the calibration
league, every one priced by its overpay, e.g. a contender $11.9M over the tax sending a $29.6M-a-season
role player through 2038 with a first and a second.

**The pick window rolls forward.** Future picks were registered only when a league was loaded, so after
the tradable horizon a long save had no future picks to trade at all (and dumps, trades and the board went
quiet). The draft now registers whatever the horizon newly reaches each season
(`FixtureDraftAssets.ExtendHorizon`), leaving existing picks — owned, traded or encumbered — untouched.
`LongSaveRealismTests` checks every franchise still has a pick in the last draft of the window after
twelve seasons.

The season review lists qualifying offers and salary dumps in its summer line and promotes dumps and
matched offer sheets to its headlines.

## 13. F6 (second step): losing teams sell their veterans

**Veteran sales (`VeteranSaleModel`).** A team that won under 45% last season and is not contending sends a
veteran (28+) to a contender — one for whom he would be among its six best — for draft picks and the
salary the buyer needs to send back (players outside its top five, biggest contracts first, at most three).
The price is set by where the veteran ranks in the league: the top 5% two firsts, the top 12% a first and a
second, the top 25% a first, the top 40% two seconds; below that a contender's picks are not spent on him.
Guards against the carousel the competitor is known for: the league's 45 best players and each seller's
own best are franchise players, sold only at 32 or older; at most one of the league's stars is sold a
summer; and nobody signed this summer (a re-signing, an extension starting now, a free agent) is traded
the same summer — a team does not keep a player only to move him, and real leagues bar it. The same
just-signed rule now applies to salary dumps. Every sale is re-validated by the trade engine.

**A bug this exposed.** Every planned trade carries the league state it was drawn against, so when a
batch of plans was executed in turn, the second and later were refused as stale: dumps and sales were
quietly capped at one a summer. The session now executes one plan and plans again
(`ExecuteTradesOneAtATime`), with each pass's teams excluded from the next.

Measured on the calibration league: 2–8 veteran sales a summer (real leagues make several such trades
between the draft and the deadline), top-30 movement 0–3 a summer, and win spread up from ~0.10 to
0.09–0.125 — better, still short of the real league's ~0.15.

**Tanking (`DevelopmentMinutesPolicy`).** A team that won under 40% last season plays for the future:
its players 23 and under carry eight rating points of rotation priority, its 30-and-overs six fewer
(`AvailablePlayer.MinutesPriority`, read by `DepthChartBuilder` for who starts and the bench order — never
for how well anyone plays). No record, no change, so a league's first season and every frozen
calibration (single seasons from a fresh league) are untouched. Win spread over twelve seasons now
averages ~0.115 (0.09–0.136), worst teams 15–28 wins. The remaining gap to ~0.15 is talent concentration:
the market still spreads good players evenly; recorded as open.

The season boundary order is now: options → re-signing window → qualifying offers → conclude → career
history → age and retire → draft (rookie scale, the pick window rolls forward) → buyouts → salary dumps →
market → qualifying offers signed → salary dumps (second pass) → veteran sales → summer trades → floor fill.

## 14. Sim logic exposed by a real-data pack: usage, foul drawing, and starters

Running a private, local pack built from a real league's stats (never committed; `local-packs/`) through
the multi-season chain exposed logic the generated league had hidden:

- **Usage reads offensive level, not Overall.** The ball found a player by his Current Ability squared,
  so a great rebounder and rim protector — a great player — became his team's first option, led the
  league in scoring and won MVP. Usage now reads `PlayerRating.OffensiveLevel` (scoring at every range,
  shot creation, handling, off-ball movement, first step, weighted as Current Ability weights them).
  Team strength still reads Overall. The generated league's shape and every calibration test are
  unchanged (league-shape rows 29 → 28 of 61 at 40 seasons, within run-to-run noise; 16.4 ms a season).
- **A team's four best players always start** (`MinutesAllocationBounds.AlwaysStart`), ranked by Current
  Ability alone (a tanking team's rotation priority moves its bench, not its stars), wherever they can
  genuinely play: the starting-five search no longer brings a star off the bench because a role player
  fits a crowded position a little better — the competitor's "best player in the league wins Sixth Man"
  failure. Their bonus is ranked, so when they cannot all fit the better player keeps his place.
  Regression test: three interior bigs who all must start, tanking or not. Sixth Man now goes mostly to
  bench scorers; an ageing former star past a team's top four can still win it, as in a real league.
- **Foul drawing** stacks three multipliers (rim shot, attack tendency, level). Lowering the engine's cap
  broke the locked leader calibration, so the pack calibrates instead: the attack tendency is set from
  the real free-throw rate with a spread (8) that brings simulated free throws per 36 to 1.28× the real
  rate for the top foul-drawers and 1.13× in the middle (from 1.44× and 1.32×).
- **Team offsets** fitted to the real table are carried on every player's sheet, so they are capped at ±6
  rather than ±15: a strong team's whole roster was being inflated for years, and a good scorer on a bad
  team (26.7 points a game) rated a bench player and was bought out within two seasons.
- **The specialist-centre MVP (fixed in steps).** An efficient, foul-drawing young centre on the best
  real team won MVP in about half of five-season runs — a player who, by the owner's own judgement,
  would never win one. Four causes, four fixes: (1) usage leans on the touch tendency more than on
  level — `UsageTouchesDoublingPoints` 17 → 10, `UsageRatingDoublingPoints` 10 → 18 — which the locked
  calibrations pass and which improves the generated league's shape (29 → 31 of 61 rows); (2) a pack
  sets touches on a log scale of the real usage rate matched to that doubling (32% against 16% is twice
  the ball, not five times); (3) a sheet reaches a high Overall by amplifying the player's strengths
  before shifting everything, so a star rim-runner stays a non-shooter and a shooter's shooting reads
  99; (4) the attack tendency measures free throws beyond what the shot diet explains, since the
  engine already draws more fouls at the rim. He now scores ~25 a game (real 19.5 in fewer minutes)
  and wins 2 MVPs in 20 simulated seasons; the MVPs go to the league's real stars.
- **Still open:** an aged star who missed most of last season (injury proneness from games missed is
  not yet strong enough) can carry a thin roster to an MVP in a pack's first season.

**League packs at the current standard (pack schema 4).** The builder writes every player's full sheet
from stats (`tools/league-pack-builder/sheet.py`; see `docs/architecture.md` → "League packs"): attribute
spreads matched to the calibration league the engine was tuned on (×2 for a real league's wider skill
gaps), stamina from minutes, the attack tendency from real free-throw rate, Overall blended from
production and the sheet's own skill level with the team offset applied after. Contracts carry their
history (signed season, years with the team, rookie-deal options, partial guarantees). On the private
pack, simulated against the real season it came from, per-player correlations rose from minutes 0.638,
points 0.785, rebounds 0.768, assists 0.771 (the old five-attribute ratings on the same engine) to 0.61,
0.86, 0.83, 0.84, with the table reproduced at r 0.97; packs are written compact and load in ~47 ms.

## 15. Your franchise is yours; the payroll floor means something

**The managed franchise (`LeagueSession.ManagedTeamId`).** The session now knows which team the person
plays — the client sets it from the selected team. The summer's AI acts for every other team and never
for this one, nor with it: no re-signing, qualifying offer, buyout, free-agent bid, salary dump, veteran
sale or summer trade. What the league itself requires still happens to it — options decided, draft picks
signed on the scale, a roster below the minimum filled — because an empty roster is not a choice.
Session state, like an in-flight negotiation: not saved. `ManagedTeamTests` plays three summers and
checks nobody under contract left, nobody arrived but draft picks, and no expiring player was re-signed
on its behalf (and fails if the team is left unmanaged). Open: an interactive offseason, where the
person makes those moves themselves before the AI market runs.

**The payroll floor.** It was informational: nothing made a team spend to it, and payrolls fell to a
quarter of the cap. A team below the floor pays the shortfall in the real agreement, so it spends it: in
the market it bids for anyone near its rotation (or better than its weakest player, with a spot open)
and overpays on one-season deals — the gap spread across its open spots, at least twice the player's
price, never above his maximum salary — and it takes on salary dumps whatever its direction. The lowest
payroll in a twelve-season chain is now 75–87% of the cap (was 50–70%, once 25%). The shortfall itself
is not charged: there are no team finances yet to charge it to.
