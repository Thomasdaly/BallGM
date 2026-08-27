# Negotiation mechanisms

Scope decision record for Milestone 6 (contract negotiation and free agency).

Real-world basketball agreements contain roughly two dozen distinct signing mechanisms. Most
management games either hardcode one league's current set or discover the list one bug at a
time. This document enumerates them once, in generic terms, and states for each whether
Milestone 6 builds it, a later milestone builds it, or we decline it outright — so that
"deferred" is a recorded decision with a reason rather than an omission someone finds later.

Read this alongside `docs/domain-language.md` (Contract, Option, Cap charge, Threshold) and
`docs/roadmap.md` (Milestone 6). It does not replace either: names that survive into code get
added to the domain language when they do.

`docs/competitive-feature-review.md` is the companion document: same method, applied to a
competitor's published franchise-mode feature set rather than to league agreements. It adds four
signing-adjacent mechanisms this inventory did not have — a **post-buyout market** (M9),
**short-term contracts** (M7), **cash as a tradeable asset** (M9), and **player signing demands**
(M9) — and reverses one verdict below. Where the two documents disagree on a mechanism this one
already covers, **this one wins**.

## Naming rule

Every mechanism below is named for **what it does**, not for what any real agreement calls it.
This is not only a legal boundary (`CLAUDE.md` → safety and legal boundaries); it is a design
constraint that keeps the ruleset honest. A field called `midLevelException` is a field that
only one league can ever use. A field called `standardOverCapAllowance` is configuration.

Where a mechanism is genuinely universal — a salary floor, a maximum contract length — the
generic name is also the obvious one, and that is a good sign.

## Verdict key

- **M6a** / **M6b** — Milestone 6 ships in two halves, with a checkpoint between them. **6a** is offer legality, the signing routes, roster-slot holds, and an offer screen: a GM can sign an uncontested free agent, or be refused and told exactly which line they crossed. **6b** is the market: the `Negotiation` state machine, the decomposed `PlayerPreference`, seeded tie-breaking, simultaneity and resolution ordering, competing offers, and the free-agency board. 6a is bounded and mechanical against machinery that already exists three times over; 6b holds both genuinely open design questions and all the new UI, and merging them would leave the half most likely to balloon with nothing to balloon against. **Both halves are now shipped.**
- **M6** — built in Milestone 6 (used below where the half does not matter).
- **M7**–**M12** — named now, built in the stated milestone, because it depends on machinery
  that milestone introduces (a calendar, a draft class, an AI front office).
- **Declined** — we do not intend to build it. Modelling it would add rule surface that no
  player decision depends on, or it encodes one specific league's arbitrage so tightly that a
  generic version is meaningless.

Each entry also states where it would live: a **ruleset field** (configuration, no code
change), a **Rules service** (algorithm in `BallGM.Rules`), or **Domain** (a change to an
aggregate).

---

## 1. Offer legality — is this contract a legal thing to write down?

These constrain the *shape* of an offer, independent of whether the team can afford it. M6
needs all of them, because without them the offer screen accepts contracts no league would
permit and the negotiation model is being tested against nonsense.

| Mechanism | What it does | Verdict | Lives in |
|---|---|---|---|
| **Term limit** | Maximum seasons a contract may cover; may differ for an incumbent team | **Built (M6a)** | `NegotiationRules.MaximumContractSeasons` / `MaximumIncumbentContractSeasons` |
| **Escalation limit** | Maximum season-over-season raise (and drop), as a percentage of the first season | **Built (M6a)** | `NegotiationRules` + `OfferLegality` |
| **Compensation ceiling** | Highest per-season salary any one player may be paid, usually as a share of the soft cap | **Built (M6a)** | `CompensationCeilingScale` |
| **Tenure ceiling tiers** | Compensation ceiling rising with years of service, so veterans can be paid more than rookies | **Built (M6a)** | `CompensationCeilingScale` over `BandedScale` |
| **Compensation floor scale** | Minimum per-season salary, also scaling with service | **Built (M6a)** | `CompensationFloorScale` over `BandedScale` |
| **Guarantee structure** | Which part of each season's compensation survives a release | Built (M3) | Domain — `ContractSeasonTerm.GuaranteedAmount` |
| **Options** | A season one party may decline | Built (M3) | Domain — `ContractOption` |
| **Signing bonus** | Money paid up front, amortised across the contract's cap charges | M10 | Domain + `CapChargeProjection` |
| **Movement consent clause** | Player's right to veto a trade | M9 | Domain + `TradeValidator` |
| **Trade bonus** | Extra compensation triggered by being traded | Declined | — |
| **Performance escalators** | Compensation conditional on statistical or team outcomes, classified by likelihood | ~~Declined~~ **M10** | Domain + `CapChargeProjection` — reversed, see below |

