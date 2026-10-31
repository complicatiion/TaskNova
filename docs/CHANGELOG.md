# Changelog

All notable changes to TaskNova. One file for all versions – newest on top.

## 0.0.4 – 2026-10-31

### New
- **Run new task** (Ctrl+N, header button, tray menu): program, document, folder or URL like Win+R, optionally with administrator rights, with a history of the last commands.
- **Efficiency mode** for processes (EcoQoS + idle priority, like the Windows 11 Task Manager) in the context menus of Processes and Details.
- **Restart process**: ends the process and starts it again with the same command line.
- **Analyze wait chain**: shows which lock, window message, COM call or other process the threads of a hanging program wait for, incl. deadlock detection.
- **Process window**: facts (path, command line, user, session, parent, start time, signature), live CPU and memory charts and the loaded modules (DLLs) of one process.
- **Alerts** (off by default): small notification in the corner of the screen when CPU or memory stays above a limit; limits, duration and cooldown adjustable; test button.
- **Expert mode** (off by default) for terminal servers and Citrix: end all processes of a user (session processes stay, the user stays signed in) and RDP shadowing (view / control) on the Users page.

### Changed
- All table toolbars use symbol buttons with two-line tooltips (action + description); dialogs and cards keep symbol + text.
- Startup apps: real on/off switch in the toolbar for the selected entry.
- Services: console button with a console symbol.
- Overview: kernel and memory pills on the line of the card heading, right-aligned; Top CPU / Top memory show more entries when the cards are taller (maximized, dashboard, large screens).
- App history: "Recorded since" as a pill. Power plans: "Active" as a pill. System report: "Save report".
- File locks: narrower path field, the buttons keep their full text.
- Light theme softened: muted greys instead of pure white.
- Settings: hint texts under UI size, rendering, start mode and monitor removed.
- Reset settings no longer uses reflection (Native AOT safe) and re-applies "always on top".

## 0.0.3 – 2026-10-31

### Changed
- Key figures of the page toolbars are shown as **pills** (value in the accent color + label): Processes (processes, CPU, memory), Details (shown / total processes), Network (entries, established, listening), Ports (entries, processes), Services (services, running, shown), Users (sessions, active), Startup apps (entries, enabled).
- Card introductions start with a small round **info symbol** (power plans, accent color, licenses, privacy).
- Shorter toolbar buttons: *End*, *Details*, *Show processes*, *Delete*, *Console*; Services start / stop / restart are symbol buttons with tooltips. Context menus keep the full texts.
- About: *GitHub Repository*, *License folder*, *Flush memory* (now also trims the working set, so the value in the Task Manager drops right away).
- Power plans: *Windows power options*; power plan and system report cards have the same fixed width.
- Settings › Administrator & data: buttons right-aligned.
- Overview: key figure pairs centered in their columns, so the kernel and memory pills sit exactly between them; the usage bars of Top CPU / Top memory are narrow in a normal window (names readable), wide in large windows.
- Network: at most 5 adapter cards per row (2 in a normal window).
- Removed intro texts: memory of TaskNova, file locks, system report, app history.

### Fixed
- Startup apps: the counters did not update when an entry was switched in the list.

## 0.0.2 – 2026-10-31

### New
- **Tools** group in the sidebar:
  - **File locks** – which processes hold a file or folder open (Restart Manager API, like Explorer and installers); show or end the process.
  - **Ports** – who listens on or uses a port; search by port number, show or end the process.
  - **Power plans** – see and switch the active power plan.
  - **System report** – self-contained HTML report (system, load, top processes, sessions, services), created locally.
- **Create dump file** in the process context menus (Processes, Details) – full memory dump for WinDbg / Visual Studio.
- **Dashboard mode** for a dedicated monitoring screen: full screen without sidebar and header (F11, Esc leaves it).
- **Start settings**: start page (default Overview, or "Last used"), start as window / maximized / full screen / dashboard / tray only / mini monitor only, start monitor, open the mini monitor too.
- **UI size**: Auto (by the screen the window is on), XS, S, M, L, XL.
- **Rendering** setting: graphics card or software (applies after a restart).
- **Mini monitor** rebuilt: title "TaskNova · Mini Monitor", metric labels in the accent color, click a metric for its details (CPU: larger chart, kernel share, clock, top 3 · RAM: in use / available / committed, top 3 · every disk · every adapter with send/receive). Own settings card: position on a 3 × 3 monitor picker (default bottom right), monitor (default primary), own theme (default: like TaskNova), opacity, always on top.
- **About**: memory diagnostics (working set, private bytes, .NET heap, garbage collections, rendering, build) with "Release memory"; expanded privacy section.
- Optional **trim memory while in the tray** (off by default).

