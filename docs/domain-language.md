# Domain language

Use these terms consistently unless an architecture decision changes them.

- **League**: Competition containing franchises, rules, schedule, seasons, and history.
- **Franchise**: Persistent organisation identity across seasons.
- **Team**: Competitive squad for a specific league context.
- **Player**: Basketball participant with identity, skills, contract status, health, and career history.
- **Contract**: Agreement containing terms, compensation, options, guarantees, and clauses.
- **Cap charge**: Amount applied to a team calculation for a season.
- **Threshold**: Configurable financial boundary such as cap, tax, first apron, or second apron.
- **Draft pick**: Asset identified by draft, round, and original franchise.
- **Pick ownership**: Current legal control of a draft pick asset.
- **Protection**: Condition determining whether a pick conveys.
- **Swap right**: Conditional right to exchange draft positions.
- **Transaction**: Auditable state change involving players, contracts, picks, money, or roster status.
- **Trade proposal**: Proposed multi-team exchange before validation and execution.
- **Rule violation**: Structured reason a proposed operation is illegal.
- **Ruleset**: Versioned configuration plus trusted algorithms defining league operation.
- **Simulation seed**: Input used to reproduce stochastic outcomes.
- **Data pack**: Validated declarative content used to create or modify a league.
