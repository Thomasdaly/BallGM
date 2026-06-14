Design and implement draft-pick ownership as a tested domain slice.

Required concepts:

- Pick identity: league, draft season, round, original franchise
- Current owner
- Ownership history
- Protection terms
- Conveyance result
- Rollover outcome
- Swap rights
- Prevention of duplicate current ownership
- Validation of attempts to transfer unavailable or already-encumbered assets

Requirements:

- Use generic configurable rules; do not use NBA branding.
- Separate pick identity from mutable ownership state.
- Model protections explicitly rather than as free-form strings.
- Make evaluation deterministic.
- Return structured validation codes and readable explanations.
- Preserve an auditable transaction history.
- Add unit tests for:
  - ordinary transfer;
  - duplicate transfer rejection;
  - protected pick conveying;
  - protected pick not conveying;
  - rollover;
  - swap exercise;
  - invalid circular/conflicting encumbrance where applicable.

First present a design note and identify unresolved edge cases. Then implement the accepted smallest coherent slice.
