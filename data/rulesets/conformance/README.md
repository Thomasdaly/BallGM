# Ruleset conformance fixtures

**These files are not playable rulesets. Some of them may not load, on purpose.**

`data/rulesets/*.json` holds rulesets the game runs on. This subfolder holds rulesets that
describe leagues the ruleset schema *should* be able to express. They are executable
specifications for the schema: `tests/BallGM.Integration.Tests/RulesetConformanceTests.cs`
loads each one and asserts exactly what happens. A fixture the schema cannot yet express pins the
rule codes it fails with today; when the schema grows to cover the case, the assertion flips to
"loads, and these rules are off" in the same test.

They are deliberately outside the `data/rulesets/*.json` glob in
`src/BallGM.Infrastructure/BallGM.Infrastructure.csproj`, so nothing that loads a real ruleset
can pick one up by accident. Only the integration test project copies this folder.

## Why bother

`LeagueRuleset` is only generic if some league other than the one it was written for can be
expressed in it. Asserting "the rules are configurable, not hardcoded" in a doc costs nothing
and proves nothing. A second league that the schema visibly cannot describe is the cheapest
honest measurement we have, and it is much cheaper to take that measurement now than after
Milestone 10 has built a tax bill on top of the assumption that every league has five
thresholds.

## Current fixtures

### `uncapped-open-league.json`

A fictional league with **no salary cap system of any kind** and **no draft**. Players arrive
by open signing, teams spend what they can afford, and roster limits and trade legality are the
only constraints. Structurally this is a common shape outside North America, and it is the
single most different thing from the default ruleset that a basketball league can plausibly be.

Every cap field is absent rather than set to zero or to a very large number, because a league
with no soft cap does not have a soft cap of zero — it has none, and a cap sheet that reports
"over the soft cap by your entire payroll" is lying to the player. The same reasoning applies to
`draftRoundCount`: a league with no draft does not run a nought-round draft.

**Status: it loads, and free agency runs in it.** As of `schemaVersion` 4 this fixture is expressible — the cap system, the
draft, and salary matching are optional by absence — and the conformance tests now assert the fixed
behaviour: a cap sheet with a real payroll and no standings, a trade that skips salary matching and
says so, and a franchise that cannot be handed a pick. The four gaps it exposed are recorded, marked
closed, in `docs/negotiation-mechanisms.md` → "Ruleset genericity".

Milestone 6a added the free-agency case without adding a single key to this file, which is itself the
proof: the negotiation section is absent in full, and that loads as an **open market** — any team may
pay anyone anything, for any term. The offer screen says so as a signing route that *permits*, and
every route needing a line this league does not configure reports "not a rule here" rather than a
refusal. No compensation floor also means no roster-slot holds: there is no figure this league could
honestly reserve for an empty roster spot, so it reserves none rather than reserving nought.
