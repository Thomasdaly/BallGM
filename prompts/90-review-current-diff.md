Review the current uncommitted diff as a senior C# architect and sports-simulation engineer.

Do not modify files during the first pass.

Check:

- correctness and hidden state mutation;
- domain invariants;
- deterministic behaviour;
- simulation-core independence from Godot;
- save/mod compatibility concerns;
- misuse of floating point for money;
- unclear ownership semantics;
- transaction atomicity;
- security of external data;
- unnecessary dependencies;
- overengineering;
- missing tests;
- misleading documentation;
- commands that were claimed but not run.

Return:

1. Blocking findings
2. High-priority findings
3. Non-blocking improvements
4. Missing test cases
5. A minimal remediation plan

After presenting the review, wait for an explicit instruction before applying broad remediation. Small obviously safe fixes may be proposed separately.
