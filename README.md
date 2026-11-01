![TaskNova](/preview/favicon.png)

# TaskNova

**A fast, modern task manager for Windows 10/11 and Windows Server 2019–2025: processes, performance, app history, network, startup apps, users, details and services in one window.**

TaskNova shows what your computer is doing right now and lets you act on it: end, suspend or prioritize processes, start and stop services, switch startup apps off, disconnect or sign out sessions on a terminal server. It reads everything with a handful of native Windows calls on its own background thread, so the window stays responsive even at 100 % CPU, and it uses very little itself when it sits in the system tray.

![TaskNova](/preview/preview-collage.png)

## Highlights

- **Processes** — Grouped like the Windows Task Manager (*Apps*, *Background processes*, *Windows processes*) or as a **parent → child tree**. CPU, memory, I/O and GPU per process with heat-colored cells, sortable columns, icons and descriptions.
- **All process actions** — End task, end process tree, **restart**, **suspend / resume**, **efficiency mode**, priority, **CPU affinity**, **dump file**, **analyze wait chain**, **process window** with live charts and loaded DLLs, open file location, file properties, copy details, jump to details or services. Critical Windows processes are protected by an extra warning.
- **Security hints** — A shield marks processes without a valid signature, running from Temp/Downloads/AppData, whose file is gone, or with a Windows name outside the Windows folder.
- **Performance** — CPU (overall with kernel share, or one chart per logical processor), memory, every disk, every network adapter and GPU with large live charts and all key figures.
- **Overview** — Ring gauges for CPU and memory, live tiles for disk, network and GPU, the top consumers and the facts of your system at a glance – on one screen, without scrolling.
- **App history** — CPU time, I/O and run time per program, recorded by TaskNova itself while it runs.
- **Network** — Throughput per adapter and every TCP/UDP connection with its process, local and remote address and state; optional host names.
- **Startup apps** — Run keys, startup folders and scheduled tasks with publisher, signature and an on/off switch (reversible, like the Windows Task Manager).
- **Users** — All sessions (console, RDP, **Citrix ICA**) with client, sign-in and idle time, CPU, memory and process count; disconnect, sign out, send a message, show the session's processes.
- **Details** — Every process with all columns: PID, user, session, CPU time, working set, private bytes, threads, handles, priority, I/O, platform, description and command line.
- **Services** — Start, stop (with dependents), restart, change the startup type, see path, account and dependencies.
- **Run new task** — Like Win+R, optionally as administrator (Ctrl+N).
- **Alerts** — Optional notification when CPU or memory stays above a limit, also from the tray.
- **Expert mode for terminal servers & Citrix** — Optional: end all processes of a user, shadow RDP sessions, drain mode, sign out disconnected sessions, message to all, usage per user.
- **Tools** — **File locks** (which program holds a file open), **Ports** (who listens on port 443?), **Power plans** and an offline **HTML system report**.
- **Lists your way** — Sort every list by clicking a column, filter chips, resize and hide columns, type to jump to a name.
- **Back & forward** — Through the visited pages like in a browser (Alt+←/→, mouse side buttons).
- **Search everywhere** — One search box (Ctrl+F) for every list; `pid:1234` and `session:2` filter precisely.
- **Appearance** — **Dark** by default, **Shadow** (neutral dark grey), **Light** or **System** (follows Windows). Accent color: the TaskNova coral, **10 presets**, the **Windows accent** or **any color** from the Windows color picker – icons, highlights, logo, charts, switches and selections follow it, with readable contrast in every theme.
- **UI size** — Auto by screen size or XS to XL.
- **Mini monitor** — A small always-on-top window with CPU, memory, disk and network charts (Ctrl+M); click a metric for its details (top processes, every disk, every adapter).
- **Mini monitor settings** — Own settings card: position on a 3 × 3 monitor picker (default bottom right), which monitor (default primary), its own theme independent of the app, opacity and always on top; dragged by hand it stays where you put it.
- **Dashboard mode** — Full screen without sidebar for a dedicated monitoring screen (F11), with optional page rotation, computer name and clock.
- **Start the way you like** — Start page, window / maximized / full screen / dashboard / tray / mini monitor only, on the monitor of your choice.
- **System tray** — Starts in a normal window by default, optional start in the tray at sign-in, close or minimize to the tray, CPU and memory in the tray tooltip.
- **Windows integration** — Classic **desktop context menu** entry (no signed package needed) and, optionally, **replace the Windows Task Manager** so Ctrl+Shift+Esc opens TaskNova.
- **English and German** — chosen automatically at the first start, switch any time.
- **Local and offline** — No account, no telemetry, no update check; nothing is sent to external servers or CDNs.

