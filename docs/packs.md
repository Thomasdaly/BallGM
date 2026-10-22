# Packs, league setups, and the pack checker

Design record for the mod platform's first slice (roadmap Milestone 10: manifests, a validation
CLI, safe asset loading, compatibility errors). Read this before touching `src/BallGM.Mods`,
`data/packs`, the league-pack keys, or anything that decides what loads. The player-facing guide is
`docs/modding/PACKS.md`; the league presets and how faithfully each expresses its league are in
`docs/league-presets.md`.

## The problem, stated as Football Manager's

Football Manager has the mod scene this game wants: community databases, face packs of hundreds of
thousands of images, logo packs, kits, real-name fixes. It also has the reputation for being painful
to mod, and the pain is specific. Every item below is a decision this design makes differently.

| FM pain | What goes wrong | What BallGM does |
|---|---|---|
| Graphics map to **numeric database ids** through a `config.xml` per folder | A face lands on the wrong player after a database update; ids are unreadable; the mapping file has to be regenerated | Packs address teams and players by **readable, stable keys** (`G04`, `aldo-brenner-2001-03-14`) the league pack states or derives. A file named after a key *is* the mapping; an optional map is only for files that cannot be renamed |
| **Caches and "reload skin"** toggles | Installed images do not show until the right preference is flipped | No cache layer in the format. The checker reads the folder as it is, every time |
| Editor files ticked **only at new-game time**, separately from graphics | The data and the graphics drift apart; nobody can say which database a save was started on | A **league setup** names everything — settings, league, cosmetics, in order — in one file the checker verifies end to end and a player can hand to another player |
| **No dependencies or versions** | A face pack for last season's roster silently half-matches this season's | Manifests state `version`, `requires`, `conflicts` and `targets` (with version ranges); the checker reports a cosmetic made for another league as a warning *with the coverage it actually achieves* |
| **Last folder read wins**, invisibly | Two packs with the same face; which shows depends on folder order | Load order is the setup's order, said in the report; every override is a note naming both packs; duplicate pack ids are resolved by a stated rule (higher version, then user over built-in) and reported |
| **Real-name and rule fixes** overwritten by every patch, copied into several folders | A league's rules drift back to the defaults after an update | League rules are a **ruleset pack** (a preset) plus **per-setting overrides** in the setup, each a named, documented, ranged field |
| The **advanced rules editor** is a tree of opaque records | Modelling a real league's rules needs someone who already knows the database | The **settings catalogue**: every ruleset field with a group, a label, a sentence saying what it does, a range, and an explicit "off" |
| Nothing says **what actually loaded** | Bug reports cannot be reproduced | The checker's report: verdict, load order, coverage, every finding with a code and a fix. It is what the CLI prints and what a bug report pastes |

## Stability, not policing

The checker answers one question: **will this setup load, play, and look the way its packs say?**
It does not ask what a pack contains beyond whether it works, and it does not report on whether a
pack carries real names, real leagues or anyone's branding. That follows the content-neutrality
position in `docs/competitive-feature-review.md`: the engine is neutral, the format must be
expressive enough for community packs, and **what we ship is fictional** — every built-in pack is a
ruleset or a setup over a generated fictional league.

Three rules decide severity, and they are the design:

1. **Anything that stops the game running is an error and blocks the load.** A ruleset the reader
   refuses, a league that will not load under those settings, a season that cannot start, finish, or
   roll over.
2. **Anything the setup asks for and cannot have is an error and blocks the load.** A pack that is
   not installed, the wrong version, the wrong kind, a missing dependency, a declared conflict.
   Loading something other than what was asked for is how a career ends up on the wrong data without
   anybody noticing.
3. **Nothing cosmetic ever blocks.** A missing, empty, truncated, mislabelled, oversized or
   badly-proportioned image, an unmatched key, a bad colour: each is a warning or a note, by pack and
   by count, and the slot falls back to the drawn art. A broken file is dropped from its layer, so an
   image beneath it (another pack's, the league pack's own) still shows.

