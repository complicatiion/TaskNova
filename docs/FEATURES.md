# Features

## Overview
- Fills the window without a scroll bar on normal screens (scrolls only in very small windows).
- CPU and memory as ring gauges with key figures (processes, threads, handles, up time, available, cached, committed); kernel share/clock and "used / total" memory in centered accent pills.
- Memory composition bar (in use / cached).
- Tiles for disk (busiest disk), network (total throughput) and GPU (busiest adapter) with live charts; click opens the device on the Performance page.
- Top 5 processes by CPU and by memory; click on a process opens it, click on the card opens the Processes page sorted by that column.
- System facts: computer, Windows edition and build, server/workstation, virtual machine, CPU model, sockets/cores/logical processors, RAM, GPUs, user, sessions. Click opens Windows Settings › System › About (msinfo32 where the Settings app is missing); right-click: msinfo32, advanced system properties, Control Panel.

## Processes
- **Groups** (default): *Apps* (own a visible top-level window), *Background processes*, *Windows processes* (kernel pseudo processes, critical processes, executables under the Windows folder). Groups collapse; the state is remembered.
- **Tree**: parent → child by parent process id; a parent only counts when it started before the child (re-used ids never create wrong trees). Search keeps the matches and their ancestors visible.
- Columns: name (description like the Windows Task Manager, image name as tooltip), status (suspended), CPU, memory (private working set), I/O (bytes/s), GPU, PID. Sorted by clicking the header; value columns re-sort live.
- Heat cells: accent tint by value (CPU %, share of RAM, MB/s, GPU %).
- Security shield: unsigned (embedded or catalog signature checked offline), user-writable location, missing file, Windows name outside the Windows folder.
- Actions (toolbar, right-click, keyboard): end task (Del), end process tree, restart process, suspend/resume, efficiency mode (EcoQoS + idle priority), priority (incl. realtime warning), affinity, create dump file (full memory dump), analyze wait chain (Wait Chain Traversal, deadlock detection), process window (facts, live CPU/memory charts, loaded modules), open file location, Explorer properties, copy details (Ctrl+C), go to details, go to services.
- Protection: critical processes (csrss, wininit, smss, services, lsass, winlogon …) need an extra confirmation; TaskNova cannot suspend itself or critical processes. "Don't ask again" in the end-task confirmation.

## Performance
- Devices on the left with mini charts: CPU, memory, every physical disk, every network adapter that is up, every GPU.
- Big chart with grid; second series dashed (CPU kernel time, network send).
- CPU: overall or one small chart per logical processor (one control draws all cores).
- Figures: utilization, current and base clock, processes, threads, handles, up time, sockets, cores, logical processors, VM; memory in use, available, committed, cached, paged/non-paged pool; disk active time, read/write, response time; network send/receive, link speed, adapter, IPv4; GPU utilization, dedicated/shared memory, busiest engines.

## App history
- CPU time, I/O and run time per program (by image name), with description and path once known.
- Recorded from the process deltas while TaskNova runs (in the tray every 5 s); saved every 5 minutes and on exit.
- Sort by name, CPU time, I/O, run time, last seen; delete the history.

## Network
- Adapter cards with live send/receive: 2 per row in a normal window, up to 5 on wide windows.
- "Show in processes" / "Details" for the selected connection; double-click opens the process on the Processes page.
- TCP (v4/v6) connections and UDP endpoints with process, protocol, local/remote address, state; filters for listening and UDP; optional reverse DNS (cached, max. 8 parallel lookups).

## Details
- All processes incl. System Idle Process; 20 columns; horizontal scrolling with synchronized header; same actions as on the Processes page.

