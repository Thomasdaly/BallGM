Implement the smallest useful league and roster domain slice.

Acceptance criteria:

- Stable typed identifiers for league, franchise, team, and player.
- League and team aggregate boundaries are documented.
- A team roster can add and remove players through invariant-preserving methods.
- Duplicate player membership is rejected.
- Configurable minimum and maximum roster limits are represented without hardcoding one real league.
- Domain code has no Godot, filesystem, database, or JSON concerns.
- Tests cover valid membership, duplicate membership, removal, and roster-limit failures.
- Include structured error codes and readable messages.
- Update `docs/domain-language.md` only where necessary.

Workflow:
- inspect;
- propose the smallest design;
- implement;
- run tests;
- review the diff;
- report exact results.

Do not add contracts, draft picks, trades, or broad repository abstractions in this task.