Warnings mean "loads and plays, but something will look or behave differently than the packs say":
a team starting over a hard cap, a contract above the settings' maximum, a face pack written for a
different roster. Notes are everything else worth saying — every override applied, every rule a
preset approximates.

## Concepts

**Pack.** A folder with a `ballgm-pack.json` manifest. Exactly one **kind**: `ruleset`, `league`,
`faces`, `logos`, `colours`, or `setup`. One kind per pack is deliberate: it is what lets a face pack
outlive the roster update it was made for, and what lets the report say which layer a problem is in.

**Pack id and version.** Ids are `owner.name` (lower-case letters, digits, `.`, `-`); `builtin.` is
reserved for the packs the game ships and refused in a user folder. Versions are exactly
`major.minor.patch`. Ranges have four spellings only — `*`, `1.4.0`, `^1.4.0`, and space-separated
comparators — because every extra spelling is one more way for an author and a player to disagree
about what a range admits.

**Keys.** Teams have always had a pack-local `key`. Players now may state one too (`key`, optional,
on the league pack's player entry, no schema bump — the field is optional and an older build refuses
the unknown field rather than ignoring it). A player who states none is keyed by name and birth
date, `aldo-brenner-2001-03-14`, deterministically, with `-2`, `-3`… for collisions in file order.
Stated keys must be unique and file-name safe. The loaded league carries both maps on
`LeagueArtwork.TeamKeys`/`PlayerKeys` (loaded id → key), which is how anything outside the league
pack addresses its content when identifiers are minted fresh on every load.

**League setup.** One file — `setup.json` inside a setup pack, or anywhere — naming the ruleset
pack, per-setting overrides, the league (a league pack, or a generated fictional league of a stated
shape), the cosmetic packs in load order, and the seed of the test season. A setup is the whole
answer to "what am I playing".

**Settings catalogue.** `LeagueSettingCatalogue`: every ruleset field as a setting keyed by its
dotted path (`softCap`, `capMechanics.taxRatesPercent`, `gameRules.personalFoulLimit`), with a group,
a label, prose, a value kind, a range, choices, and whether it can be switched off. `null` switches
an optional setting off — and off means the league does not have that rule, the ruleset format's
central idea carried up to the settings layer. `LeagueSettingCatalogueTests` holds the catalogue to
the ruleset envelope in both directions, so no rule can be added to the format without prose.

**Preset.** A ruleset pack. The game ships five (see `docs/league-presets.md`), each with structured
**fidelity notes** — one per rule of the real league the format cannot express exactly, marked
`approximated` or `notModelled` — and the checker repeats every one in its report. A preset that
silently drops a rule is the silent-wrong-rule failure the ruleset format refuses everywhere else.

## The check, in load order

`PackChecker.Check(setup, library, options)`; the stage of each finding is `PackCheckStage`.

1. **Library.** `PackLibrary.Scan` over the built-in folder (`<app>/data/packs`) and the user folder
   (`<data root>/mods`, the same per-user root the client keeps saves in; `BALLGM_DATA_ROOT`
   overrides). Unreadable manifests take their own pack out and nothing else; a folder without a
   manifest is explained rather than ignored (including a bare `pack.json` league pack dropped in
   the way the UI branch's New career screen expects).
2. **Setup.** Each named pack is installed, the right kind, at an admitted version.
3. **Dependencies.** `requires` met inside the setup (with "installed but not in this setup" told
   apart from "not installed"), `conflicts` absent, `targets` matched — a cosmetic's target is the
   league pack it was keyed against, a league pack's the ruleset packs it was authored under. A
   cosmetic that requires another loads after it; moving it is a note, a cycle an error.
4. **Settings.** The preset's ruleset with the overrides applied through the catalogue
   (`LeagueSettingsResolver` — "did you mean `softCap`?"), then read by the real ruleset reader
   through `IPackContentProbe.ProbeRuleset`. Fidelity notes are reported here.
5. **League.** The league loaded *under those settings* — not under whatever ruleset the league pack
   names — and its fit reported: rosters below the minimum, salaries above the ceiling or below the
   floor for each player's service, contracts longer than allowed, payrolls over a hard cap or under
   a floor, team keys that are not file-safe, players keyed by name and birth date.
6. **Season.** A fresh load plays a whole season from the setup's seed, concludes it (awards, draft,
   re-signings) and opens the next, through a real `LeagueSession` wired as the client wires one.
   About 0.3–1 s for thirty teams. A crash is caught and reported as a defect in the game, not in a
   pack.