## Startup apps
- HKCU/HKLM Run (incl. WOW6432Node), user and common startup folder (shortcuts parsed without COM), scheduled tasks with logon/boot trigger outside `\Microsoft\`.
- Publisher from the signature or version info, signed badge, missing target dimmed.
- Switch on/off like the Windows Task Manager (`StartupApproved`), tasks via schtasks; open location; copy command.

## Users
- All sessions with a user (console, RDP, Citrix ICA): state, client name and address, sign-in time, idle time, CPU, memory, process count; own session marked.
- Disconnect, sign out (confirmation), send message (dialog in the session), show the session's processes (Details, `session:N`).

## Services
- All Win32 services incl. per-user service instances: name, display name, PID, status, startup type.
- Filters: all / running / stopped; search by name, description, PID.
- Start, stop (asks before stopping running dependents), restart; startup type automatic / automatic (delayed) / manual / disabled.
- Detail panel: description (indirect strings resolved), binary path, account, depends on, needed by.

## Run new task
- Ctrl+N, header button or tray menu; programs, documents, folders, URLs, `.msc`; optional administrator rights; history of the last 12 commands; browse dialog.

## Alerts
- Off by default. CPU or memory above a limit (50–100 %) for 10 s – 5 min shows a small notification window bottom right (not activated, closes after 10 s, hover keeps it). Cooldown 5–60 min per kind. Click opens the Processes page sorted by CPU / memory.

## Expert mode
- Off by default (Settings › Expert mode). Users page: end all processes of a session (Windows session processes and TaskNova stay; the user stays signed in), RDP shadowing view / control via `mstsc /shadow` (not for Citrix ICA sessions – use Citrix Director).

## Tools
- **File locks**: processes that hold a file open (or the files directly in a folder) via the Restart Manager API – type of process, service name, user, session; show in processes, end process.
- **Ports**: listening TCP ports and UDP endpoints (optionally all connections) with process; search by exact port number or text; copy, show in processes, end process.
- **Power plans**: all plans with the active one marked; click activates; link to the power options.
- **System report**: HTML file with system facts, current load, top 15 processes by CPU and memory, sessions and running services (optional). No external CSS, fonts or scripts; print-friendly.

## Mini monitor
- Small always-on-top window with CPU, RAM, disk and network sparklines; drag to move, double-click opens TaskNova.
- Click a metric for its details: CPU (larger chart, kernel share, clock, top 3), RAM (in use / available / committed, top 3), every disk (active time, read/write), every adapter (send/receive).
- Position: 3 × 3 anchor points on a chosen monitor (default bottom right on the primary monitor); dragged by hand it stays where it was put. The window grows from its anchored edge when details open.
- Own theme (like TaskNova, System, Dark, Shadow, Light), opacity 40–100 %, always on top on/off.

## Dashboard mode
- Full screen without sidebar and header – for a dedicated monitoring screen. F11 or the header button toggles it, Esc leaves it. Can be the start mode.

## Appearance & settings
- Themes: Dark (default, navy), Shadow (neutral dark grey), Light, System.
- Accent: TaskNova coral, 10 presets, Windows accent, custom color (Windows color dialog with 16 saved custom colors).
- UI size: Auto (by the logical size of the screen the window is on), XS 85 %, S 92 %, M 100 %, L 112 %, XL 125 %.
- Rendering: graphics card (default) or software (no GPU memory, for servers/VMs; after a restart).
- Chart fill, animations on/off.
- Start: start page (Overview by default, any page, or "Last used"), start as window / maximized / full screen / dashboard / tray only / mini monitor only, monitor (automatic = last position, or a fixed monitor), open the mini monitor too.
- Update speed, chart history, processor utility, GPU monitoring, app history, host names, end-task confirmation.
- Sign-in start in the tray, close/minimize to tray, keep window in tray, always on top, trim memory while in the tray.
- Every change is saved right away (debounced), including window size/position and the collapsed sidebar.
- Desktop context menu entry, Task Manager replacement.
- Elevation status, restart as administrator, data folder, reset settings.

## Command line
| Argument | Effect |
| --- | --- |
| `--tray` | Start hidden in the system tray |
| `--register-autostart` / `--unregister-autostart` | Sign-in task for the current user |
| `--unregister-autostart-all` | Remove the sign-in tasks of all users (uninstaller) |
| `--register-context-menu` | Desktop context menu entry for the current user (installer) |
| `--unregister-integration` | Remove context menu entry and Task Manager replacement (uninstaller) |