**Reversal, recorded rather than silently edited.** Performance escalators were declined here on
the grounds that they are compensation trivia no player decision depends on. That reasoning fails
once likelihood classification is part of the mechanic: an escalator classified *likely* is charged
against the cap now and reconciled later, so a team is buying cap relief today against a risk it
takes on. That is a roster-building decision, and it is the same shape as signing-bonus
amortisation, already M10. Full reasoning in `docs/competitive-feature-review.md` → "Reversals".

Note on tier tables: **tenure ceiling tiers** and **compensation floor scale** are the first
ruleset content that is a *table* rather than a scalar. That is deliberate — a league whose
minimum salary does not vary by service is a league where every veteran signs for the rookie
minimum, and the free-agency market stops meaning anything. Get the table shape right in M6;
the draft-slot scale (M8) and the tax-bracket table (M10) will reuse it.

## 2. Signing routes — how does a team pay for this player?

A signing is legal only if some route permits it. This is the mechanism family that balloons:
real agreements carry six to ten routes, each with its own eligibility. Milestone 6 builds
three and names the rest.

| Mechanism | What it does | Verdict | Lives in |
|---|---|---|---|
| **Cap room** | Sign anyone up to the gap between payroll and the soft cap | **Built (M6a)** | `SigningRouteTable` (uses `CapLedger`) |
| **Minimum-salary signing** | Always available regardless of payroll, at the compensation floor | **Built (M6a)** | `SigningRouteTable` |
| **Standard over-cap allowance** | One generically named fixed-size allowance usable above the soft cap; may be split across players; unavailable above a configured threshold | **Built (M6a)** | `NegotiationRules` + `SigningRouteTable` |
| **Incumbent retention allowance** | Re-sign your own player above the cap, up to the compensation ceiling, if they have accrued enough continuous service with you | M9 | Ruleset field + Rules service |
| **Retention tiers** | Partial versions of the above for shorter service — typically a percentage of the ceiling, or a multiple of prior salary | M9 | Ruleset field (tier table) |
| **Post-room allowance** | Smaller allowance available to a team that has already spent its cap room | M9 | Ruleset field |
| **Periodic allowance** | Allowance usable only every N seasons | M9 | Ruleset field |
| **Replacement allowance** | Allowance granted when a player suffers a season-ending injury | M8 | Rules service (needs injuries) |
| **Traded-salary credit** | Credit created by sending out more salary than you took back, spendable later | M9 | Domain (a new asset) + `TradeExecutor` |
| **Signed-then-traded movement** | Sign a player specifically to trade them, under stricter matching | Declined | — |

**A fourth route, and why it is not the exception zoo reopening.** `UnrestrictedSigning` exists only in a league that configures no soft cap. It is the degenerate case rather than a new kind of eligibility: with no line to be measured against, no amount is restricted, and reporting that as a route which *permits* is what a GM in such a league needs told. Reporting it as the absence of a refusal would leave the offer screen silent about the only rule that applies — roster space.

