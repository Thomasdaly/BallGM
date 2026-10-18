# Bundled assets and their licences

Everything the base build ships, with its licence. The Credits & Licenses screen (S31) is generated
from the same list (`src/BallGM.Client.Avalonia/Assets/Licenses`). Only MIT, BSD, Apache-2.0, ISC and
SIL OFL are allowed (spec 00 §5 rule 3). No NBA, team, league, 2K or EA marks, fonts or art.

| Asset | Files | Source | Licence | Licence file |
|---|---|---|---|---|
| Barlow (body) — Regular, Medium, SemiBold, Bold | `Assets/Fonts/Barlow-*.ttf` | The Barlow Project Authors, https://github.com/jpt/barlow (via Google Fonts) | SIL Open Font License 1.1 | `Assets/Fonts/OFL.txt` |
| Barlow Condensed (display) — Medium, SemiBold, Bold, ExtraBold, ExtraBold Italic | `Assets/Fonts/BarlowCondensed-*.ttf` | same | SIL Open Font License 1.1 | `Assets/Fonts/OFL.txt` |
| Phosphor Icons 2.1.1, regular weight (70 icons, as path geometry) | `Themes/Icons.axaml` | https://github.com/phosphor-icons/core (`@phosphor-icons/core`) | MIT | `Assets/Licenses/Phosphor-MIT.txt` |
| Court-line motif, player silhouette, fallback plates | `Themes/Geometry.axaml`, drawn in code | original to this project | project licence | — |

## Libraries (runtime)

| Library | Version | Licence |
|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent | 12.0.4 | MIT |
| SkiaSharp (via Avalonia.Skia) | 3.119.4 | MIT |
| HarfBuzzSharp (via Avalonia.Skia) | — | MIT |
| LiveChartsCore.SkiaSharpView.Avalonia | 2.1.0-dev-798 | MIT |
| Steamworks.NET (Infrastructure, not yet wired) | 2024.8.0 | MIT |

Debug-only, never published (`Publish="false"`): AvaloniaUI.DiagnosticsSupport, Zafiro.Avalonia.Mcp.AppHost.
Test-only: xunit, xunit.v3, Avalonia.Headless.XUnit, Microsoft.NET.Test.Sdk, coverlet.collector.

## Test fixtures

The UI fixture league (`tests/Fixtures/UiLeague`) is invented: every team, player and colour. Mod
fixture images are drawn at test time from simple shapes (`Harness/ModFixtures.cs`) and never committed.
