# 01 · Design Language

How the game should look, move and read. Values here are defaults; token **names** may follow the project's existing convention (record the mapping in `AUDIT.md` → Decisions). Every color value below has been contrast-checked against the surfaces it sits on.

---

## 1. References — take the traits, not the artwork

The UI should feel like it belongs on the same shelf as these, without copying any of their layouts, fonts, icons or art.

**NBA 2K — MyNBA (franchise mode)**
- The user's team takes over the interface: its colors and logo are everywhere, and the shell re-tints when you change teams.
- The home screen is a hub of large, live tiles (next game, standings, front-office tasks, news), not a menu list.
- Player imagery is the hero: big cutouts on team-color gradients; ratings in bold, tier-colored chips.
- Each screen has horizontal sub-tabs; time moves through calendar-driven sim controls; ratings pages stay legible despite density because tiers carry the signal.

**EA SPORTS NBA Live (franchise / The League)**
- Hard geometry: slanted edges, angled tab flags, parallelogram buttons, cut corners.
- Kinetic motion: panels slide and snap in, counters tick up, nothing simply appears.
- Bold condensed headlines that lean italic in hero and score contexts; broadcast lower-thirds and score bugs.

**Football Manager**
- Information architecture: an inbox-driven loop, a single Continue button that advances time and refuses to move past decisions that need you, back/forward history, breadcrumbs.
- Everything is a link: any player or team name opens a profile, shows a hover card, and has a right-click action menu.
- Power-user tables: choose columns, sort, filter, compare, search from anywhere, compact density.
- Mod-friendly by design: faces and logos dropped into user folders, missing assets degrade gracefully.

**TV broadcast packages**
- Score bugs, bottom tickers, lower-thirds, head-to-head comparison bars, team-color wipes, tabular numerals that never jitter.

## 2. Pillars

Use these to settle anything the spec doesn't.

1. **Broadcast, not spreadsheet.** Every screen reads like a TV graphics package: condensed caps headlines, hard edges, one clear focal point, team color as the light source.
2. **Your team is the theme.** The user's team colors and logo drive the shell accent; any page about another team takes that team's colors; matchups show both.
3. **Faces first.** Player imagery is the hero element. Mod photos must look great; missing photos must look intentional.
4. **Depth on demand.** Default views are clear and scannable (2K); every table can go dense (FM) with column choice, sorting, filters and compact density.
5. **Instant and unbreakable.** Navigation feels immediate, the UI never freezes, and a broken mod never crashes the game or blanks a screen.

## 3. Color tokens

Dark is the primary and default theme. Keep a light variant only if the project already ships one.

| Token | Default | Use |
|---|---|---|
| `Canvas` | `#0A0E14` | App background, header band fade target |
| `CanvasRaised` | `#0F1520` | Page background |
| `Surface1` | `#141B27` | Cards, panels, table body |
| `Surface2` | `#1A2331` | Table header, hover, nested panels |
| `Surface3` | `#222D3E` | Pressed, selected rows, inputs |
| `Stroke` | `#2A3547` | 1px dividers (decorative only) |
| `TextPrimary` | `#F2F5F9` | Primary text |
| `TextSecondary` | `#A9B4C4` | Secondary text |
| `TextMuted` | `#8C99AD` | Captions, meta (still ≥ 4.5:1 on every surface) |
| `TextDisabled` | `#5B6779` | Disabled controls only (exempt from contrast) |
| `AccentFallback` | `#3D8BFF` | Accent when team colors are off or unavailable |
| `Positive` | `#2BD67B` | Wins, gains, healthy |
| `Negative` | `#FF6B78` | Losses, injuries, violations |
| `Warning` | `#FFA53D` | Day-to-day, expiring, near a threshold |
| `Info` | `#4CC2FF` | Neutral notices |
| `PositiveCvd` | `#5AAEFF` | Positive in color-vision-safe mode |
| `NegativeCvd` | `#FF9F43` | Negative in color-vision-safe mode |
| `FocusRing` | `#F2F5F9` | 2px focus outline, offset 2px outside the control |
| `Scrim` | `#0A0E14` @ 72% | Behind text placed over imagery |

**Measured contrast (WCAG 2.x):**

