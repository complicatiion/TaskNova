# Dependencies

| Package | Version | License | Why |
| --- | --- | --- | --- |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter | 11.3.14 | MIT | UI, Fluent theme, tray, Inter font |
| Avalonia.Diagnostics | 11.3.14 | MIT | Debug builds only (F12 dev tools) |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | Observable properties and commands (source generators) |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk | 2.9.3 / 3.0.2 / 17.12.0 | Apache-2.0 / MIT | Tests only |

Deliberately **not** used:

| Not used | Reason |
| --- | --- |
| LiveCharts2 / OxyPlot / ScottPlot | Animation and allocation overhead per frame; own chart controls are enough and cost nothing while hidden |
| Avalonia.Controls.DataGrid / TreeDataGrid | Live updates every second: own virtualized tables with fixed columns are cheaper and fully controllable |
| System.Management (WMI) | Slow, allocation-heavy, not AOT-friendly |
| LibreHardwareMonitorLib | Needs a kernel driver (security products may flag it), MPL-2.0 – possible later as an optional plugin |
| System.Drawing.Common | Icons are drawn into a DIB section with GDI directly |