**Why the split.** M6's three routes are the minimum set where the free-agency market has real
texture: a team with room can outbid, a team with only the allowance must sell something other
than money, and a team with neither can still fill a roster. Every deferred route is a
*variation on eligibility*, not a new kind of thing — they all resolve to "this team may commit
this much to this player". Build the route abstraction in M6 so M9 adds table rows, not
branches.

**Constraint on the abstraction:** a signing route must return the *reason* it permitted or
refused the signing as a rule code, in the same `DomainOperationResult` shape everything else
uses. "You cannot afford this" is not an explanation; "your payroll is above the soft cap and
your standard allowance has $2.1m left" is.

## 3. Retention and competition — who gets to keep the player?

| Mechanism | What it does | Verdict | Lives in |
|---|---|---|---|
| **Roster-slot hold** | A placeholder charge for an unfilled roster spot, so a team cannot count phantom room | **Built (M6a)** | `CapChargeKind.RosterSlotHold` + `RosterSlotHoldProjection` (Rules) |
| **Pending-departure hold** | A charge for your own expiring player while you still hold retention rights over them | M9 | Domain — with retention allowance |
| **Releasing a hold** | Giving up retention rights to clear the hold and gain room | M9 | Rules service |
| **Retention offer** | A standing offer to your own expiring player that makes them contestable rather than free | M9 | Domain — `Negotiation` variant |
| **Matching right** | Incumbent's right to match a rival's signed offer and keep the player | M9 | Rules service + `Negotiation` state |
| **Competing offer sheet** | The rival offer a matching right is exercised against | M9 | Domain |
| **Agreement moratorium** | A window where terms are agreed but not signable, so the market resolves as a batch | **Built (M6b)** — configured in 6a, consumed in 6b | `NegotiationRules.MarketResolution` + `FreeAgencyMarketResolver` |
| **Prior-salary arbitrage rules** | Adjustments preventing a just-signed contract being used as matching salary | Declined | — |

**Roster-slot hold is in M6a for one reason:** without it, a team with eight players and $30m of
room appears able to spend all $30m on one player, then discovers it cannot fill the roster to
the minimum. That is a trap the UI would set for the player and the AI would walk into. It is a
small piece of `CapChargeProjection` and it belongs with the first signing.

**Simultaneity — settled, and built as this document proposed.** The prompt for this milestone
flagged market resolution ordering as the decision most likely to expand scope. This document's
position was that the **agreement moratorium** is the mechanism that makes it a rule rather than an
accident, and that is what shipped: offers accumulate, the market resolves at an explicit point, and
within a resolution point offers are ordered by a stated key rather than by arrival. `Immediate`
exists as the other mode, is honestly labelled as arrival-order dependent, and reports itself as a
note so a GM in such a league knows why their better late offer was never weighed.

**The stated key is `(TeamId, OfferId)`, ordinal ascending.** Both are `SortableId`s, so the order is
stable across runs and platforms and owes nothing to submission order. The reasoning, and the
non-transitivity consequence that stopped it being a sort comparator, are in `docs/architecture.md` →
"The free-agency market".

## 4. Roster structure

| Mechanism | What it does | Verdict | Lives in |
|---|---|---|---|
| **Payroll floor** | Minimum total payroll a team must reach, with a penalty for missing it | **Built (schema v4)** — reporting only; the penalty stays M10 | `CapThresholds.PayrollFloor`, `CapThresholdKind.PayrollFloor` |
| **Auxiliary roster slots** | Slots outside the main roster limit, with capped compensation and service limits | M8 | `RosterSizeLimits` + Domain |
| **Hardship replacement** | Temporary over-limit signing when injuries drop a team below the minimum | M8 | Rules service (needs injuries) |
| **In-season signing window** | Dates outside which signings are barred | M7 | Ruleset field (needs a calendar) |
| **Playoff eligibility cutoff** | Date after which a signed player cannot appear in the postseason | M7 | Ruleset field |

## 5. Exit and residue

