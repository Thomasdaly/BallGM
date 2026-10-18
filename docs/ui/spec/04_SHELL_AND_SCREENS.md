# 04 · Shell, Navigation and Screens

Wireframes are schematic (proportions and hierarchy, not pixels). All names, teams and numbers in them are fictional fixture data.

**Wireframe notation:** `/LABEL/` skewed accent-filled shape (the primary button, Continue, the active tab flag, score-bug segments) · `[ LABEL ]` secondary button or control · `[ABC]` team logo · `(o)` player headshot · `(#)` draft-pick token · `▌` 3px accent stripe · `█▉▌` bar fill · `▁▃▅▇` sparkline.

---

## 1. App shell

```
┌────────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ [LOGO] HARBOR CITY MARINERS  24-11  2ND EAST   < >   [ Search  Ctrl+K ]   TUE 14 JAN  /CONTINUE >/     │
│                                                                                  vs IRV  7:30 PM       │
├────────────────┬───────────────────────────────────────────────────────────────────────────────────────┤
│  HOME          │  HOME / ROSTER                                                                        │
│  INBOX       3 │  ROSTER                                    [ COMPARE ] [ COLUMNS ] [ VIEWS v ]        │
│▌ ROSTER        │  /OVERVIEW/   RATINGS    CONTRACTS    STATS    HEALTH                                 │
│  ROTATION      │  ┌─────────────────────────────────────────────────────────────────────────────────┐  │
│  TACTICS       │  │ [ Search roster ]   PG  SG  SF  PF  C    AGE 19-40    OVR 40-99    [ RESET ]    │  │
│  SCHEDULE      │  ├─────────────────────────────────────────────────────────────────────────────────┤  │
│  STANDINGS     │  │   #  PLAYER                        POS  AGE   OVR   POT   CONTRACT     STATUS   │  │
│  TRADES        │  │   7  (o) M. OKONKWO-VANDERBILT     SF    27   [84]  [86]  $31.2M x 3            │  │
│  FREE AGENCY   │  │   3  (o) D. O'MALLEY-FITZGERALD    PG    24   [82]  [90]  $9.8M x 2    ROOKIE   │  │
│  DRAFT         │  │  21  (o) J. KOVAC                  PF    29   [79]  [79]  $18.2M x 2   OUT 2-3W │  │
│  SCOUTING      │  │      ...  virtualized rows, sticky header, right-aligned numbers                │  │
│  STAFF         │  └─────────────────────────────────────────────────────────────────────────────────┘  │
│  FINANCES      │                                                                                       │
│                │                                                                                       │
│  [<<] collapse │                                                                                       │
├────────────────┴───────────────────────────────────────────────────────────────────────────────────────┤
│ SCORES  HCM 98-101 IRV  FINAL  ·  NCV 77-80 SMS  Q4 2:31   |   NEWS  Forge guard out 2-3 weeks (ankle) │
└────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

**(A) Header band — 64px.** `TeamPrimaryRaw` gradient from the left (~40% of the width) fading into `Canvas`. Left to right: team logo (40px), team name (H3), record and standing, back/forward, global search (Ctrl+K), current in-game date, **Continue**. Continue is the only accent-filled button in the header. Below 1440 wide the breadcrumb leaves the page header first, then search collapses to an icon.

**(B) Nav rail — 220px expanded, 64px collapsed** (auto-collapses below 1440 wide; a toggle persists the choice). Lists only sections whose screens exist. Active item: 3px accent stripe, `Surface2` fill, `TextPrimary` label. Count badges for unread inbox items and items needing attention.

**(C) Page area.** Breadcrumb (caption) → `PageHeader` (H1 caps title, optional subtitle, page actions with at most one primary) → `SkewTabStrip` for sub-views → content. Optional `Banner` under the header for season-phase alerts.

**(D) Ticker — 32px**, toggled in Settings. Scores (live, final, upcoming) and headlines. Every item is an `EntityLink`.

**Overlays:** toasts top-right under the header; dialogs centered with a `Scrim`; hover cards anchored to their link; the progress overlay covers the page area only, so the header still shows the date ticking.

## 2. Navigation model

- Sections come from the screens that exist (`AUDIT.md` inventory). Don't add sections for screens that don't exist.
- **Back/forward history** like FM: Alt+Left / Alt+Right, mouse buttons 4 / 5, and the header arrows. History stores the screen plus its key state (selected tab, scroll position, filters) where cheap.
- **Everything links:** any player, team, staff member or competition name is an `EntityLink` (03 §4).
- Opening a profile from a table keeps the table's state, so Back returns to the same scroll position and filters.
- Deep state such as a selected tab is part of the breadcrumb label ("Roster › Contracts").

## 3. Keyboard map

| Keys | Action |
|---|---|
| Ctrl+K (also Ctrl+F outside text fields) | Global search |
| Alt+Left / Alt+Right | Back / forward |
| Ctrl+1 … Ctrl+9 | Nav sections in rail order |
| Ctrl+Enter | Continue (shown in its tooltip) |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous sub-tab |
| Tab / Shift+Tab | Move focus; never trapped outside dialogs |
| Arrow keys | Inside tab strips, segmented controls, lists, grids |
| Enter / Space | Activate; Enter on a row opens its profile |
| Shift+F10 or the Menu key | Context menu for the focused entity |
| Esc | Close dialog, flyout or hover card; clear the search box |
| F11 | Full screen |
| ? | Shortcut overlay (P2) |

Shortcuts never fire while a text field has focus unless they include Ctrl or Alt.

## 4. Continue button

| State | Label / subtitle | On activate |
|---|---|---|
| Ready | CONTINUE · next event ("vs IRV 7:30 PM", "Trade deadline", "Draft lottery") | Advance time the way the game already does |
| Blocked | RESPOND · "2 items need you" | Opens the first blocking inbox item (FM behavior) |
| Game day | GO TO GAME · opponent | Opens pre-game (S15) |
| Simulating | STOP · current date ticking | Stops at the next safe point, if the game supports it; otherwise shows progress only |

The dropdown lists only the sim options the game already supports (to next game, next week, trade deadline, all-star break, end of regular season…). No new sim logic.

## 5. Screen templates

Every inventory screen maps to one template. A screen not listed in §6 gets the closest template and a new ID (S34+) in the inventory.

| Template | Structure | Used by |
|---|---|---|
| **T0 Full-screen** | Branded canvas, no shell | Splash, title, crash |
| **T1 Dashboard** | 12-column tile grid, featured tile top-left | Home hub, pre-game, finances overview |
| **T2 Data table** | Filter bar → table or card grid → footer counts; column chooser; compare | Roster, standings, stats, free agency, staff, saves |
| **T3 Profile** | Hero band (team gradient, imagery, identity, key numbers, actions) → tab strip → panels | Player, team, staff, prospect |
| **T4 List–detail** | List left (filters on top), detail right; keyboard moves selection | Inbox, scouting reports, Mods & Graphics |
| **T5 Wizard** | Stepper → step content → Back / Next; summary step | New career |
| **T6 Builder** | Two-sided composition with live validation bar | Trades, contract offers |
| **T7 Live event** | Score bug → feeds and stats → controls | Live game, box score |
| **T8 Calendar** | Month grid or list toggle, jump-to-date | Schedule |
| **T9 Visual board** | Spatial layout (court, bracket, board) | Rotation, bracket, draft board |
| **T10 Form** | Grouped sections with headers, inline help, live preview where useful | Settings, tactics, credits |

Every screen, whatever its template, has **loading** (skeletons shaped like the content), **empty** (EmptyState with an action) and **error** (ErrorState with Retry) states, works at 1280×720 and at 150% UI scale, and is fully keyboard-operable.

## 6. Screen specs

Priority sets the build order (00 §7). "**UI**" marks UI-only additions allowed by scope. "If data" parts are built only if the game has the data; otherwise log them.

### S01 · Splash and loading — T0 — P0
Game wordmark on `Canvas` with the court-line motif; determinate progress bar; step text ("Loading rosters", "Scanning graphics packs · 2,418 faces · 30 logos"); rotating tips. If the game has no logo yet, set a typographic wordmark in the display font and a monogram app icon, and log a final brand pass in `FOLLOWUPS.md`.

### S02 · Title / main menu — T0 — P0
Large left-aligned menu in display caps: Continue, New career, Load, Mods & Graphics, Settings, Credits, Quit. **Continue card** shows the latest save: team logo, GM name, in-game date, record. Background: slow team-color gradient sweep with the court-line motif (static under reduced motion).

### S03 · New career — T5 — P0
Steps: **Database & mods** (roster sets and graphics packs the loader exposes: name, author, version, team/player counts; read-only where the loader can't toggle) → **League options** (only options that exist) → **Choose team** (grid of team tiles: logo, team gradient, last season record, payroll, star player headshot, owner expectation if modeled; filter by conference) → **GM profile** → **Summary**. Picking a team re-tints the wizard live.

### S04 · Load / save — T2 — P0
Save cards (team logo, GM name, in-game date, record, real timestamp, playtime), sort by recent or team, delete with destructive confirm, empty state for no saves.

### S05 · Home hub — T1 — P0
Answers "what needs me now, and what's next?" at a glance.

```
┌─ NEXT GAME · TUE 14 JAN · 7:30 PM · HOME ────────────────────────┐ ┌─ STANDINGS · EAST ──────────────┐
│                                                                  │ │ 1  [NGG] GLACIERS      27-8     │
│  [HCM]  MARINERS                vs                FORGE  [IRV]   │ │▌2  [HCM] MARINERS      24-11    │
│         24-11 · 2ND EAST                  19-16 · 6TH EAST       │ │ 3  [CAP] SENATORS      22-13    │
│         L10 7-3 · W3                      L10 4-6 · L2           │ │ 4  [RVB] OTTERS        21-14    │
│                                                                  │ │ 5  [MDM] MONARCHS      20-15    │
│  KEYS   Win the glass  ·  Slow their pace  ·  Contain #3         │ │ 6  [IRV] FORGE         19-16    │
│                                                                  │ │ - - - - - PLAY-IN - - - - -     │
│  [ PREVIEW ]   [ SET LINEUP ]                                    │ │ [ FULL STANDINGS ]              │
└──────────────────────────────────────────────────────────────────┘ └─────────────────────────────────┘
┌─ INBOX · 3 NEW ───────┐ ┌─ INJURIES · 2 ────────┐ ┌─ TEAM LEADERS ─────────┐ ┌─ CAP SPACE ───────────┐
│! Extension request    │ │(o) J. KOVAC           │ │PTS (o) OKONKWO-V. 24.1 │ │████████████░░ $141.2M │
│  Kovac wants talks    │ │    OUT · 2-3 WEEKS    │ │REB (o) LINDQVIST  10.4 │ │         |CAP  |TAX    │
│  Scout report: CDH    │ │(o) R. NAKAMURA-R.     │ │AST (o) O'MALLEY    8.2 │ │$3.1M UNDER THE TAX    │
│  Owner: objectives    │ │    DAY-TO-DAY         │ │STL (o) KOVAC       1.6 │ │2 SPOTS OPEN           │
│                       │ │                       │ │                        │ │                       │
│[ OPEN INBOX ]         │ │[ MEDICAL ]            │ │[ TEAM STATS ]          │ │[ FINANCES ]           │
└───────────────────────┘ └───────────────────────┘ └────────────────────────┘ └───────────────────────┘
┌─ NEXT 7 DAYS ──────────────────────────────────────────────────────────────────┐ ┌─ LEAGUE NEWS ─────┐
│TUE 14  vs IRV  7:30 PM    THU 16  @ NCV  8:00 PM    FRI 17  @ SMS  9:00 PM     │ │Glaciers win 9th   │
│SUN 19  vs CAP  3:00 PM    TUE 21  vs RWR  7:30 PM   (back-to-back marked B2B)  │ │straight >         │
└────────────────────────────────────────────────────────────────────────────────┘ └───────────────────┘
```

- Featured **Next game** tile: both logos, records, form, time, home/away, "keys to the game" if the game produces them; Preview and Set lineup.
- **Standings** snippet: conference top seeds with the play-in line, user's team highlighted.
- **Inbox** (decisions first), **Injuries**, **Team leaders**, **Cap space**, **Next 7 days**, **League news**. Tiles for data the game lacks are omitted, not faked.
- At 1280 wide: two columns, Next game full width.

### S06 · Inbox and news — T4 — P0
List with filters (All, Decisions, News, Scouting, Medical, Finance) and unread / priority markers; reading pane with a lower-third style headline, sender, date, body (760px max), and decision buttons. Decisions that block Continue are pinned and badged.

### S07 · Roster — T2 — P0
Sub-tabs: Overview, Ratings, Contracts, Stats, Health / Morale (only those with data). Table ↔ card toggle. Column chooser, filter bar (positions, age, rating ranges, status), compare (pick 2–3 → side-by-side `AttributeBar` compare). Row: number, headshot XS + name link, positions, age, OVR / POT badges, contract, status chips. Double-click or Enter opens the profile.

### S08 · Rotation / depth chart — T9 — P0 (if the game has it)
Starting five on a half-court layout by position; bench list with minutes; closing lineup if modeled. Drag and drop **and** keyboard reordering (select, then Alt+Up/Down). A minutes total validator (240 per game) if minutes are set manually.

### S09 · Tactics / playbook — T10 — P1 (if the game has it)
Offense and defense schemes as selectable cards with plain descriptions; labeled sliders; play-call lists. Only settings that exist.

### S10 · Player profile — T3 — P0

```
┌──────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ ///// team gradient (primary -> canvas) /////////////////////////////     [ logo watermark 6% ]      │
│   .-----------.                                                                                      │
│   |           |   #7 · SF/PF · AGE 27 · 6'8" · 220 LB · 6TH SEASON · HARBOR CITY MARINERS            │
│   |  CUTOUT   |   MAXIMILIAN OKONKWO-VANDERBILT                            .-----.  .-----.          │
│   |  (Hero)   |   [HEALTHY]  [EXTENSION ELIGIBLE]  [STARTER]               | 84  |  | 86  |          │
│   |           |   $31.2M · 3 YRS · PLAYER OPTION 2029                      | OVR |  | POT |          │
│   '-----------'                                                            '-----'  '-----'          │
│   /OFFER EXTENSION/    [ SHOP IN TRADES ]    [ SET ROLE ]    [ ... ]                                 │
├──────────────────────────────────────────────────────────────────────────────────────────────────────┤
│  /OVERVIEW/   RATINGS   STATS   CONTRACT   HISTORY   HEALTH   SCOUTING   DEVELOPMENT                 │
├──────────────────────────────────┬─────────────────────────────────┬─────────────────────────────────┤
│ INSIDE SCORING           [78]    │ PLAYMAKING              [71]    │ LAST 10 GAMES                   │
│ Close shot      82 ████████▌     │ Pass accuracy   74 ███████▍     │ PTS ▁▃▅▇▅▆█▅▆▇   24.1           │
│ Driving layup   80 ████████      │ Ball handle     70 ███████      │ W W L W W L W W W L             │
│ Post control    71 ███████       │ Pass vision     69 ██████▉      │ +/-  +4.8 per game              │
├──────────────────────────────────┼─────────────────────────────────┼─────────────────────────────────┤
│ OUTSIDE SCORING          [83]    │ DEFENSE                 [80]    │ STRENGTHS                       │
│ Mid-range       84 ████████▍     │ Perimeter D     83 ████████▎    │ + Pull-up shooting              │
│ Three-point     85 ████████▌     │ Steal           77 ███████▋     │ + Help defense                  │
│ Free throw      81 ████████      │ Block           62 ██████▏      │ - Post scoring                  │
└──────────────────────────────────┴─────────────────────────────────┴─────────────────────────────────┘
```

- **Hero:** team gradient, logo watermark, Hero-size headshot overlapping the band's bottom edge; number, positions, age, height, weight, experience, team; name in Hero type; OVR and POT at XL; contract line; status chips; actions that already exist (one primary).
- **Tabs** (only with data): Overview (attribute groups as bars with group chips, last-10 form, strengths / weaknesses), Ratings (all attributes, grouped), Stats (season / career; per-game, totals, per-36 or advanced only if available), Contract (year-by-year table, options, guarantees), History (transactions, awards), Health, Scouting, Development (rating over time).
- The page takes the player's team colors (01 §4 scope). "Show entity IDs" adds the ID with a copy button to the hero.

### S11 · Team profile — T3 — P0
Hero with XL logo, team colors, arena, record and standing, coach; tabs: Roster, Schedule, Stats, Finances (if visible to the user), History. The whole page takes that team's colors.

### S12 · Schedule / calendar — T8 — P0
Month grid with game chips (home/away, opponent logo, result with score), today marker, back-to-backs marked, list view toggle, jump to date. Clicking a game opens pre-game, live or box score as appropriate.

### S13 · Standings — T2 — P0

```
┌──────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ EAST                                           [ CONFERENCE | DIVISION | LEAGUE ]                    │
│ SEED  TEAM                  W    L    PCT    GB   HOME   AWAY   CONF    L10   STRK                   │
│  1 x  [NGG] GLACIERS        27    8   .771    -    15-3   12-5   18-4    8-2    W9                   │
│▌ 2    [HCM] MARINERS        24   11   .686   3.0   13-5   11-6   15-7    7-3    W3                   │
│  6    [IRV] FORGE           19   16   .543   8.0   11-6    8-10  12-10   4-6    L2                   │
│ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ PLAY-IN (7-10) ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─                                           │
│  7    [CAP] SENATORS        18   17   .514   9.0   10-7    8-10  11-11   5-5    W1                   │
│ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ELIMINATION LINE ─ ─ ─ ─ ─ ─ ─ ─ ─ ─                                           │
│  x clinched playoff · y clinched division · z clinched conference · e eliminated · ▌ your team       │
└──────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Conference / division / league toggle; seed, logo + name link, W, L, PCT, GB, home, away, conference, L10, streak; play-in zone and elimination line; clinch markers; user's team stripe.

