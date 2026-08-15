Ship Milestone 3: contracts, cap charges, and a transaction ledger, ending with the Milestone 2 cap sheet stub replaced by real numbers.

Read `docs/roadmap.md` (Milestone 3) and `docs/domain-language.md` (Contract, Cap charge, Threshold, Transaction) first. The thresholds and roster limits are already configuration loaded from `data/rulesets/default-league.json` through `LeagueRulesetSerializer` — this milestone makes the domain *read and enforce* them. Do not reinvent them, do not move them into code.

Goal: open the client, pick any team, and see a cap sheet whose every figure is derived from contracts that actually exist on that team's players — with the payroll changing when a contract changes, and an auditable ledger entry behind every change.

Scope:

- `Contract` aggregate: parties (team, player), a season range, per-season compensation, guaranteed vs non-guaranteed amounts, and team/player option representation. Created through `Create(...)` returning `DomainOperationResult<Contract>`, like every other aggregate.
- Cap charge as its own concept, separate from contract terms: a contract states what is owed, a cap charge states what counts against a threshold in a given season. Dead money is a cap charge with no active contract behind it — model it that way from the start rather than as a special case bolted on later.
- A cap-ledger domain service that totals a team's charges for a season and compares them to `LeagueRuleset.CapThresholds`, returning structured results (rule code plus human explanation) rather than bare numbers, so the UI can say *why* a team is hard-capped, not just that it is.
- A transaction ledger: every signing, waiver, and cap-affecting event recorded as an append-only entry with a reason. Nothing mutates payroll silently.
- Extend `FixtureLeagueDataSource` so every fictional player carries a contract, and team payrolls land in a deliberately varied spread — one team over the second apron, one under the soft cap, one mid-tax — so the UI is exercised against real rule states rather than one comfortable case.
- Extend the `GetLeagueOverview` read model (or add a sibling query) with the cap figures the screen needs, keeping the UI on Application types.
- Replace `CapSheetViewModel`'s `PlaceholderCommittedSalary` / `PlaceholderDeadMoney` constants and the STUB banner with real data. The signed "over/under" presentation of threshold distance stays — extend it to every threshold, not just the soft cap.

Constraints:

- All money through the existing `Money` value object in integer smallest units. No `decimal`, no `double`, no floating-point rounding anywhere in cap math.
- No real-world dollar values, no real CBA exception names, no team branding. Thresholds and any exception amounts come from the ruleset file.
- Do not implement every cap exception. Pick the smallest set the ledger needs to be coherent and name explicitly what is deferred to a later milestone.
- Contracts are save/mod-schema surface: version the DTOs from the first commit and keep runtime types separate from serialized shapes, as `LeagueRulesetEnvelope` already does.
- Trade validation stays out of scope — that is Milestone 5. The cap ledger it will call is what this milestone builds.
- Tests: multi-season salary, partial guarantees, an exercised and a declined option, dead money surviving a released player, threshold comparison at and either side of each boundary, ledger append order, and a save round-trip. Unhappy paths get the same attention as happy ones.
- Determinism: no `DateTime.Now`, no ungenerated identifiers. Time and identifier minting come in through injection and `SortableId.NewId()`.

Finish by running `./tools/verify-dotnet.sh`, then launch the client and look at the cap sheet for a team over the tax and a team under the cap. Report what the screen tells you and what it fails to tell you — a GM who cannot see why they are capped has not been given a cap sheet. Update `docs/architecture.md` and `docs/domain-language.md` where behavior actually changed.

Before implementation, propose the minimum aggregate and value-object shapes, the ledger entry shape, and the read-model additions. Explain what is deliberately deferred and why.
