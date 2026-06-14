Implement the first contracts and team cap-ledger slice.

Scope:

- Contract identity and parties
- Contract seasons and compensation
- Guaranteed and non-guaranteed amounts
- Team/player option representation
- Cap-charge calculation interface
- Configurable league thresholds
- Team cap ledger query
- Structured rule explanations

Constraints:

- Do not hardcode current real-world dollar values or branded exception names.
- Money calculations must avoid floating-point errors.
- Keep rules swappable/versioned.
- Do not implement every exception in one task.
- Separate contract terms from cap-calculation results.
- Add tests for multi-season salary, guarantees, options, threshold comparisons, and deterministic calculations.

Before implementation, propose the minimum value objects and rule interfaces. Explain what is deliberately deferred.
