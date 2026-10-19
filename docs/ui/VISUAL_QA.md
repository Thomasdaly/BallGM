# Visual QA

> Rubric, severities and protocol: spec 06 §6. Reviewers are fresh subagents that have not seen the implementation. An S2 must cite a spec rule.

## Summary
| Screen | Screenshots reviewed | Open S1 | Open S2 | Open S3 | Last reviewed (run / turn) | Clean |
|---|---|---|---|---|---|---|
| S33 Component Gallery | 18 (9 sections × 1080 + full) | 2 | 34 | 26 | R1 / T4 (pass 1) | no |

## Defects
| ID | Screen | Screenshot | Sev | Rule (spec ref) | Defect | Fix commit | Verified by (run / turn) |
|---|---|---|---|---|---|---|---|
| Q001 | S33 Inputs | S33_gallery-inputs_1920x1080 | S1 | R6/R7 | RangeFilter numeric boxes clip their digits (19→'1(') | open | |
| Q002 | S33 Inputs | S33_gallery-inputs_1920x1080 | S1 | R5, 06 §6.2 | Placeholder text ~2.25:1 on field fill | open | |
| Q003 | S33 Foundations | S33_gallery-foundations_1920x1080 | S2 | 01 §13 | Gallery section tab strip in sentence case | open | |
| Q004 | S33 Foundations | S33_gallery-foundations_1920x1080 | S2 | 03 §1 selected | Team switcher shows no active team | open | |
| Q005 | S33 Foundations | S33_gallery-foundations_1920x1080 | S2 | 01 §5 | Caption text reads ~11px (caption role, header subtitle, toasts, hover meta, timeline) | open | |
| Q006 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 01 §3 FocusRing | Focus column: secondary/ghost/danger/default show no focus ring | open | |
| Q007 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 01 §3 FocusRing | Primary focus ring missing top/bottom edges; EntityLink focus ring incomplete | open | |
| Q008 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 01 §3 TextDisabled | Disabled ghost/danger look enabled | open | |
| Q009 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 01 §5 | Continue subtitles ~11px | open | |
| Q010 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 03 §2 | Toggles/check/radio lack hover/pressed/focus/disabled states; only Continue on a hero | open | |
| Q011 | S33 Actions | S33_gallery-actions_1920x1080 | S2 | 03 §4 SplitButton | SplitButton not primary + menu; open menu not shown | open | |
| Q012 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 04 §1 (B) | Collapsed rail shows dots on every item; dots over icon strokes | open | |
| Q013 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 01 §5 | Nav labels and segment options ~11px sentence case with caps tracking | open | |
| Q014 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 03 intro; 04 §2 | PageHeader breadcrumb has no separators | open | |
| Q015 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 03 §5 Stepper | Stepper lacks Back/Next and invalid-step state | open | |
| Q016 | S33 Navigation | S33_gallery-navigation_1920x1080 | S2 | 03 §2 | NavItem, SkewTabStrip, SegmentedControl show no hover/pressed/focus/disabled | open | |
| Q017 | S33 Inputs | S33_gallery-inputs_1920x1080 | S2 | 01 §3 FocusRing | Focused text box identical to default | open | |
| Q018 | S33 Inputs | S33_gallery-inputs_1920x1080 | S2 | 03 §6 Slider | Slider ticks unlabeled | open | |
| Q019 | S33 Inputs | S33_gallery-inputs_1920x1080 | S2 | 03 §6 ComboBox | No logo ComboBox or open popup | open | |
| Q020 | S33 Data | S33_gallery-data_1920x1080 | S2 | 01 §12 Deltas | StatTile deltas grey, not Positive/Negative | open | |
| Q021 | S33 Data | S33_gallery-data_1920x1080 | S2 | 01 §6; 03 §7 | Featured tile has no visible cut corner | open | |
| Q022 | S33 Data | S33_gallery-data_1920x1080 | S2 | 01 §12; 03 §7 | AttributeBar group header with average chip missing | open | |
| Q023 | S33 Data | S33_gallery-data_1920x1080 | S2 | 01 §5 | StatTile sub-lines and card action links ~11px, condensed face | open | |
| Q024 | S33 Data | S33_gallery-data_1920x1080 | S2 | 03 §2; 03 §7 | Card compact variant and hover/focus states missing | open | |
| Q025 | S33 Tables | S33_gallery-tables_1920x1080 | S2 | 01 §6; 03 §7 | Selected row shows no 3px accent stripe | open | |
| Q026 | S33 Tables | S33_gallery-tables_1920x1080 | S2 | 03 §8; 01 §14 | Thick always-visible horizontal scrollbars with arrows | open | |
| Q027 | S33 Media | S33_gallery-media_1920x1080 | S2 | 01 §11 | Hero headshot only 240 tall | open | |
| Q028 | S33 Media | S33_gallery-media_1920x1080 | S2 | 01 §11; 03 §2 | Logo sizes 20 and 256 missing | open | |
| Q029 | S33 Media | S33_gallery-media_1920x1080 | S2 | 01 §11; R8 | Monogram stripe crosses the letters; illegible at small sizes | open | |
| Q030 | S33 Media | S33_gallery-media_1920x1080 | S2 | R6; 01 §14 | HoverCard contract line clipped mid-glyph without ellipsis | open | |
| Q031 | S33 Sports | S33_gallery-sports_1920x1080 | S2 | 03 §7; 01 §4 | Calendar NCV fixtures show the IRV logo | open | |
| Q032 | S33 Sports | S33_gallery-sports_1920x1080 | S2 | 01 §12 Shot chart; 01 §3 | Shot chart: no court lines; labels straddle zone borders; strong fills | open | |
| Q033 | S33 Sports | S33_gallery-sports_1920x1080 | S2 | 01 §12 Cap bar | Third cap-bar marker (apron) unlabeled | open | |
| Q034 | S33 Feedback | S33_gallery-feedback_full | S2 | 03 §8; 03 §4 | Destructive dialog confirm uses accent primary | open | |
| Q035 | S33 Feedback | S33_gallery-feedback_full | S2 | 03 §8; 03 §2 | Confirm and form dialogs, tooltip, context menu and flyout styling not shown | open | |
| Q036 | S33 Feedback | S33_gallery-feedback_1920x1080 | S2 | 03 §8; 01 §6 | Headshot skeleton round; L headshots are square | open | |
| Q037 | S33 Foundations | S33_gallery-* | S3 | — | Tabular sample far from its label | open | |
| Q038 | S33 Foundations | S33_gallery-* | S3 | — | Type samples unnamed | open | |
| Q039 | S33 Foundations | S33_gallery-* | S3 | — | Gradient cut-corner sample hides its cut | open | |
| Q040 | S33 Foundations | S33_gallery-* | S3 | — | Gradient banding in header band | open | |
| Q041 | S33 Actions | S33_gallery-* | S3 | — | Button matrix rows touch | open | |
| Q042 | S33 Actions | S33_gallery-* | S3 | — | 'DEFAULT' variant row label | open | |
| Q043 | S33 Actions | S33_gallery-* | S3 | — | Selected chip barely distinguishable | open | |
| Q044 | S33 Actions | S33_gallery-* | S3 | — | 'Team colours' wording vs spec | open | |
| Q045 | S33 Navigation | S33_gallery-* | S3 | — | Breadcrumb tail vs active tab | open | |
| Q046 | S33 Inputs | S33_gallery-* | S3 | — | NumericUpDown taller than fields | open | |
| Q047 | S33 Inputs | S33_gallery-* | S3 | — | Reset enabled at defaults; spinner bounds; active chips | open | |
| Q048 | S33 Inputs | S33_gallery-* | S3 | — | Disabled field fill lighter | open | |
| Q049 | S33 Data | S33_gallery-* | S3 | — | Hyphen-minus, hyphen ranges, letter x | open | |
| Q050 | S33 Tables | S33_gallery-* | S3 | — | Numeric header right padding | open | |
| Q051 | S33 Tables | S33_gallery-* | S3 | — | Name column could take spare width | open | |
| Q052 | S33 Tables | S33_gallery-* | S3 | — | User row not distinct | open | |
| Q053 | S33 Tables | S33_gallery-* | S3 | — | Fixture names with numeric suffixes | open | |
| Q054 | S33 Tables | S33_gallery-* | S3 | — | Court motif clipped in empty overlay | open | |
| Q055 | S33 Media | S33_gallery-* | S3 | — | Silhouette stripe reads as prohibition mark | open | |
| Q056 | S33 Media | S33_gallery-* | S3 | — | Card name colour; hover name wraps | open | |
| Q057 | S33 Sports | S33_gallery-* | S3 | — | Comparison bars lack team labels | open | |
| Q058 | S33 Sports | S33_gallery-* | S3 | — | Hyphen vs en dash in results | open | |
| Q059 | S33 Sports | S33_gallery-* | S3 | — | Left column empty gap; bare canvas | open | |
| Q060 | S33 Sports | S33_gallery-* | S3 | — | Bracket connectors and centring | open | |
| Q061 | S33 Feedback | S33_gallery-* | S3 | — | Indeterminate bar empty under reduced motion | open | |
| Q062 | S33 Feedback | S33_gallery-* | S3 | — | 'No injuries' empty state has no action | open | |

Pass 1 (R1 T4): fresh subagent reviewed all 18 gallery screenshots against 01, 03, 04 and 06 §6 only.
