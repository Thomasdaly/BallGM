# Competitive feature review

Scope decision record. Source: the publicly published feature page for the 2026 edition of a
mainstream basketball video game's franchise mode
(`https://nba.2k.com/2k27/features/mynba/`), read on 2026-08-20.

Purpose is the same as `docs/negotiation-mechanisms.md`: enumerate a body of ideas **once**,
in generic terms, and record for each whether we build it, defer it to a named milestone, or
decline it — so that "we didn't copy that" is a decision with a reason attached rather than an
omission someone re-litigates in six months.

## How to read this document

Three rules govern everything below, and they are not negotiable:

1. **Mechanics only, never expression.** A mechanic — "friendships between players modify
   development" — is an idea, and ideas are fair to build. The *name* of that mechanic, its
   branding, its UI, its copy, its numbers, and the real players and teams it ships with are
   the other party's expression. None of that appears here or in the code. This is the same
   rule `CLAUDE.md` → "Safety and legal boundaries" already states, applied to a feature list
   instead of to a data pack. It constrains what *we* build and ship; it is not a constraint on
   what the data-pack format can express — see "Content neutrality" below.
2. **Generic naming, per `docs/negotiation-mechanisms.md` → "Naming rule".** Every entry below
   is named for what it does. Where the source's name is also the only obvious generic name
   (`buyout`, `payroll floor`), we use it; where the name is a coined brand, we do not.
3. **A feature on this list is not thereby scoped.** Verdicts here are provisional intent, not
   commitments. A deferred item enters a milestone only when that milestone's own scope
   document takes it.

## Content neutrality, and why it raises the bar on this list

The engine is content-neutral by design. A data pack describes leagues, franchises, players,
rulesets, calendars, and draft classes; the engine has no opinion about which ones. That is the
football-management precedent this project follows: the developer ships its own content, and the
community authors packs for the leagues it cares about. Two consequences, and they pull in
opposite directions:

- **On what we ship.** Unchanged and non-negotiable. Every asset in this repository — fixture
  data, sample packs, screenshots, marketing — is fictional. We do not bundle, host, mirror,
  endorse, or link third-party packs, and we do not design features whose value depends on one
  existing. `CLAUDE.md` → "Safety and legal boundaries" stands exactly as written.
- **On what the format must support.** Higher, not lower. If people are going to model leagues
  we have never seen, then every rule this document defers or declines is a rule someone will
  eventually need to express. That reframes several verdicts above from "nice to have" to
  "load-bearing": per-rule prose and sliders (§4), configurable award sets, configurable
  postseason formats, configurable lottery weighting, and pre-authored draft-class playlists
  (§5) are all *how a pack author describes a league we did not anticipate*. The same goes for
  the uncapped-league and no-draft gaps already recorded in
  `docs/negotiation-mechanisms.md` → "Ruleset genericity": those are not edge cases, they are
  the first two proofs that the format is honest.

The test for any feature on this list is therefore: **can a pack author express it, or does it
require a code change?** A feature that only we can configure is a feature that fails the
moddability pillar, no matter how well it plays.

## Verdict key

- **Built** — already exists in `src/`.
- **M6**–**M13** — named now, built in the stated milestone.
- **Backlog** — worth building, no milestone yet; revisit at M11.
- **Declined** — we do not intend to build it, with a reason.

---

## 1. Contracts and free agency

Most of this section overlaps `docs/negotiation-mechanisms.md`, which is the authority. Where
the two disagree, that document wins; entries here that it already covers are marked with a
cross-reference rather than restated.