## Navigation

| Area | What you'll find |
| --- | --- |
| **Overview** | CPU and memory gauges, disk/network/GPU tiles with live charts, top CPU and memory processes, system facts. |
| **Processes** | Grouped list or tree, live values with heat cells, all process actions (right-click). |
| **Performance** | Devices with mini charts on the left, the big chart and figures of the selected device on the right. |
| **App history** | Recorded CPU time, I/O and run time per app; delete the history. |
| **Network** | Adapters with live throughput, all connections and listening ports with their process. |
| **Details** | All processes with all columns, sortable, wide table with horizontal scrolling. |
| **Startup apps** | All startup entries with switch, publisher, signature and location. |
| **Users** | Sessions with protocol, client, idle time and usage; disconnect, sign out, message. |
| **Services** | All services with PID, status and startup type; details and dependencies on the right. |
| **Tools** | File locks, ports, power plans, system report. |
| **Settings** | Appearance, language, start, mini monitor, monitoring, window & tray, Windows integration, administrator & data. |
| **About** | Version, license, components and their licenses, privacy, memory used by TaskNova. |

The main action of a page or dialog is always on the right, *Cancel* to its left.

## Requirements

- Windows 10 / 11 or Windows Server 2019 / 2022 / 2025, 64-bit (x64).
- Nothing else: the .NET runtime is included.
- Administrator rights are optional. As administrator (UAC prompt, like the Windows Task Manager) TaskNova sees and manages everything; as a standard user it shows all processes, but paths and users only for your own, and services and other sessions cannot be changed.

## How To – Step by Step

**1 · Download & install**