### S14 · League leaders and stats — T2 — P1
Leader cards (top five per category with headshots), then full sortable tables with per-game / totals / per-36 segmented control (only what exists).

### S15 · Pre-game — T1 — P0
Matchup hero split down the middle in both teams' colors; records, form, injury reports, probable starters, head-to-head `ComparisonBar`s, keys to the game if produced; Go to game / Sim game (options that exist).

### S16 · Live game — T7 — P0 (if the game shows games live)

```
┌──────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ /[HCM] MARINERS 88/  Q4 4:12  /91 FORGE [IRV]/   TIMEOUTS 2|3   BONUS    [ PLAY ] [ FAST ] [ PAUSE ] │
├──────────────────────────────────────────────────┬───────────────────────────────────────────────────┤
│ PLAY-BY-PLAY                  [ ALL | SCORING ]  │ BOX SCORE                       [ HCM | IRV ]     │
│ Q4 4:12 ▌IRV Mensah-H. pull-up 3PT (91-88)       │ PLAYER          MIN PTS REB AST   FG    3P   +/-  │
│ Q4 4:31 ▌HCM Okonkwo-V. misses driving layup     │ Okonkwo-V.       34  27   6   4  10-19  4-8   +6  │
│ Q4 4:45 ▌HCM TIMEOUT · Mariners 2 left           │ O'Malley-F.      31  18   3   9   7-15  2-6   -2  │
│ Q4 5:02 ▌IRV Abernathy-C. dunk, assist Mensah-H. │ Lindqvist        29  12   9   2   5-11  0-1   +1  │
│ ...  newest on top, scoring plays bold           │ TEAM            240  88  41  21  33-77 11-31      │
├──────────────────────────────────────────────────┼───────────────────────────────────────────────────┤
│ LEAD TRACKER  ▁▂▃▂▁▁▂▄▅▆▅▄▃▂▁▂▃  HCM led by 11   │ SHOT CHART  vector half-court   (*) made (o) miss │
│ Q1 31-24   Q2 22-25   Q3 19-23   Q4 16-19        │ filter: [ ALL | HCM | IRV ]  [ PLAYER v ]         │
├──────────────────────────────────────────────────┴───────────────────────────────────────────────────┤
│ COACH  [ TIMEOUT ]  [ SUBSTITUTIONS ]  [ TACTICS ]   (only controls the game already supports)       │
└──────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

`ScoreBug` across the top; play-by-play (newest first, team-color stripe per play, scoring plays bold, period separators); live box score with team toggle; lead tracker and linescore; shot chart if shot locations exist; coach controls only if the game supports them; sim speed controls that exist.

### S17 · Box score / post-game — T7 — P0
Final score bug, linescore, team totals, player lines (MIN PTS REB AST STL BLK TO PF FG 3P FT +/- — columns the game tracks), top performers as `PlayerCard`s, recap text if generated.

### S18 · Trades — T6 — P0 (if the game has trades)

```
┌──────────────────────────────────────────────────────────────────────────────────────────────────────┐
│ TRADE BUILDER                                                     [ CLEAR ]    /PROPOSE TRADE/       │
├──────────────────────────────────────────────────┬───────────────────────────────────────────────────┤
│ [HCM] MARINERS SEND                              │ [IRV] FORGE SEND                [ change team v ] │
│ ┌──────────────────────────────────────────────┐ │ ┌───────────────────────────────────────────────┐ │
│ │(o) J. KOVAC         PF 29  [79]  $18.2M x2   │ │ │(o) T. ABERNATHY-CLARKE  SG 26 [77]  $21.5M x3 │ │
│ │(#) 2027 1ST (top-4 protected)                │ │ │                                               │ │
│ │[ + ADD PLAYER OR PICK ]                      │ │ │[ + ADD PLAYER OR PICK ]                       │ │
│ └──────────────────────────────────────────────┘ │ └───────────────────────────────────────────────┘ │
│ OUT $18.2M · IN $21.5M                           │ OUT $21.5M · IN $18.2M                            │
│ PAYROLL AFTER ████████████░ $0.4M under tax      │ PAYROLL AFTER ███████████░░ $6.9M under tax       │
├──────────────────────────────────────────────────┴───────────────────────────────────────────────────┤
│ RULE CHECKS (from existing trade logic)   SALARY [PASS]   ROSTER SIZE [PASS]   PICK RULES [PASS]     │
│ AI RESPONSE (only if the game exposes one)   ███████░░░  LEANING YES                                 │
└──────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Two sides with team logos and colors; asset cards (players, picks if modeled); add-asset pickers with search; per-side salary out/in and payroll after; a rule-check bar that shows **the existing trade logic's** verdicts; an AI response meter only if the game exposes one. Propose uses the existing flow.

### S19 · Free agency — T2 — P0 (if the game has it)
Market table (filters: position, age, rating, asking price), cap space bar pinned at the top, offer button per row opening the contract builder (S20) or the existing offer flow.

### S20 · Contract negotiation — T6 — P1 (if the game has it)
Offer builder (years, salary per year, options, guarantees — whatever exists) with live cap impact; player response indicator only if the game provides one; history of offers.

### S21 · Draft — T2 / T9 — P1 (if the game has it)
Big board (rank, prospect headshot + link, position, age, school or club, OVR / POT ranges if scouted); prospect profile uses the T3 template; lottery results (animated reveal is P2); draft night board with on-the-clock team, timer if the game uses one, pick ticker.

### S22 · Scouting — T4 — P1 (if the game has it)
Assignments list, report reading pane with confidence indicators, shortlist actions.

### S23 · Finances — T1 / T2 — P1 (if the game has it)
Overview tiles (payroll, cap space, tax position, revenue and expenses if modeled), multi-season contracts sheet table, `CapBar` per season.

### S24 · Staff — T2 — P2 order
Staff table with roles, ratings, contracts; hire / fire only through existing flows.

### S25 · Training and development — T2 / T10 — P1 (if the game has it)
Practice focus settings that exist; progression table with ▲/▼ deltas; individual development plans.

### S26 · Playoff bracket — T9 — P1 (if the game has it)
Broadcast-style bracket: seeds, logos, series scores, the user's path highlighted; play-in round if modeled.

### S27 · Awards, history and records — T2 — P2 order
Season awards with headshots, champions by year, record books.

### S28 · Global search — UI — P1
Ctrl+K opens a command-palette style overlay: grouped results (players with headshots, teams with logos, staff), keyboard navigation, Enter opens, recent searches. Searches existing in-memory data or existing query services only.

### S29 · Settings — T10 — UI — P0
Sections: **Display** (UI scale 90–150%, density, window mode, remember window), **Theme** (team colors in interface, rating palette, ticker on/off), **Accessibility** (reduced motion, color-vision-safe palette shortcut), **Units and formats** (imperial / metric, date format), **Mods** (show entity IDs, Reload graphics, Open mods folder), **Advanced** (Open logs folder). All persist across restarts.

### S30 · Mods & Graphics — T4 — UI — P0
Specified in 05 §7.

### S31 · Credits and licenses — T10 — UI — P0
Team and contributors, then every bundled font, icon set, library and image with its license, generated from or kept in sync with `ASSET_LICENSES.md`.

### S32 · Dialogs, toasts and crash dialog — P0
Captured as screens in the matrix so they're reviewed like everything else.

### S33 · Component Gallery — dev-only — P0
03 §2.

## 7. Responsive rules

| Width | Nav rail | Header | Content |
|---|---|---|---|
| ≥ 1920 | Expanded | Everything visible | Dashboards up to 4 tiles per row |
| 1440–1919 | Expanded | Everything visible | 3 tiles per row |
| 1280–1439 | Collapsed (icons) | Breadcrumb hidden, search as icon | 2 tiles per row; tables hide low-priority columns first (column priority set per table) |

At every size: no horizontal page scroll, no overlap, no clipped primary content. Tables may scroll horizontally **inside** their own frame with the name column frozen.