7. **Cosmetics.** Each layer read (`CosmeticPackReader`), matched by key, every matched image's
   header inspected (`ImageFileInspector`: signature, dimensions, size; 64 KB read per file at most),
   winners chosen last-pack-wins over the league pack's own artwork, coverage counted.

The report (`PackCheckReport`) carries the verdict (`Stable`, `StableWithWarnings`, `Blocked`), the
load plan, the coverage, every finding, and — unless blocked — a `PackLoadPlan`: the exact ruleset
text, the league source, and the winning cosmetic for every key. `CheckedLeagueDataSource` loads that
plan and nothing else, re-keying cosmetics to the fresh load's identifiers, so **what loads is what
was checked**.

`PackMatcher` runs the other way round: given league settings, which installed league packs load
under them (best fit first), which cosmetic packs target each, and a composed setup to start from.

## Decisions worth not re-deriving

- **The checker lives in `BallGM.Mods` and asks about content through a port.** Mods sees only
  Application and Domain; the ruleset format, the league-pack loader and the rules live in or under
  Infrastructure. `BallGM.Application.Packs.IPackContentProbe` is the question, `BallGM.Infrastructure
  .Packs.PackContentProbe` the answer — the same port-and-adapter shape the cap ledger set. The CLI
  and the client are composition roots that name the probe; `ArchitectureBoundaryTests` keeps
  `BallGM.Mods`, like Infrastructure, out of views and view models.
- **The test season plays a fresh load.** Playing a season mutates the aggregates it plays with; the
  league a player is about to start must not be one the checker has already played.
- **`PackFinding` is not `RuleFinding`.** A rule's verdict on a transaction is keyed by team and has
  no severity; the checker's whole output is the difference between "will not load" and "loads with
  fallbacks", and it is about a pack.
- **Generated leagues are shaped by the settings.** `GeneratedLeagueShape` adds team count,
  conferences (zero is a flat table), divisions and roster size; salaries are put on the chosen
  ruleset's scale (scaled to its soft or hard cap, held within its floor and ceiling, and each team
  kept under a hard cap). The standard shape draws the random stream in exactly the old order, so
  the calibration league is unchanged.
- **The manifest replaced the placeholder `DataPackManifest`** (schema version 1, a name and nothing
  else, never written to disk). The manifest is schema version 2.
- **Integrity hashes are not in this slice.** `docs/architecture.md` → "Mod and data-pack trust
  model" point 3 still stands: a content hash in the manifest, refused on mismatch, before the mod
  platform ships to players. It is a manifest schema bump when it lands.

## Found by the checker on its first run

Running the five presets through a full season found one real engine defect:
`RulesCapLedger.Status` rebuilt the ruleset assuming the league had conferences, so **every flat
(single-table) league with a postseason refused to load** ("8 qualifiers per conference plus a
final"). `LeagueConfigurationMapper.ToRuleset` now takes `bool? leagueIsFlat`, and the cap ledger,
which has no league to hand, passes null and skips the round-count check rather than guessing.
`PackFormatTests.Probe_AFlatLeagueWithAPostseasonLoadsItsCapSheet` is the regression test. This is
the case for the season stage: nothing short of playing the season would have found it.

## Not yet built

- The **Mods & Graphics screen** (UI spec S30) and the **New career** setup picker (S03) consume
  `PackCheckReport` and `PackMatcher`; today the client checks a setup named by
  `BALLGM_LEAGUE_SETUP` and shows the report in place of a league when it is blocked.
- **Skin packs** (UI spec 05 §9, token overrides) — a seventh kind, validated by the client against
  its token names.
- **Draft-class playlist packs** (roadmap M10).
- **Integrity hashes**, above.
- A cosmetic pack covering **staff, arenas or kits** — no slot exists for them yet.