| Mechanism | What it does | Verdict | Notes |
|---|---|---|---|
| **Agreement moratorium** | Terms agreed in a window, signable only at a resolution point | M6 | Already scoped — negotiation-mechanisms §3 |
| **Contract buyout** | Negotiated reduction of guaranteed money in exchange for release | M9 | Already scoped — negotiation-mechanisms §5 |
| **Post-buyout market** | A separate pool of bought-out players, signable late in the season under stricter rules | M9 | New. Cheap once buyouts and a calendar exist; it is a filter over free agents plus a `playoffEligibilityCutoff` interaction |
| **Short-term contract** | A fixed, very short deal (configured in days or games) outside the normal term limit | M7 | New. Needs a calendar. A ruleset field — `shortTermContractDays` — not a new contract kind |
| **Performance escalators** | Compensation conditional on individual or team outcomes, classified by likelihood, with the likely portion charged against the cap up front | **Reversed to M10** | `negotiation-mechanisms.md` §1 currently marks this **Declined**. See "Reversals" below |
| **Cash as a tradeable asset** | Money sent alongside players and picks, capped per season | M9 | New. A fourth `TradeAssetMovement` kind and a per-season allowance in `TradeRules`. Genuinely changes trade texture: it is how a team buys a pick |
| **Player signing demands** | A free agent conditions signing on a named teammate joining, or refuses a named rival | M9 | New. Depends on the relationship model in §2 and on AI front offices |
| **Deferred/stashed rights** | Retained signing rights over a player playing outside the league, convertible later | M12 | New. Needs an out-of-league concept, which is close to the deferred "international leagues" scope item |
| **Era-specific contract rules** | Term limits and ceilings that differ per configured historical period | **Already how it works** | A ruleset *is* an era. Shipping more than one ruleset file is content, not code |

## 2. Relationships, morale, and trust

This is the section with the most to take. Our current design has no model of what a player
thinks about anything, and `docs/vision.md` sells "believable negotiations" — believability is
exactly what this section buys.

| Mechanism | What it does | Verdict | Notes |
|---|---|---|---|
| **Directed player relationship graph** | Per-pair affinity, positive or negative, between two players | M13 | New system. See "Milestone 13" below |
| **Relationship seeding from shared history** | Affinity seeded from shared origin — birthplace, prior team, prior amateur programme, draft class | M13 | Requires biographical fields on `Player` that M8 (career history) introduces anyway |
| **Rivalry from competition** | Negative affinity accrued from repeated postseason elimination by the same opponent | M13 | Needs postseason history (M7) |
| **Personality/mentality compatibility** | Player traits that combine well or badly, driving locker-room friction | M13 | The trait vocabulary is ruleset content, not an enum in Domain |
| **Grouped affinity bonus** | Two or three high-affinity stars on one roster produce an on-court performance bonus | **Declined** | Discrete named tiers ("two players, then three") are a game-feel mechanic tuned for a mode with a live match engine. A continuous chemistry term feeding team strength gives the same simulation effect without the arbitrary cliff |
| **Front-office trust rating** | A per-GM scalar, moved by kept and broken promises, that gates how much a negotiating player believes an assurance | M13 | The strongest idea on the page for us. It converts "the AI ignored my pitch" into an explainable number, which is pillar 4 |
| **Promise as a first-class object** | An assurance made during negotiation — playing time, a re-signing, not being traded — that is later kept or broken | M13 | Prerequisite for trust. It is `Contract`'s missing clause concept (negotiation-mechanisms → gaps, item 2) generalised beyond the contract |
| **Owner/board directives** | Objectives set by ownership, whose completion moves trust and job security | **Backlog** | Already listed under `docs/product-scope.md` → deferred systems as "owner personalities and board objectives". Trust gives it something to move; keep them together |

## 3. Trade and roster administration

