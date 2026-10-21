# Visual QA

> Rubric, severities and protocol: spec 06 §6. Reviewers are fresh subagents that have not seen the implementation. An S2 must cite a spec rule.

## Summary
| Screen | Screenshots reviewed | Open S1 | Open S2 | Open S3 | Last reviewed (run / turn) | Clean |
|---|---|---|---|---|---|---|
| S33 Component Gallery | 18 (9 sections × 1080 + full) | 0 | 20 | 24 | R1 / T4 (pass 2) | no |

## Defects
| ID | Screen | Screenshot | Sev | Rule (spec ref) | Defect | Fix commit | Verified by (run / turn) |
|---|---|---|---|---|---|---|---|
| Q001 | S33 Inputs | S33_gallery-inputs_1920x1080 | S1 | R6/R7 | RangeFilter numeric boxes clip their digits (19→'1(') | d368efa | R1 / T4 (pass 2) |
| Q002 | S33 Inputs | S33_gallery-inputs_1920x1080 | S1 | R5, 06 §6.2 | Placeholder text ~2.25:1 on field fill | d368efa | R1 / T4 (pass 2) |
| Q003 | S33 Foundations | S33_gallery-foundations_1920x1080 | S2 | 01 §13 | Gallery section tab strip in sentence case | d368efa | R1 / T4 (pass 2) |
| Q004 | S33 Foundations | S33_gallery-foundations_1920x1080 | S2 | 03 §1 selected | Team switcher shows no active team | d368efa | R1 / T4 (pass 2) |
| Q005 | S33 Foundations | S33_gallery-foundations_1920x1080 | S2 | 01 §5 | Caption text reads ~11px (caption role, header subtitle, toasts, hover meta, timeline) | d368efa | R1 / T4 (pass 2) |
| Q006 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 01 §3 FocusRing | Focus column: secondary/ghost/danger/default show no focus ring | d368efa | R1 / T4 (pass 2) |
| Q007 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 01 §3 FocusRing | Primary focus ring missing top/bottom edges; EntityLink focus ring incomplete | d368efa | R1 / T4 (pass 2) |
| Q008 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 01 §3 TextDisabled | Disabled ghost/danger look enabled | d368efa | R1 / T4 (pass 2) |
| Q009 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 01 §5 | Continue subtitles ~11px | d368efa | R1 / T4 (pass 2) |
| Q010 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 03 §2 | Toggles/check/radio lack hover/pressed/focus/disabled states; only Continue on a hero | reopened as Q068 | R1 / T4 (pass 2) |
| Q011 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 03 §4 SplitButton | SplitButton not primary + menu; open menu not shown | reopened as Q067 | R1 / T4 (pass 2) |
| Q012 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 04 §1 (B) | Collapsed rail shows dots on every item; dots over icon strokes | d368efa | R1 / T4 (pass 2) |
| Q013 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 01 §5 | Nav labels and segment options ~11px sentence case with caps tracking | d368efa | R1 / T4 (pass 2) |
| Q014 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 03 intro; 04 §2 | PageHeader breadcrumb has no separators | d368efa | R1 / T4 (pass 2) |
| Q015 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 03 §5 Stepper | Stepper lacks Back/Next and invalid-step state | d368efa | R1 / T4 (pass 2) |
| Q016 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 03 §2 | NavItem, SkewTabStrip, SegmentedControl show no hover/pressed/focus/disabled | reopened as Q073, Q074 | R1 / T4 (pass 2) |
| Q017 | S33 Inputs | S33_gallery-inputs_1920x1080 | S2 | 01 §3 FocusRing | Focused text box identical to default | reopened as Q078 | R1 / T4 (pass 2) |
| Q018 | S33 Inputs | S33_gallery-inputs_1920x1080 | S2 | 03 §6 Slider | Slider ticks unlabeled | reopened as Q079 | R1 / T4 (pass 2) |
| Q019 | S33 Inputs | S33_gallery-inputs_1920x1080 | S2 | 03 §6 ComboBox | No logo ComboBox or open popup | d368efa | R1 / T4 (pass 2) |
| Q020 | S33 Data | S33_gallery-data_1920x1080 | S2 | 01 §12 Deltas | StatTile deltas grey, not Positive/Negative | d368efa | R1 / T4 (pass 2) |
| Q021 | S33 Data | S33_gallery-data_1920x1080 | S2 | 01 §6; 03 §7 | Featured tile has no visible cut corner | d368efa | R1 / T4 (pass 2) |
| Q022 | S33 Data | S33_gallery-data_1920x1080 | S2 | 01 §12; 03 §7 | AttributeBar group header with average chip missing | d368efa | R1 / T4 (pass 2) |
| Q023 | S33 Data | S33_gallery-data_1920x1080 | S2 | 01 §5 | StatTile sub-lines and card action links ~11px, condensed face | d368efa | R1 / T4 (pass 2) |
| Q024 | S33 Data | S33_gallery-data_1920x1080 | S2 | 03 §2; 03 §7 | Card compact variant and hover/focus states missing | d368efa | R1 / T4 (pass 2) |
| Q025 | S33 Tables | S33_gallery-tables_1920x1080 | S2 | 01 §6; 03 §7 | Selected row shows no 3px accent stripe | reopened as Q086 | R1 / T4 (pass 2) |
| Q026 | S33 Tables | S33_gallery-tables_1920x1080 | S2 | 03 §8; 01 §14 | Thick always-visible horizontal scrollbars with arrows | d368efa | R1 / T4 (pass 2) |
| Q027 | S33 Media | S33_gallery-media_1920x1080 | S2 | 01 §11 | Hero headshot only 240 tall | d368efa | R1 / T4 (pass 2) |
| Q028 | S33 Media | S33_gallery-media_1920x1080 | S2 | 01 §11; 03 §2 | Logo sizes 20 and 256 missing | d368efa | R1 / T4 (pass 2) |
| Q029 | S33 Media | S33_gallery-media_1920x1080 | S2 | 01 §11; R8 | Monogram stripe crosses the letters; illegible at small sizes | d368efa | R1 / T4 (pass 2) |
| Q030 | S33 Media | S33_gallery-media_1920x1080 | S2 | R6; 01 §14 | HoverCard contract line clipped mid-glyph without ellipsis | d368efa | R1 / T4 (pass 2) |
| Q031 | S33 Sports | S33_gallery-sports_1920x1080 | S2 | 03 §7; 01 §4 | Calendar NCV fixtures show the IRV logo | d368efa | R1 / T4 (pass 2) |
| Q032 | S33 Sports | S33_gallery-sports_1920x1080 | S2 | 01 §12 Shot chart; 01 §3 | Shot chart: no court lines; labels straddle zone borders; strong fills | d368efa | R1 / T4 (pass 2) |
| Q033 | S33 Sports | S33_gallery-sports_1920x1080 | S2 | 01 §12 Cap bar | Third cap-bar marker (apron) unlabeled | d368efa | R1 / T4 (pass 2) |
| Q034 | S33 Feedback | S33_gallery-feedback_full | S2 | 03 §8; 03 §4 | Destructive dialog confirm uses accent primary | d368efa | R1 / T4 (pass 2) |
| Q035 | S33 Feedback | S33_gallery-feedback_full | S2 | 03 §8; 03 §2 | Confirm and form dialogs, tooltip, context menu and flyout styling not shown | reopened as Q102 | R1 / T4 (pass 2) |
| Q036 | S33 Feedback | S33_gallery-feedback_1920x1080 | S2 | 03 §8; 01 §6 | Headshot skeleton round; L headshots are square | d368efa | R1 / T4 (pass 2) |
| Q037 | S33 Foundations | S33_gallery-* | S3 | — | Tabular sample far from its label | reopened as Q065 | R1 / T4 (pass 2) |
| Q038 | S33 Foundations | S33_gallery-* | S3 | — | Type samples unnamed | d368efa | R1 / T4 (pass 2) |
| Q039 | S33 Foundations | S33_gallery-* | S3 | — | Gradient cut-corner sample hides its cut | reopened as Q064 | R1 / T4 (pass 2) |
| Q040 | S33 Foundations | S33_gallery-* | S3 | — | Gradient banding in header band | d368efa | R1 / T4 (pass 2) |
| Q041 | S33 Actions | S33_gallery-* | S3 | — | Button matrix rows touch | d368efa | R1 / T4 (pass 2) |
| Q042 | S33 Actions | S33_gallery-* | S3 | — | 'DEFAULT' variant row label | d368efa | R1 / T4 (pass 2) |
| Q043 | S33 Actions | S33_gallery-* | S3 | — | Selected chip barely distinguishable | reopened as Q072 | R1 / T4 (pass 2) |
| Q044 | S33 Actions | S33_gallery-* | S3 | — | 'Team colours' wording vs spec | d368efa | R1 / T4 (pass 2) |
| Q045 | S33 Navigation | S33_gallery-* | S3 | — | Breadcrumb tail vs active tab | d368efa | R1 / T4 (pass 2) |
| Q046 | S33 Inputs | S33_gallery-* | S3 | — | NumericUpDown taller than fields | reopened as Q080 | R1 / T4 (pass 2) |
| Q047 | S33 Inputs | S33_gallery-* | S3 | — | Reset enabled at defaults; spinner bounds; active chips | d368efa | R1 / T4 (pass 2) |
| Q048 | S33 Inputs | S33_gallery-* | S3 | — | Disabled field fill lighter | d368efa | R1 / T4 (pass 2) |
| Q049 | S33 Data | S33_gallery-* | S3 | — | Hyphen-minus, hyphen ranges, letter x | d368efa | R1 / T4 (pass 2) |
| Q050 | S33 Tables | S33_gallery-* | S3 | — | Numeric header right padding | reopened as Q088 | R1 / T4 (pass 2) |
| Q051 | S33 Tables | S33_gallery-* | S3 | — | Name column could take spare width | d368efa | R1 / T4 (pass 2) |
| Q052 | S33 Tables | S33_gallery-* | S3 | — | User row not distinct | d368efa | R1 / T4 (pass 2) |
| Q053 | S33 Tables | S33_gallery-* | S3 | — | Fixture names with numeric suffixes | d368efa | R1 / T4 (pass 2) |
| Q054 | S33 Tables | S33_gallery-* | S3 | — | Court motif clipped in empty overlay | d368efa | R1 / T4 (pass 2) |
| Q055 | S33 Media | S33_gallery-* | S3 | — | Silhouette stripe reads as prohibition mark | d368efa | R1 / T4 (pass 2) |
| Q056 | S33 Media | S33_gallery-* | S3 | — | Card name colour; hover name wraps | reopened as Q092 | R1 / T4 (pass 2) |
| Q057 | S33 Sports | S33_gallery-* | S3 | — | Comparison bars lack team labels | reopened as Q095 | R1 / T4 (pass 2) |
| Q058 | S33 Sports | S33_gallery-* | S3 | — | Hyphen vs en dash in results | d368efa | R1 / T4 (pass 2) |
| Q059 | S33 Sports | S33_gallery-* | S3 | — | Left column empty gap; bare canvas | d368efa | R1 / T4 (pass 2) |
| Q060 | S33 Sports | S33_gallery-* | S3 | — | Bracket connectors and centring | reopened as Q099 | R1 / T4 (pass 2) |
| Q061 | S33 Feedback | S33_gallery-* | S3 | — | Indeterminate bar empty under reduced motion | reopened as Q104 | R1 / T4 (pass 2) |
| Q062 | S33 Feedback | S33_gallery-* | S3 | — | 'No injuries' empty state has no action | reopened as Q103 | R1 / T4 (pass 2) |