| Mechanism | What it does | Verdict | Lives in |
|---|---|---|---|
| **Release / dead money** | Guaranteed money owed to a released player | Built (M3) | `Contract.Terminate`, `CapCharge.DeadMoney` |
| **Waiver claim order** | Which team may claim a released player first | M7 | Rules service (needs standings) |
| **Guarantee amortisation** | Spreading a released player's dead money over more seasons at a lower annual charge | M10 | Domain + `CapChargeProjection` |
| **Contract buyout** | Negotiated reduction of guaranteed money in exchange for release | M9 | `Negotiation` variant |
| **Retirement mid-contract** | What happens to the remaining money | M8 | Domain |

---

## What Milestone 6 ships

The minimum coherent set, restated as one list, and split across the two halves.

**Milestone 6a — shipped.**

1. Offer legality: term limit (with a longer incumbent limit where a league sets one), escalation
   and de-escalation limits, compensation ceiling with tenure tiers, compensation floor scale.
2. Signing routes: cap room, minimum salary, one standard over-cap allowance — plus the degenerate
   unrestricted route for a league with no soft cap.
3. Roster-slot holds, so room is real room.
4. Payroll floor as a threshold (reporting only — the penalty is M10). A signing that leaves a team
   still under it is a *warning*, not a refusal.
5. An offer screen: pick a team and an unsigned player, set the terms, and see every route's
   verdict with the figure behind it before signing.
6. Conformance: free agency in `data/rulesets/conformance/uncapped-open-league.json`, where any
   team may pay anyone anything and every other route reports "this league has no such line".

**Milestone 6b — shipped.**

1. The `Negotiation` aggregate: an ordered history of offers, counteroffers, withdrawals and
   expiries, and the four states that history leaves it in — open, resolved on an offer, signed, or
   closed with nobody. Every transition is rule-checked, including on load.
2. Preference decomposed per factor — money, term, team fit, market demand — each reporting its own
   reading, rule code and sentence, and **never summed**. Ranking compares factor by factor with a
   materiality band per factor.
3. `MarketResolution` consumed rather than merely configured, with the ordering key stated above.
4. Competing offers resolved together at one point, each re-checked through the same
   `SigningValidator` an offer screen uses, so an offer that stopped being legal loses on a rule code.
5. Seeded tie-breaking through `IRandomSource`, and **only** where the comparison reports that no
   factor separates the leaders. Surfaced on the assessment rather than hidden.
6. Offer expiry, measured in `SeasonDay` — an index rather than a date, because there is no calendar
   until Milestone 7 and a save must not age when a wall clock does.
7. The positional free-agency board: one column per position against the team's own depth, best
   available per slot, with our offer and their counter on each candidate.
8. A save round trip of an in-flight negotiation, at `NegotiationEnvelope` schema version 1, loaded
   by replaying the history through the aggregate rather than by assigning fields.

Three decisions from 6b worth not re-deriving. **A counteroffer is a new `Offer` in the history
authored by the player, not a state transition** — the negotiation stays open, nothing is accepted,
and a team that likes the counter answers it by offering again. **There is no overall preference
score anywhere in the model**, and a weighted total that is merely displayed decomposed was
considered and rejected: it cannot answer "which factor beat me". **An in-flight negotiation is
session state, not league state**, so it is not on `LeagueSnapshot` and it carries its own save
schema version independent of the ruleset's.

Everything else above is named, dated, and out.

## Ruleset changes this implies

New `NegotiationRules` section alongside `TradeRules` and `DraftRules`, and one addition to
`CapThresholds`. **As shipped** — the sketch survived, with `compensationFloorScale`'s rows keyed on
`amount` and the ceiling's on `percentOfSoftCap`, because a shared row type would have to name its
key something wrong for two of the three tables that will use the primitive:

```json
{
  "payrollFloor": 127000000,

  "maximumContractSeasons": 5,
  "maximumIncumbentContractSeasons": 6,
  "maximumAnnualEscalationPercent": 8,
  "maximumAnnualDeescalationPercent": 8,

  "compensationCeilingTiers": [
    { "minimumSeasonsOfService": 0, "percentOfSoftCap": 25 },
    { "minimumSeasonsOfService": 7, "percentOfSoftCap": 30 },
    { "minimumSeasonsOfService": 10, "percentOfSoftCap": 35 }
  ],
  "compensationFloorScale": [
    { "minimumSeasonsOfService": 0, "amount": 1150000 },
    { "minimumSeasonsOfService": 3, "amount": 2100000 },
    { "minimumSeasonsOfService": 10, "amount": 3300000 }
  ],

  "standardOverCapAllowance": 12800000,
  "standardOverCapAllowanceUnavailableAbove": "FirstApron",
  "allowanceMaySplitAcrossPlayers": true,

  "marketResolution": "ResolutionPoint",
  "offerExpirySeasonDays": 3
}
```

Two consequences worth stating before code is written:

- ~~**`payrollFloor` breaks `CapThresholds.Create`'s ordering check.**~~ **Done.** The floor is a
  new first link: the chain is now `payrollFloor ≤ softCap ≤ luxuryTax ≤ firstApron ≤ secondApron ≤
  hardCap`, checked over the thresholds that are *present* in that fixed sequence.
- ~~**This is `schemaVersion` 4**~~ **— shipped, with sign-off.** The version constant moved as part
  of the prerequisite task that closed the four genericity gaps below, so Milestone 6's remaining
  ruleset additions land inside version 4 rather than forcing a second breaking change.

**The negotiation section is `schemaVersion` 5, and that needed its own sign-off.** The prerequisite
left open whether `NegotiationRules` could be optional-by-absence inside version 4. Two questions,
answered separately:

*Is "no negotiation rules configured" coherent?* Yes, and it means an **open market**: no term limit,
no escalation limit, no maximum salary, no league minimum, no over-the-cap allowance (and none
needed, because a league with no soft cap has no cap to be over), and offers that never expire. It
does **not** mean "no signings at all" — signing is a capability, and it is the routes that gate it.
That league is exactly `uncapped-open-league.json`, and the conformance tests now assert it. So
absence is expressible, and nothing about version 4's reading of absence is retracted.

*Then why did the version move?* Because the codebase's own test for a bump is not "did keys get
added" but **would a reader run a different rulebook than the file states** — and this meets it in
the *dropping* direction. `System.Text.Json` ignores unknown members by default, so a version 4
reader handed a version 5 file would read a stated `maximumContractSeasons: 5` and enforce no term
limit at all. That is gap 1 with the sign flipped: rules stated in the file and not run by the
build. The version moved, and the serializer additionally sets `JsonUnmappedMemberHandling.Disallow`
so that a file from a *later* build fails structurally rather than silently dropping rules — the
permanent half of the fix, which does not depend on anyone remembering to move a constant.

As with 3 → 4 there is no migration: every valid version 4 file is a valid version 5 file with the
number changed, and the refusal says so.

**One field is a default rather than a rule.** Every negotiation *limit* is optional by absence.
`marketResolution` is not a limit — every league resolves offers somehow — so its absence is a
documented default, exactly as `draftLotteryEnabled` and `secondApronBlocksSalaryIncrease` already
behave. Version 4's doctrine is about lines a league may or may not have; a mode with a finite set of
behaviours takes a default. That distinction is the one to reach for when the draft-slot scale (M8)
and the tax brackets (M10) arrive, and it is recorded here so it is not re-litigated then.

**An invariant moved, and it was not optional.** `Team.Create` used to refuse a roster below the
configured minimum. Under that rule no team could ever be in the state a roster-slot hold describes,
so the hold projection would have been unreachable code the day it was written. The minimum is now
an *obligation* — a squad three short is the ordinary state of a team in the middle of free agency —
while the maximum stays a hard refusal, because a team over its limit is not a team with something
left to do. A trade may not take a team *further* below the minimum; it may leave a short team where
it already was. Recorded here rather than in a commit message because it is a domain rule, not a
detail.

## Gaps this enumeration exposed in existing code

