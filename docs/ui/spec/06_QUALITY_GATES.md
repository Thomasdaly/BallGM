# 06 · Quality Gates

What "release quality" means here, how it's measured, and what the final report must show. The `/goal` evaluator only sees what you print, so every gate ends in printed evidence.

---

## 1. Fixture league

A deterministic, fictional league for tests and screenshots. Build it through the game's **real** roster loader from files in `tests/Fixtures/` if the loader can read from a folder (this exercises the real pipeline); otherwise construct domain objects in code. Seed `42`, culture `en-US`, fixed in-game date.

### 1.1 Teams

Thirty fictional teams. No name, nickname or abbreviation matches an NBA team. Colors include deliberate hard cases for theming and contrast.

| Abbr | Team | Conf / Div | Primary | Secondary | Hard case |
|---|---|---|---|---|---|
| HCM | Harbor City Mariners | East / North | `#0B1D3A` | `#19A7A0` | Navy near the canvas (**user's team** in screenshots) |
| NGG | Northgate Glaciers | East / North | `#FFFFFF` | `#7FC8F8` | Pure white primary |
| OPA | Old Port Anchors | East / North | `#13294B` | `#E04545` | Navy almost identical to HCM |
| CAP | Capital Senators | East / North | `#5A1022` | `#D4AF37` | Maroon |
| GFP | Granite Falls Peaks | East / North | `#4A5A6A` | `#FFFFFF` | Slate |
| IRV | Ironvale Forge | East / Central | `#000000` | `#FF6A13` | Pure black primary |
| MDM | Midland Monarchs | East / Central | `#4B2991` | `#F2C14E` | Deep purple |
| RVB | Riverbend Otters | East / Central | `#1F4FD8` | `#F68B1F` | Royal blue |
| CDH | Cedar Hollow Owls | East / Central | `#4E3524` | `#E0A526` | Brown |
| PRS | Prairie Storm | East / Central | `#777777` | `#1D2A44` | Mid gray (worst case for black/white text) |
| BAY | Bayline Gulls | East / South | `#F4F4F4` | `#D7263D` | Off-white |
| TWT | Tidewater Typhoons | East / South | `#007C80` | `#0A0A0A` | Teal at the contrast edge |
| SLS | Silverlake Sirens | East / South | `#C2185B` | `#111111` | Magenta needing lightening |
| PKF | Palm Key Flamingos | East / South | `#FF5FA2` | `#00A5A8` | Pink |
| CVE | Cinder Valley Embers | East / South | `#E25822` | `#2B2B2B` | Flame orange (near `Warning`) |
| FRM | Frontier Mustangs | West / North | `#C8102E` | `#0B2341` | Red (near `Negative`) |
| PSA | Port Saint Augustine Navigators | West / North | `#CB1131` | `#FFFFFF` | Red almost identical to FRM; longest team name |
| RWR | Redwood Rangers | West / North | `#0E3B22` | `#EFE6D2` | Dark forest green |
| PHP | Pinehurst Pilots | West / North | `#3FA9F5` | `#0A2342` | Sky blue |
| GWH | Glasswater Herons | West / North | `#2E8B57` | `#F5F5DC` | Green (near `Positive`) |
| CPR | Copper Ridge Miners | West / Central | `#B87333` | `#111111` | Copper |
| LSL | Lakeshore Lynx | West / Central | `#5B2A86` | `#C0C5CE` | Purple with silver |
| DSC | Desert Springs Coyotes | West / Central | `#D9B382` | `#8B2E16` | Light sand |
| SMS | Sun Mesa Scorch | West / Central | `#FFD100` | `#2B2B2B` | Bright yellow |
| SBR | Stonebridge Rooks | West / Central | `#1C1C1C` | `#9EA2A2` | Near-black |
| NCV | Neon Coast Voltage | West / South | `#B6FF00` | `#000000` | Neon lime |
| BWB | Bluewater Barracudas | West / South | `#0057B8` | `#FFC72C` | Strong blue |
| HLS | Highland Stags | West / South | `#2F4F2F` | `#C9A227` | Dark olive |
| MVC | Mesa Verde Condors | West / South | `#A0522D` | `#F4E1C1` | Sienna |
| SFC | Starfall Comets | West / South | `#0B0B45` | `#9B5DE5` | Midnight blue |

The reference accent algorithm (01 §4) passes on all thirty: worst accent vs `Surface2` 3.11, worst `OnAccent` 4.64.

### 1.2 People and season state

- About 15 players per team, 150 free agents, 60 draft prospects (≈ 660). A **stress variant** adds players to reach ≥ 1,000.
- Ratings across every tier on the game's own scale. Ages 19–40, rookies to 15-year veterans.
- **Name stress:** `Maximilian Okonkwo-Vanderbilt`, `D'Shawn O'Malley-Fitzgerald`, `Jean-Baptiste Kouyaté-Lindqvist`, `Tyrese-Montgomery Abernathy-Clarke`, `Đorđe Vukčević-Ilunga`, `Nguyễn Thành Long`, and a single-name player `Kaleo`.
- Every injury status, every contract type the game models, at least one blocking inbox decision, news items, a mid-season state (the user's team HCM at 24–11, standings and results filled in).
- If the game has a live-game view, a deterministic in-progress game for S16 (fixed seed, captured at a fixed play index).

### 1.3 Mod fixtures (generated at test time, never committed)

Written into a temp mod root by a fixture builder:
- **Good pack:** PNG cutouts at 1:1 and 1.37:1, an opaque JPEG portrait, logos for all 30 teams — simple generated shapes, no real-world imagery.
- **Broken pack:** a missing file referenced by the mapping, a zero-byte file, a corrupt PNG (random bytes), a JPEG named `.png`, an 8000×8000 image, a 5:1 "logo", mapping entries with `..`, an absolute path and an escaping symlink, and a duplicate ID that conflicts with the good pack.
- **Stress pack:** 5,000 small generated PNGs mapped to fixture players.

## 2. Screenshot matrix and naming

| Set | Sizes | When |
|---|---|---|
| `before/` | 1280×720, 1920×1080, 2560×1440 — all at 100% | Phase 0, on the untouched UI, for every screen that exists at baseline |
| `after/` | 1280×720@100, 1920×1080@100, 2560×1440@100, **1920×1080@150** | Each time a screen changes; all re-captured for the final report |

- Path: `artifacts/ui-screens/{before|after}/{ScreenID}_{slug}_{W}x{H}@{scale}.png`, e.g. `S07_roster_1920x1080@100.png`.
- Capture with reduced motion on, fixture league loaded, user team HCM, the good mod pack enabled. Also capture `S05`, `S07` and `S10` with **no mods** as `{...}_nomods.png` (fallbacks must pass QA on their own).
- A baseline screen that cannot render headless before any change gets an `AUDIT.md` note with the error instead of a `before/` file.
- `artifacts/` is gitignored.

## 3. UiRelease test suite

All tests below carry the category / trait `UiRelease`. Record the exact command that runs them in `AUDIT.md` (for example `dotnet test <UiTests>.csproj -c Release --filter Category=UiRelease` for xUnit traits; NUnit uses `TestCategory=UiRelease`).

| Test | Asserts |
|---|---|
| `Screens_RenderMatrix` | Every inventory screen × every `after/` size: navigates through the real shell, renders with no exception and no binding warnings or errors in the log sink, saves its PNG |
| `Screens_Layout_NoOverflow` | At 1280×720@100 and 1920×1080@150, for every screen: no horizontal page scroll; every visible control outside a scroll viewer lies within the window bounds |
| `Tokens_NoHardcodedValues` | Scans `.axaml` files outside `Themes/`: no hex / `rgb()` / named colors (except `Transparent`), no `FontFamily=` or `FontSize=` literals. Fails on anything not in `tests/ui-lint-allowlist.txt`, where every entry carries a reason |
| `Bindings_Compiled` | Compiled bindings on by default; every view and data template declares `x:DataType`; reflection bindings only on allowlisted elements |
| `Images_OnlyViaResolver` | No bitmap construction from files or streams in `Views/` or `ViewModels/` outside the resolver (grep for `new Bitmap(`, `Bitmap.DecodeTo`, image-loading `AssetLoader.Open`) |
| `Contrast_Tokens` | Text tokens ≥ 4.5:1 on all five surfaces; semantic and CVD colors ≥ 4.5:1 on all surfaces; `AccentFallback` and `FocusRing` ≥ 3:1 on all surfaces |
| `Contrast_TeamColors` | For all 30 fixture teams: derived accent ≥ 3.0:1 vs `Surface2`; `OnAccent` ≥ 4.5:1 on the accent; `OnPrimaryRaw` ≥ 4.5:1 on the raw primary; every tier chip's text ≥ 4.5:1 in both palettes |
| `Theme_TeamSwitch` | Changing the user's team updates the accent resources and a rendered sample control's brush without restart; a team page scopes its own colors |
| `Assets_ModResilience` | With the good, broken and stress packs (and none): app starts, every screen renders, fallbacks shown for every broken entry, `Issues` lists each case from 05 §5, zero unhandled exceptions |
| `Assets_PathSafety` | `..`, absolute and escaping-symlink mappings are rejected before any file is opened |
| `Assets_NoUiThreadDecode` | With a deliberately slow fake decoder, `ResolveAsync` returns control to the UI thread immediately with the fallback shown; decoding runs on a non-UI thread (assert thread identity) |
| `Assets_RecycleCancel` | Scrolling a virtualized roster fast with the slow decoder: no row ever shows an image for a different player; stale requests are cancelled |
| `Assets_CacheBudget` | Scrolling the 5,000-image stress roster end to end keeps cache bytes ≤ budget; hit and miss counts are reported |
| `UiThread_Heartbeat` | During the 5,000-image scan and during navigation across all screens, the longest gap between 10ms dispatcher heartbeats is ≤ 200ms |
| `Table_Virtualization` | A 1,000-row player table realizes fewer than 100 row containers at the top, middle and end |
| `Keyboard_Traversal` | On every P0 screen: Tab and Shift+Tab visit every focusable control and cycle without traps; Esc closes overlays; arrow keys move within tab strips, lists and grids; Enter opens the focused row |
| `FocusVisible_Themes` | Every interactive control theme defines `:focus-visible` |
| `Automation_Names` | Icon-only buttons and every headshot / logo image expose an automation name |
| `Text_NoPlaceholders` | No user-visible string containing the whole words `TODO`, `FIXME`, `Lorem`, `placeholder`, `debug` or `test` (case-insensitive, word boundaries so "latest" passes) in views or resources, except allowlisted entries with reasons |
| `Names_LongFit` | Long fixture names and the longest team name render without overlap on roster, profile, standings and hover cards; trimmed text has a tooltip |
| `Motion_Reduced` | With reduced motion on: page transition is null, styled transitions are removed, count-ups show final values, the ticker pages |
| `Settings_Persist` | UI scale, density, reduced motion, team colors, rating palette, units, ticker, window state and Show entity IDs survive a restart of the app host |
| `Gallery_Complete` | Every **Req** component in 03 is registered in the gallery and renders in each of its states |
| `Perf_ScreenOpen` | Median of three timed navigations (after one warm-up) from command to first rendered frame at 1920×1080 with fixture data; prints a table; fails if any screen exceeds 500ms |

## 4. Budgets

| Budget | Limit | Measured by |
|---|---|---|
| Screen open (headless, 1920×1080, fixture data) | ≤ 500ms median | `Perf_ScreenOpen` |
| Realized rows, 1,000-row table | < 100 | `Table_Virtualization` |
| UI-thread stall during scan / navigation | ≤ 200ms | `UiThread_Heartbeat` |
| Image cache | ≤ 256 MB (default) | `Assets_CacheBudget` |
| Long operations | Progress shown within 250ms of start | Review + `ProgressOverlay` in gallery |

## 5. Accessibility baseline

Desktop adaptation of WCAG 2.2 AA:
- Text contrast ≥ 4.5:1 (≥ 3:1 for text ≥ 24px, or ≥ 18.66px bold); UI component boundaries and focus indicators ≥ 3:1 where they're needed to identify the control.
- Everything operable by keyboard; no traps; focus always visible.
- Interactive targets ≥ 24×24px.
- No information by color alone (ratings show numbers, deltas show ▲/▼, statuses show text).
- UI scale to 150% without loss of content or function.
- Reduced motion honored.
- Automation names on icon-only controls and images.

## 6. Visual QA

### 6.1 Rubric

Reviewers check every screenshot against:

| Rule | Check |
|---|---|
| R1 Hierarchy | One focal point; at most one primary button per view (plus the header's Continue) |
| R2 Tokens | Colors, type and spacing come from 01; nothing looks like a one-off |
| R3 Typography | Correct roles; caps headings; numbers right-aligned and tabular |
| R4 Alignment | On the 4px grid; consistent paddings; aligned columns |
| R5 Legibility | Contrast per §5; text on imagery sits on a scrim or band |
| R6 Truncation | Trimmed text has tooltips; no clipped headings; no ellipsis in numbers |
| R7 Layout | No overflow, overlap or clipped content at any size |
| R8 Imagery | No distortion; correct anchoring; fallbacks look intentional |
| R9 Team theming | Shell in the user's team colors; team-specific pages in theirs |
| R10 States | Loading, empty and error states look designed |
| R11 Consistency | Library components used; no look-alikes |
| R12 Anti-patterns | None from 01 §14 |

### 6.2 Severity

- **S1 Blocker:** blank or crashed render, overlapping or clipped primary content, text below 3:1, distorted images, placeholder or debug text, a primary action missing.
- **S2 Spec violation:** breaks a specific rule in 01, 03 or 04 — **the reviewer must cite it** (e.g. "01 §5: numbers centered"). Without a citation an S2 becomes S3.
- **S3 Polish:** a subjective improvement with no rule behind it.

**Gate:** zero open S1 and S2. Each S3 is fixed or logged in `FOLLOWUPS.md`.

### 6.3 Protocol

1. After capturing `after/` for a batch, spawn a **fresh subagent that has not seen the implementation**. Give it only: spec files 01, 03 and 04, this section, and the list of screenshot paths. Not the code, not the diff.
2. It opens every screenshot and returns rows: Screen · Screenshot · Severity · Rule (with spec citation) · Defect · Suggested fix.
3. Record the rows in `docs/ui/VISUAL_QA.md`, fix, re-capture, and send only the changed screenshots to another fresh subagent for verification.
4. A screen is clean when its latest screenshots were reviewed after its last change with zero open S1/S2.
5. Before the final report, run one complete fresh pass over every `after/` screenshot.

## 7. Acceptance checklist

The final report reproduces this table with PASS / FAIL / DEFERRED and an evidence pointer (test name, file path or screenshot) for each row. **DEFERRED is allowed only where the last column says Yes**, and only with a `FOLLOWUPS.md` entry.

| ID | Item | Deferrable |
|---|---|---|
| **A1** | `dotnet build -c Release` exits 0; zero XAML or binding warnings; compiler warnings ≤ the `AUDIT.md` baseline | No |
| **A2** | Full `dotnet test` exits 0 with 0 failed and 0 skipped; `git diff --stat` on test folders against the base branch shows no pre-existing test deleted, skipped or weakened (any modification explained) | No |
| **A3** | Every test in §3 passes and is listed by name | No |
| **B1** | Tokens, type, geometry and motion live in `Themes/`; token lint clean; allowlist entries all have reasons | No |
| **B2** | Fonts embedded with license files; one icon family; `ASSET_LICENSES.md` complete | No |
| **B3** | Team theming: live switch, page scoping, contrast for all 30 teams, setting to turn it off | No |
| **B4** | Every Req component in 03 exists with all states and appears in the gallery screenshots | No |
| **B5** | Motion per 01 §10; reduced motion removes non-essential motion | No |
| **C1** | Shell per 04 §1–4: header band, nav rail with collapse, page header and tabs, Continue states, back/forward, ticker toggle | No |
| **C2** | Every inventory screen restyled to its template; `before/` complete for baseline screens (or noted in `AUDIT.md`); `after/` complete at all four sizes, plus the three `_nomods` captures | No |
| **C3** | Loading, empty and error states exist for every data screen and appear in screenshots or the gallery | No |
| **C4** | No overflow, overlap or horizontal page scroll at 1280×720@100 and 1920×1080@150 | No |
| **C5** | `VISUAL_QA.md`: every `after/` screenshot reviewed by a fresh subagent after its last change; zero open S1/S2 | No |
| **C6** | Global search (S28) | No |
| **D1** | All images go through the resolver (`Images_OnlyViaResolver`) | No |
| **D2** | Mod resilience, path safety, off-UI-thread decode, recycle-cancel and cache-budget tests pass | No |
| **D3** | Mods & Graphics screen with packs, counts, issues, cache stats, Reload graphics without restart, Show entity IDs | No |
| **D4** | `docs/modding/GRAPHICS.md` complete per 05 §10 | No |
| **D5** | Skin token overrides (05 §9) | Yes (P2) |
| **E1** | Keyboard traversal, focus-visible and automation-name tests pass | No |
| **E2** | Ratings, deltas and statuses never rely on color alone; CVD palette setting works | No |
| **E3** | All UI settings persist across restart | No |
| **E4** | Long names fit or trim with tooltips | No |
| **F1** | Virtualization, heartbeat and perf budgets met; perf table printed | No |
| **F2** | Global crash handler, crash dialog and rotating log | No |
| **F3** | Long operations show progress; simulation off the UI thread or yielding between steps | Yes, only if it requires simulation changes |
| **F4** | No placeholder or debug text | No |
| **G1** | No third-party marks, likenesses or fonts in the base build or fixtures; every bundled asset listed with its license | No |
| **G2** | Credits and licenses screen | No |
| **G3** | `DESIGN_SYSTEM.md` written; `CHANGELOG` entry added | No |
| **G4** | `FOLLOWUPS.md` lists every deferred item with its reason | No |
| **G5** | Remaining P2 polish (00 §7): custom window chrome, lottery animation, shortcut overlay, saved table views, ticker paging polish | Yes (P2) |

## 8. Report formats

Print reports in the conversation as plain text. Run every command fresh in the same turn; never summarize earlier output from memory.

### 8.1 Final report

```
UI RELEASE · FINAL REPORT · Run R · Turn T

1. BUILD
   $ <exact build command from AUDIT.md>
   <last lines of real output, including the warning and error counts>

2. FULL TEST SUITE
   $ <exact test command>
   <summary line(s): total, passed, failed, skipped>
   $ git diff --stat <base>...HEAD -- <test folders>
   <output, with one line of explanation for any pre-existing test file that changed>

3. UIRELEASE TESTS
   $ <exact UiRelease command>
   <one line per test: name and result>

4. SCREENSHOTS
   before/: <n> files for <m> baseline screens x 3 sizes (<k> noted in AUDIT.md)
   after/:  <n> files for <m> inventory screens x 4 sizes, plus 3 _nomods

5. VISUAL QA
   screens reviewed <x>/<y> · open S1: 0 · open S2: 0 · S3 open: <n> (all logged)

6. PERFORMANCE
   <table from Perf_ScreenOpen> · max realized rows <n> · max UI stall <ms> · cache peak <MB>

7. ACCEPTANCE
   <the §7 table: ID | PASS / FAIL / DEFERRED | evidence>

8. DEFERRED
   <FOLLOWUPS.md IDs with one-line reasons>
```

### 8.2 Checkpoint report (turn 40 of a run)

```
UI RELEASE · CHECKPOINT · Run R · Turn 40

1. BUILD           $ <command>  -> <exit code, warning and error counts>
2. EXISTING TESTS  $ <test command excluding the UiRelease category>  -> <total, passed, failed, skipped>
3. UIRELEASE       $ <UiRelease command>  -> <passing and failing test names; failures are expected mid-project>
4. DONE THIS RUN   <bullets>
5. REMAINING       <every open item by phase, matching PROGRESS.md>
6. NEXT RUN STARTS WITH  <the first three items>
```