| Pair | Worst case |
|---|---|
| `TextPrimary` on any surface | 12.69 (on `Surface3`) |
| `TextSecondary` on any surface | 6.62 |
| `TextMuted` on any surface | 4.81 |
| `Positive` / `Negative` / `Warning` / `Info` as text on any surface | 7.28 / 5.04 / 7.07 / 6.92 |
| `PositiveCvd` / `NegativeCvd` on any surface | 5.90 / 6.80 |
| `AccentFallback` on any surface | 4.19 |
| `FocusRing` on any surface | 12.69 |

Rules:
- Text uses text tokens only. Semantic colors may color text, icons and chips, never large backgrounds.
- `Stroke` hairlines are decorative; a control's boundary must also be identifiable by fill or position.
- The focus ring sits **outside** the control, on the surface behind it, so it stays visible even on white or yellow team accents.

## 4. Team theming

Every team has primary and secondary colors in its data (base roster or mods). Derive the accent so it keeps the team's identity and stays legible:

```
Accent(team):
  if contrast(Primary, Surface2) >= 3.1             -> Primary
  else lighten Primary in HSL lightness steps of 0.02, up to +0.25,
       until contrast >= 3.1                          -> lightened Primary   (keeps the team's hue)
  else if contrast(Secondary, Surface2) >= 3.1      -> Secondary
  else lighten Primary without the +0.25 cap          -> lightened Primary
OnAccent  = #FFFFFF or #000000, whichever contrasts more with Accent
AccentAlt = the other team color if contrast >= 3.1 vs Surface2, else Accent lightened +0.12
TeamPrimaryRaw / TeamSecondaryRaw = unmodified, for large fills only (hero bands, header band, logo fallback plates),
                                    with OnPrimaryRaw = #FFFFFF or #000000, whichever contrasts more
```

- Derive against 3.1; tests assert ≥ 3.0, so rounding differences never flake.
- **OnAccent must be pure `#FFFFFF` / `#000000`.** With those, the worst case over every possible color is 4.61:1. With the off-white/off-black text tokens it drops to 4.21:1 and fails.
- Checked against the 30 fixture teams (06 §1): worst Accent vs `Surface2` 3.11, worst OnAccent 4.64. Purple, blue, red and green teams keep their hue; navy and black teams fall back to bright secondaries.
- Map the derived accent onto the base theme's accent resources too (for Fluent: `SystemAccentColor` and its Light1–3 / Dark1–3 variants) so untouched controls follow.

**Scope.** The shell uses the user's team. A page about another team (team profile, player profile, opponent preview) overrides the accent resources at its root so its whole subtree takes that team's colors. Matchup views color each side with its own team.

**Gradients.** Hero and header bands run `TeamPrimaryRaw` at 100% → 0% over `Canvas`, at roughly 110°. Text never sits on the raw gradient: it sits on the solid region with `OnPrimaryRaw`, or on `Scrim`.

**Setting.** "Team colors in interface" on/off. Off uses `AccentFallback` everywhere.

**Never** use a raw team color as small text on the dark canvas.

## 5. Typography

Bundle SIL OFL fonts as embedded resources and ship their license files.

| Role | Family | Weight | Size / line height | Case and tracking |
|---|---|---|---|---|
| Hero (player name, final score) | Barlow Condensed | 800 | 44–64 / 1.0 | UPPER, +1%; italic allowed |
| H1 page title | Barlow Condensed | 700 | 32 / 1.1 | UPPER, +2% |
| H2 section | Barlow Condensed | 700 | 24 / 1.15 | UPPER, +2% |
| H3 card title | Barlow Condensed | 600 | 20 / 1.2 | UPPER, +3% |
| Stat XL (OVR, big numbers) | Barlow Condensed | 800 | 48–72 / 1.0 | Tabular figures |
| Label caps | Inter | 600 | 11–12 / 1.3 | UPPER, +8% |
| Body | Inter | 400 / 500 | 14 / 1.45 | Sentence case |
| Dense table | Inter | 500 | 13 / 1.3 | Sentence case, tabular figures |
| Caption | Inter | 400 | 12 / 1.35 | Sentence case |

- Acceptable OFL alternates: display — Saira Condensed, Oswald, Teko; body — Barlow, IBM Plex Sans. Choose once; record it.
- Essential text is never below 12px; 11px only for caps labels.
- At most three type sizes inside one panel.
- Numbers in tables are right-aligned with tabular figures. Verify by rendering a column of `111.1` over `888.8`: the decimal points must line up.
- Italic is for hero and score contexts only, never body text.