Found while writing this, all pre-existing, none blocking. All four are now resolved — kept here
rather than deleted, so the reasoning survives.

1. ~~**No payroll floor.**~~ **Closed (schema v4).** `CapThresholds.PayrollFloor` is the first link
   in the ordering chain, and a team under it gets a stated standing (`cap.under_payroll_floor`)
   with its own explanation. Reporting only: what missing the floor *costs* is still M10.
2. **`Contract` has no clause concept.** **Recorded decision: no code, deliberately.** Movement
   consent (M9) and buyouts (M9) are different shapes — a veto right held by a party, and a
   mutation of guaranteed money — so any seam cut now would be re-cut in M9. Nothing forces a
   breaking change later: `Contract.Create` is already a non-throwing factory, `Contract` is never
   serialized directly (`ContractEnvelope` carries its own schema version, independent of the
   ruleset's), and adding a clause collection in M9 is additive on both. What binds M6 is the
   constraint, not a type: the `Negotiation` offer value object must not assume compensation and
   term are the whole of a deal.
3. ~~**No service time or age on `Player`.**~~ **Closed.** `Player` carries `BirthDate` and
   `SeasonsOfService`, with `AgeOn(DateOnly)` derived rather than stored — an age in a field stops
   being true the moment the calendar moves. Nothing else was added: career history stays M8 and
   biography stays M13.
4. ~~**`CapCharge` cannot express a hold as it stands.**~~ **Closed, by making the two identifiers
   optional per kind** rather than by adding a sibling type. `CapChargeKind.RosterSlotHold` has
   neither a `PlayerId` nor a `ContractId`, and the factories enforce that: the other two kinds
   still require both. The trade-off, stated: a sibling type would have had no nulls, but a payroll
   would then be two sums instead of one, and a hold that has to be added in three places is a hold
   one of them eventually forgets. The projection that *creates* holds is still M6 work.

## Ruleset genericity

Everything above assumes `LeagueRuleset` can describe more than one league. That assumption is now
measured rather than asserted: `data/rulesets/conformance/` holds a second league — uncapped, no
draft — and `tests/BallGM.Integration.Tests/RulesetConformanceTests.cs` pins exactly what happens
when the loader is handed it. The tests assert current behaviour, so they pass today; closing a gap
means editing them.

Four gaps were found, in severity order. **All four are closed as of `schemaVersion` 4**; each is
marked below rather than deleted, and `RulesetConformanceTests` now asserts the fixed behaviour.

1. ~~**An absent cap system loads as a cap system of zero, and does not fail.**~~ **Closed.** It used
   to: omitting every cap field yielded five thresholds of `0`, which satisfied the non-decreasing
   ordering check, so the league loaded, every team was over every line, and the cap sheet explained
   it in confident sentences — the exact "silently running rules the ruleset never stated" failure
   this codebase is otherwise careful to avoid. Absence is now absence: `CapThresholds.Uncapped`
   configures nothing, and a cap sheet in that league carries a real payroll and no standings at all.
2. ~~**"This rule does not apply" and "someone typed a zero" are the same input.**~~ **Closed.** They
   used to be: an absent `salaryMatchPercent` deserialized as `0`, and `TradeRules.Create` correctly
   read `0` as a data-pack typo, so an uncapped league's honest "no matching here" was refused as a
   mistake. The envelope field is now `int?`, which distinguishes absent from present-zero without a
   new JSON key. **The reading chosen: absence means the rule does not apply; any value present and
   below 100 is still a typo.** That keeps the existing typo-catch exactly as it was and gives
   absence the only other honest meaning.
3. ~~**A league with no draft cannot be configured.**~~ **Closed.** `DraftRules` used to require a
   positive round count and a retained round inside it, so the nearest expressible thing was a
   one-round draft nobody held — which still left franchises trading picks no draft would ever use.
   `roundCount: 0` now means "no draft", `HasDraft` is the question every caller asks, and a
   franchise in such a league cannot be handed a pick (`pick_registration.league_has_no_draft`).
4. ~~**`TeamCapSheet.StandingFor` assumes every threshold exists.**~~ **Closed.** It was a
   `.Single(...)`; it now returns `ThresholdStanding?`. "This league has no such line" is an answer,
   and a caller that has to wrap a `.Single(...)` in a try/catch has not been given one.

### The fix, as shipped

The cap system, the draft, and salary matching are **optional by absence**, at `schemaVersion` 4:

- `CapThresholds`' properties are `Money?`, and there are six of them now — the payroll floor is the
  new first link. The ordering check applies to the thresholds that are present, in that one fixed
  sequence, which `CapThresholds.Configured` defines once and both the check and the cap sheet's row
  order read. `CapThresholds.Uncapped` is a league with none of them.
- `CapLedger` builds a standing only for a configured threshold; an uncapped league produces a cap
  sheet with a payroll and an empty standings list, which is the truth.
- `TeamCapSheet.StandingFor` returns `ThresholdStanding?`.
- `TradeValidator` skips salary matching with no soft cap (and, separately, with no matching
  percentage configured), skips the ceiling check with no hard cap, and skips the apron restriction
  with no apron — each an explicit early return with a comment naming the rule, never an accidental
  consequence of a null. What was skipped travels in the assessment's new `Notes` list, one rule code
  and one sentence each, so a check that did not run is distinguishable from a check that passed.
- `DraftRules` accepts `roundCount: 0` as "no draft", with `HasDraft`, and drops the retained-round
  requirement in that case. It also configures nothing else in that case: a ruleset that says "no
  draft" and then sets a retained round is a contradiction, and is refused as one.
- No new JSON keys: absence carries the meaning. The version still moved to 4, because a v3 reader
  handed a v4 file would default the missing fields to zero and hit gap 1.

Predicted blast radius was about a dozen production files — `CapThresholds`, `CapLedger`, `TeamCapSheet`,
`TradeValidator`, `LeagueRulesetEnvelope`/`LeagueRulesetSerializer`, `RulesCapLedger`,
`RulesTradeEngine`, `FixtureLeagueDataSource`, `LeagueSnapshot`, `LeagueOverview`,
`GetLeagueOverviewQuery`, and the cap-sheet view model — plus their tests. Most of it is mechanical
pass-through; the thinking is concentrated in `CheckMoney` and in what the UI shows a GM whose
league has no cap.

The actual count was roughly twice that. The extra half: `ThresholdStanding` (the floor kind, and
`IsBreached` — the floor is the one line a team is on the wrong side of by being *under* it),
`CapCharge`/`CapChargeProjection` (the hold kind), `DraftRules` (which also moved from a throwing
constructor to a `Create(...)` result, matching `CapThresholds` and `TradeRules`), `TradeRules`,
`Player`, `TradeAssessment` and its read model (a third `Notes` list for skipped rules), the
draft-asset rules and the fixture that builds picks, and the pick-board view — because the
conformance league has no draft either, so that panel needed the same treatment as the cap sheet.

**Migration.** There is none, and that is deliberate rather than an omission: version 3 and version 4
differ only in *which fields may be absent*, so every valid version 3 file is a valid version 4 file
with the number changed. A version 3 file is refused, and the refusal says exactly that. What the
version bump buys is the other direction — a version 3 reader handed a version 4 file would read
every absent field as a zero, which is gap 1 all over again. No save migration was needed either:
`LeagueSaveEnvelope` does not embed the ruleset.

**This was done before Milestone 10**, as intended. The tax bill is built on top of the assumption
that a luxury tax line exists, and every milestone that assumed five thresholds would have made the
fix more expensive.

## Sources

Mechanism inventory was assembled from publicly published league agreements and their public
explanatory material, plus the published regulations of several non-North-American leagues to
check that each mechanism generalises. No agreement text, terminology, or branding is
reproduced here or in the ruleset; where a mechanism exists only as one league's specific
arbitrage rule, it is listed as **Declined** rather than renamed.