| Mechanism | What it does | Verdict | Notes |
|---|---|---|---|
| **Multi-team trades** | Three or more teams in one atomic exchange | **Built** | M5. `TradeProposal` already treats a three-team trade as a longer movement list |
| **Configurable participant and asset caps** | Upper bounds on teams per trade and assets per team | M9 | Currently implicit. Should be `TradeRules` fields with a stated reason (they exist to bound validation cost, not to model a rule) |
| **Depth chart by position** | Ranked positional ordering of a roster, used by free-agency and lineup screens | M7 | Needed by the simulation for minutes allocation regardless |
| **Best-available-by-position board** | Free-agent market sorted into positional columns against the team's own depth | M6 | UI-layer view over M6's free-agency board. Cheap, and it is the screen that makes the market legible |
| **Daily offseason digest** | Per-day summary of signings, trades, and remaining market talent | M9 | Same surface as M9's inbox/news feed. Build once |
| **League power rankings** | A derived ordering of teams by current strength | M9 | Derived read model. Must be computed from the same team-strength function the simulation uses, never a second opinion |

## 4. League configurability

Our whole architecture is built on the claim that a ruleset is configuration. This section is
that claim taken further than we have taken it, and it is the section most aligned with
`docs/vision.md` → moddability.

| Mechanism | What it does | Verdict | Notes |
|---|---|---|---|
| **In-league rule change process** | Rules proposed and adopted *during* a save, changing the active ruleset mid-career | M13 | Large. It makes `LeagueRuleset` a versioned timeline rather than a load-time constant — see "Milestone 13" |
| **Unlimited concurrent proposals** | No cap on how many rule changes are in flight | M13 | If we build the process at all, an arbitrary cap is a worse design than none |
| **Historical context on each rule** | Every configurable rule carries prose explaining what it does and why a league might adopt it | M10 | Cheap and high value. Ruleset schema gains an optional `description` per rule; the mod validator can require it for published packs |
| **Per-rule custom sliders** | Numeric rules editable directly rather than only via presets | M10 | Already the direction — `LeagueRuleset` is a JSON file. This is the UI for it |
| **Postseason format options** | Configurable series length and home-court sequence | M7 | Ruleset field. M7 already owns the postseason |
| **Configurable lottery weighting** | The draft-order randomisation is a configured weighting, not a fixed algorithm | M8 | M8 owns the lottery. Do not hardcode one weighting even for the first version |
| **Configurable award set** | Which end-of-season awards exist, and their voting rule | M8 | Awards are data. A league that has no defensive award should not need a code change |
| **In-season tournament** | A secondary competition inside the regular season, possibly at a neutral site | M13 | Ruleset-described schedule feature. Genuinely optional |
| **Scheduled expansion** | New franchises entering on a configured season, with an expansion draft | M13 | Big: it moves league membership from fixed-at-creation to an event. `League` already references teams by identifier, which helps |
| **Franchise relocation and rebranding** | A franchise changing city, name, and identity mid-save | M13 | `Franchise` is already the persistent identity separate from `Team`, which is exactly the split this needs |
| **Toggle for automatic league evolution** | A switch disabling scheduled expansion, relocation, and rule changes | M13 | Ships with whichever of the above ships. A simulation that reshapes the league without consent is a bug to some players |
| **Very long franchise horizon** | Careers runnable for many decades | **Backlog** | Not a feature so much as a performance and save-size property. It belongs to M12 profiling, and it is a good stress test target |

## 5. Draft

| Mechanism | What it does | Verdict | Notes |
|---|---|---|---|
| **Pre-authored draft class playlist** | An ordered list of hand-authored classes consumed one per season instead of generated ones | M10 | Pure data-pack feature and a strong moddability story: a community can ship twenty years of classes |
| **Playlist ordering controls** | Loop, shuffle, and reverse over that playlist | M10 | Trivial once the playlist exists, and shuffle is what makes it replayable |
| **Configurable lottery** | See §4 | M8 | |

## 6. Player-controlled career mode

The source's headline addition is a mode where one controls a single player's career —
experience points, unlockable perk tiers, inherited traits passed to a descendant, and a set of
difficulty modifiers.

**Declined, as a mode.** Two reasons, and neither is about quality:

- It is a *player* game wearing a franchise game's clothes. `docs/vision.md` commits to a
  front-office simulation with no 3D match engine; a career mode whose core loop is earning
  experience from your own on-court performance needs the match engine we have declared a
  non-goal. Without one, the mode is a spreadsheet that awards points for numbers the
  simulation generated on your behalf.
- Its progression vocabulary — experience tiers, unlockable perks, swappable ability slots,
  purchased permanent attribute increases — is a character-build system. It is a legitimate
  design, and it is directly at odds with product priority 1 ("correct rules and invariants")
  and priority 2 (deterministic simulation): a player who can buy immunity to injury is
  outside the rules the rest of the league plays by.

Two ideas inside it survive extraction:

| Mechanism | What it does | Verdict | Notes |
|---|---|---|---|
| **Descendant players** | A retired player's child entering a later draft class with correlated attributes | M13 | A long-career storytelling device that costs almost nothing: a parentage link on `Player` plus a generation bias in M8's class generator |
| **Simulation difficulty modifiers** | Named, declarative handicaps applied to the human-controlled team | **Backlog** | Interesting *only* if expressed as ruleset overrides scoped to one team, so the rules stay explainable. As a pile of hidden multipliers, declined |

## 7. Simulation fidelity

The page's least glamorous section is the one that matches our stated priorities best. These
are not features to copy; they are a checklist of the places a league simulation is known to go
wrong, and a reminder that this is where the quality bar actually sits.

| Area | The failure it names | Our position |
|---|---|---|
| **Tie-breaking** | Standings ties resolved by a rule that does not match the league's stated one | M7. Tie-break sequence is a ruleset list, and it gets its own tests. Cheap now, painful later |
| **Bounded model terms** | One simulation input (there, defensive rating) dominating an outcome probability without a cap | M7. Any term entering an outcome probability is bounded and that bound is a named constant, not a magic number |
| **Threshold edge behaviour** | Financial apron rules that almost match the stated rule | Ours are configuration, which removes the class of bug but not the need for tests at each boundary |
| **Event format drift** | Fixed formats for league events that later changed | Formats are ruleset data — §4 |

---

## Reversals

One entry above contradicts an existing decision, and the contradiction is deliberate.

**Performance escalators**, marked **Declined** in `docs/negotiation-mechanisms.md` §1, move to
**M10**. The original reason was that escalators are compensation trivia no player decision
depends on. That is wrong once likelihood classification is part of the mechanic: an escalator
classified *likely* is charged against the cap now and reconciled later, which is a real
roster-building decision — a team buys cap relief today against a risk it takes on. That is the
same shape as `signingBonus` amortisation, already M10, and it should land with it.

`docs/negotiation-mechanisms.md` has been updated to point here rather than silently changed.

---

## Milestone 13 — league life and locker room

Several verdicts above are M13, a milestone that did not exist before this review. Rather than
scatter them into M6–M12 and inflate milestones that are already scoped, they are collected
into one post-MVP milestone in `docs/roadmap.md`. Its two halves:

- **Locker room**: the relationship graph, personality traits, promises, and the trust rating.
- **League life**: rule changes during a save, expansion, relocation, and the in-season
  tournament.

Both halves share one architectural consequence, which is why they belong together: **they make
the league itself mutable over time**. Today `LeagueRuleset` is loaded once and treated as
fixed, and league membership is fixed at creation. Every M13 item breaks one of those two
assumptions. That is a save-schema and public-API change of the kind `CLAUDE.md` puts under
change control, and it is not to be started incrementally as a side effect of a smaller task.

## What this review does not change

- No non-goal in `docs/vision.md` is relaxed. Still no 3D match engine, still no licensed
  content, still no online play.
- No milestone from M6 to M12 gains scope, except the four cheap items explicitly dated M6, M7,
  and M8 above (positional board, depth chart, postseason format, configurable lottery and
  awards) — each of which sits inside work that milestone already owns.
- Nothing here is a commitment to ship. It is a commitment to have already thought about it.