### Changed
- "Info" is now "About" (also in German).
- Theme "Shadow grey" is now **Shadow**: neutral dark grey (#202020) without the blue tint; Dark stays navy.
- Overview fills the window without a vertical scroll bar on normal screens; kernel share and "used / total" memory in centered accent pills; the whole Top CPU / Top memory card opens the Processes page sorted by that column; the system card opens the Windows "About" page (right-click: msinfo32, advanced system properties, Control Panel; msinfo32 where the Settings app is missing).
- Default window size 1180 × 760, clamped to the screen.
- Settings are saved right after every change (debounced), including the collapsed sidebar, window size and position – a sign-out or crash no longer loses them.
- The start page no longer silently follows the last page: "Last used" is now a choice in the start page list.
- Accent color: the hex code pill next to the heading is gone.
- About: website, GitHub and license buttons centered, "GitHub Repo" label, licenses folder button centered.
- Network: adapter cards 2 per row in a normal window and more on wide windows, with proper spacing; "Show in processes" and "Details" are only enabled with a selected connection; double-click opens the process on the Processes page.

### Memory
- Release builds publish as **Native AOT** by default; the non-AOT build is no longer compressed (a compressed single file is unpacked into memory at every start).
- At most 2 cached page views (was 5); bounded icon cache; no per-row allocations in the process update loop; the details timer stops while the window is hidden.

### Fixed
- Network: "Details" did nothing without a selected connection.
- Adapter cards overlapped the connection table (missing bottom margin).

### Build
- `build\publish.ps1` publishes directly into `artifacts\tasknova_v<version>_portable\TaskNova` – no `artifacts\publish` folder. The ZIP and the setup are both built from that folder; `tasknova.iss` uses it as default source (without the portable marker), so the setup can also be compiled in the Inno Setup IDE after a publish.
- `-Aot` was replaced by `-NoAot` (AOT is the default).

## 0.0.1 – 2026-10-31

First version.

### Monitoring
- One sampler thread for everything: `NtQuerySystemInformation` (all processes and threads in one call, per-core times), `GetSystemTimes`, `GlobalMemoryStatusEx`, `GetPerformanceInfo`; PDH for disks, GPU engines and the optional processor utility; adapter counters through `GetIfEntry2`.
- Demand-driven sampling: only what the visible page needs; in the tray only the totals (plus processes every 5th tick for the app history).
- Snapshots are handed to the UI in one batch; a newer snapshot replaces one the UI has not applied yet (no backlog).
- Update speed 0.5 / 1 / 2 / 5 s or paused; chart history 60 / 120 / 300 s.
- Optional "% Processor Utility" (like the Windows Task Manager), off by default.
- GPU utilization per adapter and per process from the "GPU Engine" counters (every 2 s), adapter names and memory via DXGI.

### Pages
- Overview with ring gauges, live tiles, top consumers and system facts.
- Processes grouped (Apps / Background / Windows processes) or as a parent → child tree; heat cells for CPU, memory, I/O, GPU; security hints.
- Process actions: end task, end process tree, suspend/resume, priority, CPU affinity, open file location, properties, copy details.
- Performance: CPU (overall with kernel share or per logical processor), memory, disks, network adapters, GPU.
- App history recorded by TaskNova (JSON per user).
- Network: adapters and TCP/UDP connections with process; optional reverse DNS.
- Details: all processes, all columns, horizontal scrolling with synchronized header.
- Startup apps: Run keys, startup folders, scheduled tasks (read from the task XML files), switch via StartupApproved.
- Users: sessions with protocol (console/RDP/ICA), client, logon and idle time, usage; disconnect, sign out, message.
- Services via the SCM API: start, stop with dependents, restart, startup type incl. delayed start, dependencies.

### Interface
- Dark by default, Shadow grey, Light, System (follows Windows live).
- Accent color: TaskNova coral, 10 presets, Windows accent, custom color via the Windows color dialog. All accent shades computed in OKLCH with automatic contrast (text ≥ 4.5:1); Fluent check marks, switches and selections use the accent.
- Live charts drawn by own lightweight controls (no chart library); redraw only when visible and changed.
- Header search for every page (`pid:`, `session:` filters), keyboard shortcuts.
- Starts in a normal window (1080 × 700), remembers size and position.
- System tray with tinted logo, CPU/RAM tooltip and full menu; close/minimize to tray; optional sign-in start (scheduled task).
- Mini monitor (always on top).
- Classic desktop context menu entry; optional Task Manager replacement (IFEO).
- English and German.

### Build
- .NET 10, Avalonia 11.3.14, CommunityToolkit.Mvvm 8.4.0, compiled bindings, LibraryImport – prepared for Native AOT.
- Executable `tasknova.exe`, manifest `highestAvailable`.
- Inno Setup installer and portable ZIP via `build\publish.ps1`.
