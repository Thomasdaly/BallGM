# Running BallGM locally

Every command you need to build, test and play the project yourself, from the repository root
(`~/repos/BallGM`). Commands are for macOS/zsh; they work the same on Linux.

## 1. Prerequisites (once)

| What | Check | Notes |
|---|---|---|
| .NET SDK 10.0.301 or a later 10.0 feature band | `dotnet --version` | Pinned in `global.json` (`rollForward: latestFeature`). |
| Python 3.13 venv for the pack builder (only to rebuild a pack) | `tools/league-pack-builder/.venv/bin/python --version` | Already created. The builder itself is standard library only. To recreate the venv: `cd tools/league-pack-builder && uv venv .venv --python 3.13 && uv pip install --python .venv/bin/python nba_api pandas pillow resvg_py`. The last four are needed only to refetch data and artwork for the private pack. |

```bash
dotnet restore BallGM.slnx
```

## 2. Build and test

```bash
./tools/verify-dotnet.sh        # the full gate: restore, format check, Release build, every test
```

Step by step:

```bash
dotnet format BallGM.slnx --no-restore                       # fix formatting (verify-dotnet only checks it)
dotnet build BallGM.slnx -c Release -p:UseSharedCompilation=false
dotnet test BallGM.slnx -c Release --no-build
dotnet test tests/BallGM.Rules.Tests -c Release --no-build   # one project
dotnet test tests/BallGM.Integration.Tests -c Release --no-build --filter LongSave   # one test class
```

Known: `ScreenTests.Screens_RenderMatrix` in `tests/BallGM.Client.Avalonia.UiTests/Release/ScreenTests.cs`
fails. That file is untracked and has never been committed; leave it out of commits (see §7).

## 3. Play the game

Fictional league (the shipped content):

```bash
dotnet run --project src/BallGM.Client.Avalonia
```

Debug is the default build for `dotnet run`. It includes the developer tooling (Avalonia DevTools, the
Zafiro MCP). For a Release-speed client, add `-c Release`.

A private real-league pack (local only; never commit or share it). Use your own pack folder's name:

```bash
BALLGM_LEAGUE_PACK="$PWD/local-packs/<your-pack>/pack/pack.json" dotnet run --project src/BallGM.Client.Avalonia -c Release
```

In the game:

- Pick your team: that sets the managed franchise. The summer AI never acts for it or trades with it.
- **Continue** plays to the next day or game. Past the last game it concludes the season and runs the
  whole offseason: options, re-signings, awards, ageing, the draft, buyouts, the free-agent market,
  trades. A season review follows.
- Before the season ends, re-sign your own expiring players from **Contracts → Extend…**. The summer AI
  will not re-sign them for you.
- Also before the season ends, bid in the coming summer's market from **Contracts → Summer offer…** on
  next summer's free agents (turn on *Whole league*). Offers stand through every round, and the season
  review says what became of each. They are not saved: make them in the same sitting you conclude the
  season.

Where the client keeps its files: `~/Library/Application Support/BallGM/` (settings, logs, saves; the
folder exists already).
`BALLGM_DATA_ROOT=/some/folder` points it somewhere else.

## 4. Rebuild a private pack (after builder or rule changes)

```bash
cd local-packs/<your-pack>
../../tools/league-pack-builder/.venv/bin/python make_pack.py --refit   # writes pack/ (about a minute)
cd ../..
```

- `--refit` redoes the team-strength fit. Use it whenever `tools/league-pack-builder/sheet.py`,
  `build_pack.py` or the match engine changed.
- To refetch the raw data (new rosters): `rm -rf raw && $PY fetch.py && $PY fetch_art.py && $PY make_pack.py --refit`,
  with `PY=../../tools/league-pack-builder/.venv/bin/python`.
- Real contracts: copy `contracts-template.csv` to `contracts.csv`, correct the rows you know, rebuild.
- A pack built before ruleset schema 17 is refused by the loader by design. Rebuild instead of editing it.

Last rebuilt 2026-10-28: pack schema 4, ruleset 17, 510 players.

## 5. Local harnesses (gitignored, under `scratch/`)

Build once with `dotnet build -c Release` inside the folder.

| Command (from `scratch/calib` or `scratch/bench`) | What it does |
|---|---|
| `dotnet bin/Release/net10.0/Calib.dll chain 8` | Eight chained seasons on the fictional league: champion, leaders, offseason counts each season. |
| `BALLGM_AUDIT=1 BALLGM_PACK=/Users/tomdaly/repos/BallGM/local-packs/<your-pack>/pack/pack.json dotnet bin/Release/net10.0/Calib.dll chain 8` | The same on a private pack, with the award and movement audit lines (`AWARD2`, `UNDER FLOOR`, top-30 movement). |
| `dotnet bin/Release/net10.0/Bench.dll` | Season sim time. The last line's median must stay at or under 16.9 ms (sim-regress row 22). |

## 6. Where things are written down

- `docs/franchise-realism.md`: the multi-season realism work, section by section; the newest section is the
  current state.
- `CLAUDE.md`: the project's current state and rules.
- `docs/ui/PROGRESS.md`: the UI overhaul ledger.
- `git log --oneline -20`: what was done recently.

## 7. Committing

Branch: `ui/release-overhaul`.

```bash
./tools/verify-dotnet.sh
git add -A -- . ':!tests/BallGM.Client.Avalonia.UiTests/Release/ScreenTests.cs'
git commit -m "feat(...): what changed"
```

Never commit `artifacts/`, `scratch/` or `local-packs/` (all gitignored).

## 8. Resuming with Claude Code

```bash
claude --continue        # pick up the last session in this folder
claude --resume          # choose an earlier session
```

A good first message: "read docs/franchise-realism.md (latest section) and `git log -10`, then continue".
