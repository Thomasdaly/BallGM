# sim-audit harness recipe

A standalone console project drives the real engine directly (no game-code edits). It lives outside
the solution at `scratch/audit/harness/` (gitignored) so it never touches `src/`.

1. **Project**: `scratch/audit/harness/Harness.csproj` — `net10.0` console app, `ProjectReference`s to
   `BallGM.Application`, `BallGM.Domain`, `BallGM.Infrastructure`, `BallGM.Mods`, `BallGM.Rules`,
   `BallGM.Simulation`. It copies `data/rulesets/*.json` to its output (`LinkBase="data\rulesets"`) —
   `FixtureLeagueDataSource` resolves rulesets relative to `AppContext.BaseDirectory`.

2. **Session construction** (changed in M9 — check `tests/BallGM.Integration.Tests/DynastyIntegrationTests.cs`
   for the current argument list if it fails to compile): `new LeagueSession(dataSource, new RulesCapLedger(),
   new RulesDraftAssetLedger(), new RulesTradeEngine(), new RulesSigningEngine(), new RulesFreeAgencyMarket(),
   new RulesSeasonEngine(matchEngine), new SaveGameSerializer(), new RulesFrontOfficeAdvisor(new RulesCapLedger()))`.
   `RulesSeasonEngine` takes an optional `IMatchEngine` — that is the injection point for instrumentation.

3. **Instrumented replica (how internal quantities are recovered)**: `ReplicaMatchEngine.cs` is a copy of
   `src/BallGM.Simulation/Seasons/PossessionMatchEngine.cs` with only counters added (possessions, dead
   possessions, clean/fouled misses, FT trips and trips ending in a miss, internal reboundable misses, boards
   awarded, OT periods, terminal-rule flag, side strength/efficiency). RNG call order is untouched.
   `InstrumentedEngine : IMatchEngine` plays every game through the real engine **and** the replica with the
   same `MatchSetup` and throws if a single score, stat line, or injury count differs — so the replica's
   counters are trustworthy for exactly those games. **Re-copy the engine file and re-apply the counters
   whenever `PossessionMatchEngine.cs` changes** (the comparison will throw if you forget). Replica was built
   from engine sha256 `32bb84cc…` (commit 5945b94).

4. **Modes** (`dotnet build -c Release`, then from `bin/Release/net10.0/`: `dotnet Harness.dll <mode> …`;
   output in `./out/`):
   - `league N` — N independent replays of the shipped 6-team league, fresh session per season (identifiers
     are re-minted per `Load()`), season seed = i. ~1 min for 1000 seasons.
   - `chain L S` — L leagues × S chained seasons through `ConcludeSeason` (draft, AI turn, auto-resign now keep
     chained runs alive; 10×20 completed with no failures). Seeds `7_000_000 + 100·L + s`.
   - `pack N <pack.json>` — independent replays of a league pack. The only fictional 30-team pack on disk is the
     user's local, gitignored `local-packs/sample/`; its ruleset is schema 9 and this build refuses it, so copy
     it to `scratch/audit/pack-sample/` with `schemaVersion` bumped to 10 (the reader's own error message says
     that is sufficient). **Never use `local-packs/nba-*`** — real-league content, outside the project's legal
     boundary. Seeds 101..100+N.
   - `controlled` — synthetic two-team `MatchSetup`s straight into the engine: strength gaps −70..+70 (past the
     ±40 efficiency clamp), rest days 1–3, rosters of 5–10 available players, plus 100k equal-strength games to
     sample overtime / the six-OT terminal rule. Seeds `HashSeed(gap,i)`, `HashSeed(424242,i)`.
   Each mode writes `b1_team_games_<mode>.csv` (one row per team-game, box-score totals + replica counters) and
   `b1_summary_<mode>.txt` (player-level checks, season-totals vs box-score sums, standings PF/PA vs games).

5. **Analysis**: `python3 scratch/audit/analyze_b1.py out/b1_team_games_*.csv` — Battery 1 violation rates with
   Wilson 95% CIs, rebound-leak / phantom-miss decomposition. Pure stdlib Python (no numpy/scipy, no network).

6. **Battery 1 halts the audit** while 1.4 is violated (see the latest `reports/sim-audit-*.md`). The pre-M9
   harness modes for Batteries 2–7 (talent-generator sampling, win-prob-vs-gap sweeps, dynasty runs) are kept for
   reference in `scratch/audit/harness/Program.prev.cs.txt` — they predate the shot/rebound split, so port them
   onto the replica rather than reviving them as-is.

7. **Known traps**:
   - When bucketing controlled matchups by gap, derive "the stronger side" from the numeric ratings on each
     row, never from a flag whose meaning flips sign when the gap is negative (cost a full re-run once).
   - In `controlled` mode, `GameId.For(season, day, i % 900)` collides; key buffered games by id **and** seed.
   - `FieldGoalsAttempted` in the box score includes shooting-fouled misses (charged before the foul is drawn),
     so `FGA − FGM` is not the count of live-ball misses — use the replica's `clean_miss + ft_trip_miss`.
