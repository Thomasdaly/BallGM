Ship Milestone 4: draft picks as tradeable assets, with protections that actually resolve, and a pick-ownership board in the client.

Read `docs/roadmap.md` (Milestone 4), `docs/domain-language.md` (Draft pick, Pick ownership, Protection, Swap right, Transaction), and `prompts/03-draft-pick-ownership.md` — that earlier note is the concept list, this prompt is the shippable slice. Milestone 3 established the pattern to follow: a Domain aggregate with a `Create(...)` factory, a derived-value projection, a Rules service returning rule codes plus explanations through an Application port, an append-only ledger, and a fixture spread that exercises the hard states rather than the comfortable one. Do the same shape here; do not invent a second one.

Goal: open the client, pick a franchise, and see every future draft it controls — what it owns outright, what it owes, what protection sits on each obligation, and what happens if that protection holds — with an auditable history behind every change. A GM who cannot see which of their firsts are already spoken for cannot trade.

Scope:

- `DraftPick` identity, immutable: league, draft season, round, original franchise. Identity never changes hands; ownership does. Keep the two apart from the first commit — conflating them is how pick systems end up unable to answer "whose pick was this originally".
- `PickOwnership` as the mutable side: current owner, plus the encumbrances riding on the asset. Duplicate current ownership must be impossible by construction, not by convention.
- `Protection` as an explicit value-object vocabulary, never a free-form string. Minimum coherent set: unprotected, and top-N protected with a rollover schedule (if it does not convey this draft, what it becomes in the next one) terminating in a stated fallback — conveys unprotected, converts to a later round, or extinguishes. That triple is enough to be internally consistent.
- A deterministic conveyance evaluator: given a supplied draft-order snapshot for a season, decide for each obligation whether the pick conveys, and return the rule code plus the human sentence explaining why — "protected through selection 4, landed at 3, rolls to the following draft unprotected". Draft order arrives injected, not generated: the lottery is Milestone 8, and conveyance must be testable without it.
- Swap rights as their own encumbrance, evaluated against the same snapshot. State explicitly, in code comments and in `docs/architecture.md`, whether swaps resolve before or after protections and why — the ordering is a rule, so it must be a decision, not an accident.
- Ownership validation the trade engine will call in Milestone 5: reject transferring a pick the team does not control, re-encumbering one that already carries a conflicting obligation, and violating the configured consecutive-future-round restriction (a franchise must retain a first-round pick in alternating future drafts). That restriction is configuration in the ruleset file alongside `DraftRules`, named generically — no real-world rule names, no hardcoded horizon.
- Asset history: every ownership change and every conveyance outcome recorded through the existing `TransactionLedger`, with new transaction kinds. Do not build a second ledger.
- Read model plus UI: a pick-ownership board extending the existing shell — franchises down, the next N drafts across, each cell showing owned/owed/swappable with its protection text, and a drill-down to that asset's history. Application read models only; `ArchitectureBoundaryTests` still governs.
- Extend the fixture so the board has something to show: a franchise that owns extra firsts, one that has traded away two of the next three, a protected pick that conveys against the seeded order and one that does not, one rollover already in its second year, and one live swap right.

Constraints:

- Fictional throughout. Generic rule names, configurable horizons, no real-league branding.
- Do not implement every protection form. Name what is deferred — range protections, record-conditional protections, cash considerations, multi-team pick routing, lottery odds — and defer it out loud rather than half-building it.
- Trade execution stays Milestone 5. This milestone builds the ownership surface that engine validates against, exactly as Milestone 3 built the cap ledger it will call.
- Pick and ownership DTOs are save/mod surface: version them from the first commit, keep runtime types separate from serialized shapes, and fail structurally on content this build cannot read.
- Determinism: no `DateTime.Now`, no ungenerated identifiers, no randomness inside conveyance. Time and identifiers come in through `IClock` and `SortableId.NewId()`.
- Tests, unhappy paths weighted as heavily as happy ones: ordinary transfer; duplicate-transfer rejection; a protected pick conveying; the same pick not conveying and rolling; a rollover reaching its terminal fallback; a swap exercised and a swap declined-by-outcome; transferring an unowned pick; a consecutive-future-round violation; and a save round-trip that preserves an encumbered, half-rolled-over pick.

Finish by running `./tools/verify-dotnet.sh`, then launch the client and look at the board for the franchise that owes two protected firsts and the one that has hoarded picks. Report what the screen tells a GM and, specifically, what it fails to tell them — a board that shows ownership but hides protection is a board that will get someone traded into a lottery they already sold. Update `docs/architecture.md` and `docs/domain-language.md` where behaviour actually changed.

Before implementation, propose the aggregate and value-object shapes, the encumbrance-evaluation order, the read-model additions, and the ruleset fields you intend to add. Explain what is deliberately deferred and why.