1. Download the latest `tasknova_vX.Y.Z_setup.exe` or `tasknova_vX.Y.Z_portable.zip` from the [releases](https://github.com/complicatiion/TaskNova/releases).
2. **Setup:** run it, optionally tick *Start TaskNova in the system tray at sign-in* and *Desktop context menu entry*.
   **Portable:** extract it, e.g. to `C:\Tools\TaskNova`, and start **`tasknova.exe`**. Settings are stored in the `data` folder next to it.

**2 · Find and end a hanging program**

1. Open **Processes**. Click the **CPU** or **Memory** column header to sort by consumption.
2. Select the program and click **End task** (or press *Del*). With a program that started helpers, right-click → **End process tree**.

**3 · Pause a program instead of ending it**

Right-click → **Suspend**. The row shows *Suspended* with a snowflake; **Resume** continues exactly where it was.

**4 · See what is slowing the PC down**

1. **Overview** shows CPU, memory, disk, network and GPU with the top consumers.
2. Click a tile to open **Performance** with the big chart; for the CPU switch to **Logical processors** to see every core.

**5 · Faster sign-in**

**Startup apps** → switch off what you don't need. The switch is reversible and works like the Windows Task Manager.

**6 · Terminal server / Citrix**

1. **Users** lists every session with protocol (RDP, Citrix ICA), client and idle time.
2. **Send message**, **Disconnect** or **Sign out** a session; **Show processes** opens *Details* filtered to that session.

**7 · Services**

**Services** → select a service → **Start**, **Stop** or **Restart**, or change the *startup type* on the right. When other running services depend on it, TaskNova asks before it stops them too.

**8 · Which program blocks this file?**

**Tools → File locks** → **File …** or **Folder …** → the processes that hold it open. Select one → **End task** or **Show in processes**. **Tools → Ports** answers the same for a network port: type e.g. `443` in the search box.

**9 · A monitoring screen**

**Settings → Start**: *Start as* → **Dashboard**, *Monitor* → the screen for it. Or press **F11** any time; **Esc** leaves the dashboard. The **mini monitor** (Ctrl+M) can sit in a corner of any screen – choose the position in **Settings → Mini monitor**.

**10 · Make it yours**

**Settings → Appearance**: pick *Dark*, *Shadow*, *Light* or *System*, click an accent color or **Choose color …** for your own, and set the **UI size**. **Settings → Start** for the start page and mode, **Window & system tray** for the tray, **Windows integration** for the desktop context menu and Ctrl+Shift+Esc.

## Keyboard

| Key | Action |
| --- | --- |
| Ctrl+F | Search |
| Esc | Clear the search |
| F5 | Refresh now |
| Ctrl+P | Pause / resume updates |
| Ctrl+N | Run new task |
| Alt+← / Alt+→ | Back / forward |
| Ctrl+M | Mini monitor |
| F11 | Dashboard mode on / off (Esc leaves it) |
| Del | End the selected task |
| Ctrl+C | Copy details of the selected process |

## Uninstall

- **Setup:** *Windows Settings → Apps → TaskNova*. The uninstaller removes the sign-in task, the context menu entry and the Task Manager replacement and asks whether settings and app history should be deleted.
- **Portable:** switch off sign-in start, context menu entry and Task Manager replacement in the settings, exit TaskNova via the tray and delete the folder.

## Privacy & Security

- Everything runs locally and offline. TaskNova sends no data anywhere, has no telemetry and no update check, and loads nothing from external servers or CDNs (fonts and icons are built in). Only the optional *Resolve remote host names* (off by default) sends DNS queries to your configured DNS server. Reports and dump files are written only where you save them.
- TaskNova contains no kernel driver. It reads the system through documented Windows interfaces.
- Settings: `%AppData%\TaskNova`, app history: `%LocalAppData%\TaskNova` (portable: `data` next to the app). Details: [licenses/PRIVACY.md](licenses/PRIVACY.md).

## Important Notes

- **Ending a process discards its unsaved data.** Critical Windows processes are protected by an extra warning – ending them shuts Windows down.
- **Suspend** freezes a process completely; its windows do not respond until you resume it. **Realtime** priority can block the whole system.
- The CPU value is *processor time* by default. The Windows Task Manager shows *processor utility* (includes turbo clock) – switch it on in **Settings → Monitoring** if you want the same numbers.
- The **I/O** column shows all input/output of a process per second (files, network, devices).
- *Startup impact* is not measured (it would need a boot trace) and shows *Not measured*.
- **Replace the Windows Task Manager** uses the same mechanism as Process Explorer. If TaskNova is moved or deleted while it is on, Ctrl+Shift+Esc does nothing – switch it off first (the uninstaller does this for you).
- On Windows 11 the desktop context menu entry appears under *Show more options* – the new menu only accepts signed, packaged extensions.
- Processor affinity covers the current processor group (up to 64 logical processors).

More: [docs/FEATURES.md](docs/FEATURES.md) · Build: [docs/BUILD.md](docs/BUILD.md) · Changes: [docs/CHANGELOG.md](docs/CHANGELOG.md)

## Credits

The interface is made with **[Avalonia UI](https://avaloniaui.net/)** and **[CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)** on **[.NET](https://dotnet.microsoft.com/)**. Icons: **[Material Design Icons](https://pictogrammers.com/library/mdi/)**, font: **[Inter](https://rsms.me/inter/)**. Thank you to all authors – licenses in [licenses/README.md](licenses/README.md).

## Project & Copyright

TaskNova is a personal software project by **Sven Klocke** (*sksdesign | complicatiion*).

**Project repository:** [github.com/complicatiion/TaskNova](https://github.com/complicatiion/TaskNova)  
**Website:** [sksdesign.de](https://sksdesign.de)

TaskNova is released under the **MIT License** ([licenses/LICENSE.md](licenses/LICENSE.md)). It is provided without warranty; liability is limited to the extent permitted by law – see [licenses/LEGAL-NOTICE.md](licenses/LEGAL-NOTICE.md) and [licenses/COPYRIGHT.md](licenses/COPYRIGHT.md). Third-party components keep their own copyrights and licenses: [licenses/README.md](licenses/README.md).

Windows, Windows Server, Task Manager, Process Explorer, Citrix and all other names are trademarks of their respective owners. TaskNova is not affiliated with or endorsed by them.
