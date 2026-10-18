# 03 · Component Library

Every screen is assembled from these. Build each once, theme it from tokens, and reuse it — no one-off look-alikes inside views.

---

## 1. Rules for every component

- Lives in `Controls/` (templated controls, decorators) or `Components/` (composite user controls), themed from `Themes/`.
- Uses tokens only (01 §3–7). No literals.
- **Interactive states:** default, `:pointerover`, `:pressed`, `:focus-visible`, `:disabled`, plus `:selected` / `:checked` where relevant, plus **loading** if it fetches anything.
- **Density:** honors Comfortable / Compact where it has rows or padding.
- **Keyboard:** reachable with Tab, operable with Enter/Space, arrow keys inside composite controls (tab strips, segmented controls, grids, lists).
- **Accessibility:** icon-only controls and images set `AutomationProperties.Name` (a headshot reads "Photo of {player}"). Truncated text has a tooltip with the full value.
- **Motion:** per 01 §10, removable by reduced motion.
- **Gallery:** appears in the Component Gallery in every state and size.

"**Req**" components are required. "**If data**" ones are required only if the game has the underlying data or behavior; otherwise log them in `FOLLOWUPS.md`.

## 2. Component Gallery (dev-only)

A screen reachable with a `--gallery` launch argument or a debug-menu entry, never in release navigation. Sections mirror §3–§8 below, each component shown in all variants, sizes and states (force hover/pressed/focus with classes or attached pseudo-class helpers), on both `Surface1` and a team-gradient hero, with a team switcher at the top that re-tints live (use at least: navy `HCM`, white `NGG`, yellow `SMS`, lime `NCV`, black `IRV` from the fixtures). The gallery is screenshot-tested like any screen.

## 3. Foundations

| Component | Variants / sizes | Notes | |
|---|---|---|---|
| Text styles | `h1` `h2` `h3` `hero` `stat-xl` `label-caps` `body` `body-strong` `table` `caption` | Classes on `TextBlock`; tracking and case per 01 §5 | Req |
| `Icon` | 16 / 20 / 24 | `PathIcon` with geometry resources | Req |
| `SkewBorder` | angle, fill, stroke | Parallelogram background, upright content, corner-aware hit test | Req |
| `CutCornerBorder` | corners, size | Optional child clipping | Req |
| `Divider` | horizontal / vertical | 1px `Stroke` | Req |

## 4. Actions

| Component | Variants / sizes | Behavior | |
|---|---|---|---|
| `Button` | primary (skewed, accent fill), secondary (outline), ghost, danger, icon; S / M / L | Loading state shows a spinner and keeps width; disabled has a reason tooltip where the reason isn't obvious | Req |
| `ContinueButton` | ready, blocked, game-day, simulating | The shell's main CTA (04 §4). Label plus next-event subtitle; dropdown for sim options the game supports; Stop while simulating | Req |
| `SplitButton` | primary + menu | Sim-to options, "Offer contract ▸ variants" | Req |
| `EntityLink` | player, team, staff, competition | Any entity name in the UI: click opens profile, hover shows the hover card after 400ms, right-click / Shift+F10 opens actions that already exist | Req |
| `ToggleSwitch`, `CheckBox`, `RadioButton` | — | Restyled; label click toggles | Req |

## 5. Navigation

| Component | Variants | Behavior | |
|---|---|---|---|
| `NavRail` / `NavItem` | expanded 220px, collapsed 64px | Icon + label; active item has the 3px accent stripe; count badges; auto-collapses below 1440 wide; tooltip labels when collapsed | Req |
| `PageHeader` | with/without breadcrumb, actions | H1 title, optional subtitle, breadcrumb, right-aligned actions (one primary at most) | Req |
| `SkewTabStrip` | page tabs | Parallelogram flags; the active flag slides; Left/Right arrows move; Ctrl+Tab cycles | Req |
| `SegmentedControl` | 2–5 options | Per Game / Totals / Per 36; arrow keys | Req |
| `Breadcrumb` | — | Collapses middle items to "…" when narrow | Req |
| `Stepper` | wizard steps | Done / current / upcoming; Back / Next; validation per step | Req |

## 6. Inputs

| Component | Variants | Behavior | |
|---|---|---|---|
| `TextBox` | default, search (icon + clear), numeric | Search clears with Esc | Req |
| `ComboBox` | default, with icons/logos | Restyled popup; type-to-select | Req |
| `NumericUpDown` / money input | currency, years | Formats via the formatting service; validates range | If data |
| `Slider` | labeled ticks | Minutes, tactics sliders; value label; keyboard step | If data |
| `RangeFilter` | age, rating ranges | Two numeric inputs or a two-thumb slider | Req |
| `FilterBar` | search + chips + dropdowns + reset | Shows the active filter count; Reset restores defaults | Req |
| `ChipToggle` | position chips, status chips | Multi-select; keyboard toggles | Req |