## 6. Shape and geometry

- **Signature angle: 12°.** Primary buttons, the Continue button, tab flags, section-header flags, score-bug segments and the ticker label are parallelograms leaning 12°. Only the background leans — text and icons stay upright (02 §7).
- **Cut corners:** featured tiles and hero bands get one 16px 45° cut (top-right by default).
- **Radii:** 0 for structural bands and tabs; 2px for chips and inputs; 4px maximum for cards. Fully round only for XS/S avatars and toggle thumbs.
- **Accent stripe:** 3px on the left edge of the active nav item, the selected table row and featured cards.
- **Hairlines** are 1px `Stroke`. Elevation comes from stepping surfaces, not borders. Shadows only on floating layers (menus, dialogs, toasts): `0 8 24 0 #59000000`.

## 7. Layout and spacing

- 4px base grid. Spacing scale: 4, 8, 12, 16, 24, 32, 48, 64. Nothing off-scale.
- Page padding 24px at ≥ 1440 wide, 16px below. Card padding 16px (12px compact).
- **Density modes:** Comfortable — table rows 40px, list items 56px. Compact — 28px and 44px.
- Dashboards use a 12-column fluid grid with 16px gutters. Data screens use the full width. Reading content (news, scouting reports) caps at 760px.
- **Breakpoints by window width:** ≥ 1920 wide; 1440–1919 standard; 1280–1439 compact (nav rail collapses to icons; the page breadcrumb hides, then header search shrinks to an icon). Minimum window 1280×720.
- Interactive targets are at least 24×24px.

## 8. Texture and layering

- `Canvas` carries a subtle top-to-bottom gradient (`Canvas` → `CanvasRaised`) and, optionally, a diagonal pinstripe at ≤ 3% opacity.
- Hero stack, back to front: team gradient → oversized cropped logo watermark at 6–8% → player cutout → scrim → text.
- A court-line motif, drawn as vector paths (never a photo of a real court), may appear at ≤ 5% in empty states and loading screens.

## 9. Iconography

- One icon family across the app, open-licensed, with fill-based paths so they render directly as `PathIcon` geometry (Phosphor — MIT, or Material Symbols Outlined — Apache-2.0). A stroke-based set (Lucide, Tabler) needs a stroke-aware icon control; avoid the extra work unless the project already uses one.
- Sizes 16 / 20 / 24. Nav items and primary actions pair icon with text; icon-only buttons need a tooltip and an automation name.
- No emoji, no bitmap icons, no mixed sets.

## 10. Motion

| Element | Motion | Duration and easing |
|---|---|---|
| Page change | Fade + 24px slide in the navigation direction | 200ms, cubic-out |
| Sub-tab change | Active flag slides to the new tab; content cross-fades | 160ms |
| Tile / card hover | Surface steps up, scale 1.02, accent stripe grows | 120ms |
| List entrance | Fade-up stagger, first 8 items only, 20ms apart | 180ms each |
| Big numbers (OVR, score, cap) | Count up on first show | 400ms |
| Dialog | Fade + scale 0.98 → 1 | 160ms |
| Toast | Slide in from the right | 180ms |
| Ticker | Continuous scroll at 40px/s, pauses on hover | linear |

- **Reduced motion** (setting, and the OS preference where it can be read): no slides, scaling, stagger or count-ups; fades ≤ 100ms; the ticker pages every 6s instead of scrolling.
- Animate opacity and transforms only. Never animate width, height or margins in lists.
- Motion is off in screenshot capture so images are deterministic.

## 11. Imagery

**Player headshots**
- Sizes: XS 24, S 32, M 48, L 96, XL 160, Hero 320–420 tall.
- Sources vary: 1:1 cutouts, landscape cutouts around 1.37:1, portrait photos, opaque photos. The frame has a fixed aspect; the image fills it (`UniformToFill`) anchored bottom-center. Never stretch.
- Cutouts sit on a team-gradient backdrop. Opaque photos get a soft radial vignette so they don't look pasted on.
- XS and S crop to a circle or rounded square focused on the top-center (the face).

