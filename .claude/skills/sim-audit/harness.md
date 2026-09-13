# sim-audit harness recipe

A standalone console project drives the real engine directly (no game-code edits). It lives outside
the solution at `scratch/audit/harness/` so it never touches `src/`.

1. **Project**: `scratch/audit/harness/Harness.csproj` — `net10.0` console app, `ProjectReference`s to
   `BallGM.Application`, `BallGM.Domain`, `BallGM.Infrastructure`, `BallGM.Mods`, `BallGM.Rules`,
   `BallGM.Simulation` (same set `BallGM.Integration.Tests` uses). It also copies
   `data/rulesets/*.json` to its own output directory (`LinkBase="data\rulesets"`,
   `CopyToOutputDirectory=PreserveNewest`) — `FixtureLeagueDataSource` resolves rulesets relative to
   `AppContext.BaseDirectory`, so without this copy `session.Load()` fails.

2. **Program.cs** exposes subcommands via `args[0]` (`all`, `prospects`, `seasons`, `calibration`,
   `dynasty`, `debugstrength`). Build once with `dotnet build -c Release`, then run from
   `scratch/audit/harness/bin/Release/net10.0/` with `dotnet Harness.dll <mode>`. Output CSVs land in
   `./out/` next to the built DLL — copy them to the scratchpad before analysis (they can be large;
   `player_lines.csv` at 1000 seasons is ~400MB).

3. **Independent season replays** (`RunSeasons`): construct a fresh `LeagueSession` (new
   `FixtureLeagueDataSource`, `RulesCapLedger`, `RulesDraftAssetLedger`, `RulesTradeEngine`,
   `RulesSigningEngine`, `RulesFreeAgencyMarket`, `RulesSeasonEngine`, `SaveGameSerializer`) and call
   `.Load()` for every season index. `StartSeason(seed: i)` → `AdvanceToEndOfSeason()` →
   `Standings()`/`BoxScoresOn(day)` for `day in 0..Calendar.LengthInDays` → `ConcludeSeason()` (captures
   `ChampionTeamId` for Battery 6.4). A fresh session per season is required because
   `FixtureLeagueDataSource` mints new identifiers every `Load()` (see CLAUDE.md), and because nothing
   in this codebase re-signs free agents automatically — chaining seasons in one session runs out of
   rostered players by season 3–4 (see `dynasty` below).

4. **Chained multi-season run** (`RunDynasty`): one `LeagueSession`, loop
   `StartSeason`→`AdvanceToEndOfSeason`→record standings/roster→`ConcludeSeason`→next `StartSeason`.
   Demonstrates directly that nothing auto-re-signs departing free agents: by season 4 a team has
   "nobody available" and the loop aborts. Useful for Battery 5, but only for ~3 usable seasons.

5. **Controlled two-team matchups** (`RunCalibration`): build `MatchSetup`s directly against
   `PossessionMatchEngine` (bypassing `LeagueSession`/`SeasonEngine` entirely), mirroring
   `MatchTestFixtures.Team()` — a 10-player roster per side, `Overall = topRating - 3*index`, run
   through `DepthChartBuilder`. Sweep `homeRating`/`awayRating` independently (not `delta/2` split —
   bucket by the *signed* `homeRating - awayRating` gap when analyzing, not by an internal "high/low"
   label, or a sign bug silently averages the win-favors-the-strong-team effect with its mirror image
   into ~50%). 4000 games per gap value, seeded via a simple hash of `(gap, i)`.

6. **Recovering possessions without touching game code**: `PossessionMatchEngine.Play` makes its first
   random draw as `BasePossessionsPerGame + random.NextInt32(-PossessionSpread, PossessionSpread+1)`
   from a `SeededRandomSource(setup.Seed)` it owns internally, and that value is never returned on the
   output. Since `SeededRandomSource` is a pure deterministic function of its seed, a second, entirely
   separate `SeededRandomSource(seed)` constructed in the harness and given the identical first call
   reproduces the same draw. This is how `pace`/`ortg` were recovered for controlled matchups (not
   available for real `LeagueSession` games, since the per-game seed there is
   `SeedMixer.Mix(seasonSeed, gameId)` and the harness does not currently replicate that mixing).

7. **Talent-generator sampling** (`RunProspects`): call `BallGM.Rules.Draft.ProspectGenerator.Generate`
   directly, thousands of times with incrementing seeds, against a manually-supplied
   `DraftClassRules.Create(classSize: 1, minimumRating: 40, maximumRating: 99, prospectAgeYears: 19)` —
   the shipped ruleset (`data/rulesets/default-league.json`, schema v9) does not itself configure draft
   class generation, so this bypasses that and exercises the generator's functional form directly.

8. **Analysis**: pure Python 3 (no numpy/scipy available in this environment and no network access to
   install them — `pip install` fails with PEP 668 externally-managed-environment). All statistics
   (mean/sd/skew/excess-kurtosis/Pearson r/OLS/Hill estimator/logistic fit via OLS-on-logit) were
   hand-rolled in `scratch/audit/analyze.py`-style scripts, streaming the large `player_lines.csv`
   rather than loading it whole.

9. **Known trap**: when bucketing controlled matchups by `abs(delta)` where `delta` can be negative,
   check that "the stronger side" is derived from the actual numeric ratings in each row, not from a
   boolean flag whose meaning silently flips sign when `delta < 0`. This cost a full re-run: an
   internal `high`/`low` labeling bug made the win-probability-vs-strength curve look almost flat
   (~50% at every gap) until re-derived from `homeRating - awayRating` directly, at which point it
   showed a clean, strongly monotonic, near-logistic response (R² > 0.99 on the logit scale).
