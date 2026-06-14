Implement the first end-to-end trade-validation vertical slice.

Initial scope:

- Two teams
- Players
- Draft picks
- Roster limits
- Ownership checks
- Configurable injured-player eligibility
- Basic configurable salary matching
- Atomic execution
- Resulting rosters, cap ledgers, and pick ownership
- Structured blocking reasons and warnings
- Transaction ledger entry

Non-goals:

- Full historical collective bargaining rules
- Every exception
- Three-plus-team trades
- Cash
- sign-and-trade
- UI

Requirements:

- Validation must not mutate league state.
- Execution must occur only from a valid, current proposal.
- Detect stale proposals.
- Execution must be atomic.
- Tests must cover success, each failure class, and rollback/no partial mutation.
- Explanations must be suitable for display in a trade-machine UI.

Provide a short design and test matrix before implementation.
