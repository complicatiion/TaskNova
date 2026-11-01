# Licenses

## TaskNova

TaskNova is released under the **MIT License** – see [LICENSE.md](LICENSE.md).
Copyright © 2026 Sven Klocke (*sksdesign | complicatiion*).

| Document | Content |
| --- | --- |
| [LICENSE.md](LICENSE.md) | MIT License of TaskNova |
| [COPYRIGHT.md](COPYRIGHT.md) | Copyright and trademark notes |
| [LEGAL-NOTICE.md](LEGAL-NOTICE.md) | Disclaimer, limitation of liability, operator responsibility |
| [PRIVACY.md](PRIVACY.md) | Privacy notes (local-first, no telemetry) |

## Third-party components

Components shipped with TaskNova keep their own copyrights and licenses. Full license texts are in [third-party/](third-party/).

| Component | Version | License | Shipped as | File |
| --- | --- | --- | --- | --- |
| .NET Runtime | 10.0 | MIT | self-contained runtime | [third-party/DOTNET.md](third-party/DOTNET.md) |
| Avalonia UI (incl. Fluent theme, Inter font package) | 11.3.14 | MIT | library | [third-party/AVALONIA.md](third-party/AVALONIA.md) |
| SkiaSharp / HarfBuzzSharp (via Avalonia) | – | MIT (+ Skia BSD-3-Clause, HarfBuzz MIT-style) | native libraries | [third-party/SKIASHARP.md](third-party/SKIASHARP.md) |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | library | [third-party/COMMUNITYTOOLKIT-MVVM.md](third-party/COMMUNITYTOOLKIT-MVVM.md) |
| Material Design Icons (Pictogrammers) | 7.4.47 | Apache-2.0 | vector icons in the UI | [third-party/MATERIAL-DESIGN-ICONS.md](third-party/MATERIAL-DESIGN-ICONS.md) |
| Inter (font) | 4.x | SIL OFL 1.1 | embedded font | [third-party/INTER-FONT.md](third-party/INTER-FONT.md) |

Generic license texts: [LICENSE-MIT.md](third-party/LICENSE-MIT.md) · [LICENSE-APACHE-2.0.md](third-party/LICENSE-APACHE-2.0.md) · [LICENSE-BSD-3-CLAUSE.md](third-party/LICENSE-BSD-3-CLAUSE.md) · [LICENSE-OFL-1.1.md](third-party/LICENSE-OFL-1.1.md)

TaskNova contains **no** kernel driver and no sensor library (e.g. LibreHardwareMonitor). All system data comes from documented Windows interfaces (NT/Win32 APIs, performance counters, Service Control Manager, Terminal Services).
