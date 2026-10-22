# Packs: a guide for players and pack makers

Everything you add to BallGM — a league's rules, a roster, faces, logos, colours — is a **pack**: one
folder with a small file called `ballgm-pack.json` that says what it is. You pick packs with a
**league setup**, and before a career starts the **pack checker** tells you, in plain words, whether
that setup will load, play, and look right. No config files to regenerate, no caches to clear, no
"copy this into three folders".

## Where packs go

One folder per pack, inside the `mods` folder of your BallGM data folder:

| System | Folder |
|---|---|
| Windows | `%LOCALAPPDATA%\BallGM\mods\` |
| macOS | `~/Library/Application Support/BallGM/mods/` |
| Linux | `~/.local/share/BallGM/mods/` |

(Set `BALLGM_DATA_ROOT` to use a different data folder.) The packs the game ships with live next to
the game in `data/packs/` and are read the same way.

## The six kinds of pack

| Kind | What's in the folder |
|---|---|
| `ruleset` | `ruleset.json` — a league's rules (salary cap, roster sizes, playoffs…) |
| `league` | `league.json` — teams, players, contracts |
| `faces` | `faces/` — one image per player |
| `logos` | `logos/` — one image per team, and optionally the league's own logo |
| `colours` | `colours.json` — team colours |
| `setup` | `setup.json` — a ready-made league setup naming other packs |

A pack is exactly one kind. A "complete" download for a real-world season is usually a `setup` pack
plus the packs it names — which lets you swap the faces without touching the roster.

## The manifest

```jsonc
{
  "schemaVersion": 2,
  "id": "yourname.faces-2026",          // lower case, at least one dot; must be unique
  "name": "Faces 2026",
  "version": "1.0.0",                   // major.minor.patch
  "kind": "faces",
  "author": "yourname",
  "description": "Cut-out portraits for the 2026 roster pack.",
  "targets": [{ "pack": "yourname.roster-2026", "versions": "^1.0.0" }],
  "requires": [],                       // packs that must be in the same setup
  "conflicts": []                       // packs that must not be
}
```

Comments and trailing commas are fine. Unknown fields are not — the checker refuses them rather than
guess what they meant. Start one with `ballgm-packs init <folder> --kind faces --id yourname.faces`.

**Versions.** Bump the *major* number when keys change in a way that breaks packs built on yours (a
roster pack that renames player keys); the *minor* when you add content; the *patch* for fixes.
Ranges: `*`, `1.4.0`, `^1.4.0` (that version or later, same major), or `>=1.4.0 <2.0.0`.

## Keys: how a face finds its player

Every team and player in a league has a **key**. Teams always state one (`"key": "BAY"`). Players may
(`"key": "aldo-brenner"`); a player who doesn't is keyed by name and birth date, like
`aldo-brenner-2001-03-14`. Run `ballgm-packs keys <league pack id>` to list them all.

- **Faces:** put `faces/<player key>.png` in your pack. That's the whole mapping.
- **Logos:** `logos/<team key>.png`; set `"content": { "leagueLogo": "league.png" }` for the league's.
- **Colours:** `colours.json`:
  ```json
  { "BAY": { "primary": "#0B3D91", "secondary": "#F2A900" } }
  ```
- **Files you can't rename?** Add a map — `"content": { "map": "map.json" }` — holding
  `{ "aldo-brenner": "photos/IMG_0042.png" }`. A map entry wins over a file named for the same key.

Roster pack makers: **state player keys.** A derived key changes if you fix a typo in a name; a stated
one never does, so face packs built on your roster keep working across your updates.

Images: PNG (best — keeps transparency) or JPEG. Faces roughly square, head and shoulders, 512×512
recommended; logos square, transparent, 512×512 recommended. Paths must stay inside your pack
folder — anything else is refused before it is opened.

## League setups

```jsonc
{
  "schemaVersion": 1,
  "name": "My 2026–27 career",
  "ruleset": { "pack": "builtin.ruleset.na-major" },
  "settings": {                          // optional: change any single rule
    "softCap": 170000000,
    "hardCap": null,                     // null switches a rule off
    "capMechanics.taxRatesPercent": [150, 175, 250, 325]
  },
  "league": { "pack": "yourname.roster-2026", "versions": "^1.0.0" },
  // or a generated fictional league: "league": { "generated": { "teams": 30, "conferences": 2, "divisionsPerConference": 3, "seed": 7 } }
  "cosmetics": [                         // later packs win where two cover the same key
    { "pack": "yourname.colours-2026" },
    { "pack": "yourname.logos-2026" },
    { "pack": "yourname.faces-2026" }
  ]
}
```

Every setting, what it does, its range and whether it can be switched off:
`ballgm-packs settings` (or `ballgm-packs settings tax` for one group). The five built-in rule presets
— and every rule of the real league each one only approximates — are in `ballgm-packs presets` and in
`docs/league-presets.md`.

Don't want to write one? `ballgm-packs match builtin.ruleset.na-major` lists the installed leagues that
fit those rules and the cosmetic packs made for each; add `--league <id> --write my-setup.json` and it
writes the setup for you.

## The pack checker

```
ballgm-packs check my-setup.json
```

It works through the setup in the order the game loads it — settings, league, a **test season played
start to finish**, then the cosmetic layers — and gives one verdict:

- **STABLE** — loads, plays, and looks the way the packs say.
- **STABLE WITH WARNINGS** — loads and plays; something will look or behave differently. Read the
  warnings.
- **BLOCKED** — the game will not start this setup. Each error says what to do.

What blocks: rules the game can't run, a league that won't load under those rules, a season that
can't be played, and a pack the setup asks for that isn't there (or is the wrong version, or is
missing something it needs). **Cosmetics never block**: a missing, broken or mismatched image is
listed with a count and the slot shows the game's own drawn art instead.

The checker doesn't care what your packs contain — real names, real leagues, your own invented ones —
only whether they work.

To play a setup, start the game with `BALLGM_LEAGUE_SETUP` set to the setup file or a setup pack's
id. It is checked first; a blocked setup shows the report instead of a league.

## Rights

You are responsible for having permission to distribute whatever your pack contains. BallGM ships only
fictional content and does not host, bundle or link third-party packs.