## 7. Data display

| Component | Variants / sizes | Notes | |
|---|---|---|---|
| Table styling | Comfortable / Compact | Sticky header, sort glyphs, frozen name column, zebra at a 2% surface step, selected row with accent stripe, right-aligned numerics, row actions menu, column chooser, empty / loading / error overlays | Req |
| `ColumnChooser` | — | Flyout of check items; persists per screen | Req |
| `Card` / `Tile` | standard, featured (team gradient, cut corner), compact | Title in H3 caps, optional action link; hover and focus states if clickable | Req |
| `StatTile` | default, delta | Big number (`stat-xl`, count-up), label caps, delta vs league average or rank | Req |
| `RatingBadge` | S / M / L / XL | Tier palette (01 §12), number always shown, tier in tooltip | Req |
| `AttributeBar` | single, compare | Label, bar, value, delta, compare tick | Req |
| `PositionChip` | PG SG SF PF C, hybrids | Neutral fill, caps label | Req |
| `StatusChip` | injury (Out, Doubtful, Questionable, Probable, Day-to-day), contract (Expiring, Player option, Team option, Two-way, Rookie scale), role, transaction | Color plus text, never color alone | Req |
| `PlayerHeadshot` | XS S M L XL Hero | Resolver-backed; fallback first; injury dot; optional jersey number tag; cancels stale loads when recycled | Req |
| `TeamLogo` | 16–256 | Resolver-backed; monogram fallback | Req |
| `PlayerCard` | compact, full | Headshot, name, positions, OVR/POT, age, contract line, status chips | Req |
| `TeamCard` | compact, full | Logo, name, record, standing, payroll | Req |
| `HoverCard` | player, team | Rich tooltip: headshot/logo, key facts, OVR, status, entity ID when "Show entity IDs" is on | Req |
| `Sparkline` | line, W/L strip | 01 §12 | Req |
| `ComparisonBar` | head-to-head | Two team colors, values at ends | Req |
| `CapBar` | season, multi-season | Thresholds the game models, labeled | If data |
| `ShotChart` | game, season | Vector half-court | If data |
| `Linescore` | by period | Broadcast style | Req |
| `ScoreBug` | compact, full | Logos, abbreviations, scores, period, clock, timeouts, bonus — only fields the game has | Req |
| `Ticker` | scores, news | Pause on hover; paged under reduced motion; toggle in settings | Req |
| `BracketView` | playoffs | Seeds, logos, series scores | If data |
| `CalendarMonth` | month grid | Game chips (home/away, opponent logo, W/L with score) | Req |
| `Timeline` | transactions, history | Date rail, entity links | If data |
| `KeyValueList` | bio facts | Label caps / value body | Req |
| `NewsCard` / `InboxItem` | unread, priority, decision-required | Lower-third style headline; decision badge; actions | Req |

## 8. Feedback and overlays

| Component | Variants | Behavior | |
|---|---|---|---|
| `Toast` | info, success, warning, error | Top-right stack, auto-dismiss 5s (errors stay), optional action, keyboard-dismissable | Req |
| `Dialog` | confirm, destructive confirm, form | Esc cancels, Enter confirms the default, focus trapped inside and restored after | Req |
| `ProgressOverlay` | determinate, indeterminate | Sim / load / save; current step text; Stop when supported | Req |
| `Skeleton` | text, row, card, headshot | Shapes match the final layout; shimmer off under reduced motion | Req |
| `EmptyState` | default, filtered | Icon, headline, one line of explanation, one action ("Clear filters") | Req |
| `ErrorState` | inline, page | Plain-language message, Retry, Copy details | Req |
| `Banner` | season phase, deadline | "TRADE DEADLINE IN 3 DAYS" style strip under the page header | Req |
| `Badge` | count, dot | On nav items and tabs | Req |
| `CrashDialog` | — | 02 §14 | Req |
| Scrollbar, tooltip, context menu, flyout styling | — | Thin scrollbars that widen on hover; menus with icons and shortcuts | Req |

## 9. Documentation

When the library is done, write `docs/ui/DESIGN_SYSTEM.md`: tokens with values and usage, type styles, spacing and density, every component with its variants, states, code usage example and gallery screenshot path, the team-theming algorithm, the image-disposal decision (02 §11), and how to add a new component without breaking the lint.
