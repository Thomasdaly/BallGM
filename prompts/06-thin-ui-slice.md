Ship the Milestone 2 thin UI slice. Read `docs/roadmap.md` first — Milestone 2 was pulled forward from the end of the roadmap on purpose (see "Sequencing principle"), which is why this prompt is numbered after 03-05 but runs before them.

Goal: a human can look at a real fictional roster, cap sheet, and trade form driven by actual Milestone 1 domain data, not just pass unit tests. Ugly is fine. Untested-by-a-human is not.

Scope:

- First `BallGM.Application` query (e.g. `GetLeagueOverview` or `GetTeamRoster`) reading through `League`, `Team`, `Franchise`, `Player`.
- Fictional fixture data: one league, a handful of franchises/teams, enough players to fill rosters. Build it through the existing `Create(...)` factories, not throwing constructors, and mint every identifier with `SortableId.NewId()`.
- Load the fixture's league configuration through `LeagueRulesetSerializer` from an actual ruleset file on disk, not a hardcoded `LeagueRuleset` instance — this is the first real exercise of the moddable-rules path end to end.
- Avalonia: bare navigation shell, a roster grid bound to the Application query, a stub cap sheet screen, a stub trade-proposal form.

Constraints:

- The Application query returns its own read-model/DTO shape, not a raw Domain aggregate — even though Avalonia can technically see Domain transitively through the Application project reference, don't let the UI depend on aggregate internals.
- Cap sheet and trade form are explicit stubs: placeholder numbers, no cap enforcement, no trade validation or execution. That's Milestone 3 and Milestone 5 — do not pull it forward too.
- No theming, accessibility, localization, or keyboard navigation polish. That's Milestone 11.
- Add tests for the new Application query.
- After it builds, actually run the client and look at the roster/cap/trade screens. Report back what it felt like to use, not just that it compiled — that's the entire point of this milestone.

Before implementation, propose the minimum Application query/read-model shape and the view structure. Explain what is deliberately deferred and why.