**Team logos**
- Sizes 16, 20, 24, 32, 48, 64, 128, 256. Fit `Uniform`, centered. Never upscale beyond 2× the source; above that, show it at 2× centered on the plate.

**Fallbacks** — intentional, vector, never empty boxes:
- Player: a neutral silhouette tinted with the team gradient; jersey number at L and above.
- Team: a monogram plate — abbreviation in the display font on `TeamPrimaryRaw` with a `TeamSecondaryRaw` stripe.
- League or competition: a text badge.

Text over imagery always sits on `Scrim` or a solid band.

## 12. Data visualization

**Rating tiers.** Six tiers, the number always visible inside the chip, tier name in the tooltip. Chip text is pure black or white, whichever contrasts more.

| Tier | 25–99 scale | Default palette | Chip text | CVD-safe palette | Chip text |
|---|---|---|---|---|---|
| Elite | 90+ | `#F5C542` | black 12.95 | `#FEE838` | black 16.81 |
| All-Star | 80–89 | `#A77BFF` | black 6.92 | `#DAC65B` | black 12.21 |
| Starter | 70–79 | `#58A6FF` | black 8.31 | `#B4A76F` | black 8.70 |
| Rotation | 60–69 | `#2EC27E` | black 9.13 | `#908B78` | black 6.15 |
| Bench | 50–59 | `#C5CDD8` | black 13.10 | `#6F7073` | white 4.95 |
| Fringe | < 50 | `#6B7686` | white 4.60 | `#4F576C` | white 7.21 |

- The default palette follows the loot-rarity ladder gamers already read (gray → white → green → blue → purple → gold). The CVD-safe palette is sampled from *cividis* and rises monotonically in lightness, so tiers stay ordered without hue.
- If the game rates on a 1–20 scale (FM style), map tiers to 18–20, 16–17, 14–15, 11–13, 8–10, 1–7. Record the mapping in `AUDIT.md`.
- Setting: rating palette Default / Color-vision safe.

**Attribute bars:** 4px track in `Surface3`, tier-colored fill, value right-aligned; compare mode adds a tick for the second player; group headers show the group average chip.

**Deltas:** ▲/▼ plus color (`Positive` / `Negative`, or the CVD pair). Never color alone.

**Form:** sparkline of the last 10 games (1.5px line in `TextSecondary`, last point in the accent) and a W/L strip (W filled `Positive`, L outlined `Negative`, letters visible).

**Comparisons:** head-to-head bars use each team's accent, values at the ends.

**Shot chart** (only if the game records shot locations): vector half-court in `Stroke` at 60%; makes are filled accent dots, misses are hollow `TextMuted` rings.

**Cap bar:** stacked horizontal bar of committed salary by contract type, with labeled vertical markers for the thresholds the game models (cap, tax, aprons). The segment beyond a threshold uses `Warning` or `Negative`.

**Charts in general:** no 3D, no pie charts beyond three slices, caption-style axis labels, gridlines in `Stroke` at 50%.

## 13. Voice and microcopy

- Headings and labels are short UPPERCASE nouns: ROSTER, LAST 10, CAP SPACE. Body text is sentence case.
- Headlines in news and the inbox can carry broadcast tone. UI actions are plain and specific: "Offer contract", not "Submit".
- Formats: `$24.5M`, `$850K`, `6'7"`, `215 lb`, `47.3%`, records `24–11` (en dash), streaks `W4` / `L2`, ordinals `1st`. Metric units via setting.
- Empty states say what will appear and how to get it: "No injuries. Players hurt in games or practice show up here."

## 14. Anti-patterns (reject in review)

- The stock Fluent look: 8px-rounded cards everywhere, gray-on-gray, default accent blue, default scrollbars.
- Center-aligned numbers, proportional digits in stat columns, mixed decimal formats.
- Text directly on photos or gradients without a scrim or solid band.
- A raw team color used as small text on the dark canvas.
- Empty panels, or "No data" in gray with no context or action.
- Truncated names without a tooltip; ellipsis inside numbers.
- Emoji, clip-art, stretched or pixelated images, distorted aspect ratios.
- More than one primary (accent-filled) button in a view; the header's Continue counts as the shell's.
- Modal dialogs for information that doesn't need a decision — use a toast.
- Anything recognizably lifted from 2K, EA or NBA properties: logos, fonts, iconic layouts, art.