| Q063 | S33 Foundations | S33_gallery-foundations_1920x1080 | S2 | 03 §2 | Only Actions shows components on a team-gradient hero; other sections Surface1 only | open | |
| Q064 | S33 Foundations | S33_gallery-foundations_1920x1080 | S3 | R8; 01 §6 | Gradient cut-corner tile fades into Surface1, cut invisible | open | |
| Q065 | S33 Foundations | S33_gallery-foundations_1920x1080 | S3 | R4 | Tabular figures sample far from its label | open | |
| Q066 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 01 §3 FocusRing | Focused toggle ring tight on control, crosses its label | open | |
| Q067 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 01 §6; 03 §8 | Split-button menu: no floating shadow, no icons, one shortcut | open | |
| Q068 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 01 §4; 01 §14 | Hero toggle/check labels sit on raw gradient, not solid/Scrim | open | |
| Q069 | S33 Actions | S33_gallery-actions_1920x1080 | S3 | R11 | Icon-button size row mixes variants | open | |
| Q070 | S33 Actions | S33_gallery-actions_1920x1080 | S3 | R2 | Continue blocked state uses Warning fill | open | |
| Q071 | S33 Actions | S33_gallery-actions_1920x1080 | S3 | 01 §3 | EntityLink ring ~1px clearance | open | |
| Q072 | S33 Actions | S33_gallery-actions_1920x1080 | S3 | 01 §3 | Selected position chip barely distinct | open | |
| Q073 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 03 §1; 01 §3 | Disabled NavItem and segment use TextSecondary, not TextDisabled | open | |
| Q074 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 03 §1 | Pressed segment identical to unselected | open | |
| Q075 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 01 §3 FocusRing | NavItem and segment focus rings inset, no 2px offset | open | |
| Q076 | S33 Navigation | S33_gallery-navigation_1920x1080 | S3 | 03 §2 | PageHeader variants (no breadcrumb, one action) missing | open | |
| Q077 | S33 Navigation | S33_gallery-navigation_1920x1080 | S3 | R11 | Rail samples on bare Canvas | open | |
| Q078 | S33 Inputs | S33_gallery-inputs_1920x1080 | S2 | 01 §3 FocusRing | TextBox focus is border recolour, no outside ring | open | |
| Q079 | S33 Inputs | S33_gallery-inputs_1920x1080 | S2 | 03 §6 Slider | Slider tick labels not centred on ticks | open | |
| Q080 | S33 Inputs | S33_gallery-inputs_1920x1080 | S3 | R4 | NumericUpDowns/slider ~18px lower than ComboBoxes in row | open | |
| Q081 | S33 Inputs | S33_gallery-inputs_1920x1080 | S3 | 01 §3 | FilterBar chip on/off look inconsistent with Actions chip | open | |
| Q082 | S33 Data | S33_gallery-data_1920x1080 | S2 | 01 §3 FocusRing | Focused card ring on its own edge, no gap | open | |
| Q083 | S33 Data | S33_gallery-data_1920x1080 | S3 | R4 | TEAM LEADERS tile title 4px higher than siblings | open | |
| Q084 | S33 Data | S33_gallery-data_1920x1080 | S3 | 01 §12 | OUTSIDE SCORING average chip doesn't match listed attributes | open | |
| Q085 | S33 Tables | S33_gallery-tables_1920x1080 | S2 | 01 §14; 03 §1 | TEAM cell hard-clipped, no ellipsis/tooltip | open | |
| Q086 | S33 Tables | S33_gallery-tables_1920x1080 | S2 | 03 §7; 01 §6; 01 §3 | Selected row teal tint, no Surface3/3px stripe; no user-team row | open | |
| Q087 | S33 Tables | S33_gallery-tables_1920x1080 | S2 | 03 §8 Skeleton | Skeleton rows don't follow column layout or row height | open | |
| Q088 | S33 Tables | S33_gallery-tables_1920x1080 | S3 | R4 | Numeric headers 5px left of values | open | |
| Q089 | S33 Tables | S33_gallery-tables_1920x1080 | S3 | R7 | Scrollbar under empty/error overlay; fifth skeleton row cut | open | |
| Q090 | S33 Media | S33_gallery-media_1920x1080 | S2 | 01 §11 | Fallback headshot at Hero (and card L) lacks jersey number | open | |
| Q091 | S33 Media | S33_gallery-media_1920x1080 | S2 | 01 §5 | Card names in mixed-case display face | open | |
| Q092 | S33 Media | S33_gallery-media_1920x1080 | S3 | R4 | Card name line indented 4px | open | |
| Q093 | S33 Media | S33_gallery-media_1920x1080 | S3 | R6 | HoverCard trims name | open | |
| Q094 | S33 Media | S33_gallery-media_1920x1080 | S3 | R8 | XL/Hero silhouette translucent over band | open | |
| Q095 | S33 Sports | S33_gallery-sports_1920x1080 | S2 | 01 §12; 03 §7 | ComparisonBar opponent not team accent, no team labels | open | |
| Q096 | S33 Sports | S33_gallery-sports_1920x1080 | S2 | 01 §13; 01 §14 | Cap bar markers $141M without decimal | open | |
| Q097 | S33 Sports | S33_gallery-sports_1920x1080 | S2 | 03 §7 | Decision InboxItem has no actions | open | |
| Q098 | S33 Sports | S33_gallery-sports_1920x1080 | S3 | R10 | Calendar B2B wrong; no month title | open | |
| Q099 | S33 Sports | S33_gallery-sports_1920x1080 | S3 | R11 | Bracket round 1 three series; trimmed names | open | |
| Q100 | S33 Sports | S33_gallery-sports_1920x1080 | S3 | R11 | Zone shot chart lacks legend; one corner label | open | |
| Q101 | S33 Sports | S33_gallery-sports_1920x1080 | S3 | R4 | Linescore period columns uneven | open | |
| Q102 | S33 Feedback | S33_gallery-feedback_full | S2 | 01 §6; 03 §8 | Context menu and Columns flyout no shadow, no icons | open | |
| Q103 | S33 Feedback | S33_gallery-feedback_full | S2 | 03 §8 EmptyState | Default EmptyState has no action | open | |
| Q104 | S33 Feedback | S33_gallery-feedback_full | S3 | R10 | Indeterminate bar empty under reduced motion | open | |
| Q105 | S33 Feedback | S33_gallery-feedback_full | S3 | R11 | Dialog primary labels sentence case; 'Return' not 'Enter' | open | |
| Q106 | S33 Feedback | S33_gallery-feedback_full | S3 | 03 §2 | ErrorState only one variant | open | |

Pass 1 (R1 T4): fresh subagent reviewed all 18 gallery screenshots against 01, 03, 04 and 06 §6 only.

Pass 2 (R1 T4): fresh subagent re-reviewed the 18 gallery screenshots after d368efa. Pass-1 rows it did not raise again are marked fixed; rows it raised again point at their new ID. 0 S1, 20 S2, 24 S3.
