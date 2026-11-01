# Privacy

TaskNova is **local-first**:

- **No telemetry, no account, no cloud.** TaskNova does not send any usage data anywhere.
- **No downloads, no update checks, no online lookups.** Fonts and icons are embedded.
- **Network connections** only occur when you switch on *Resolve remote host names* (off by default): the Network page then asks your DNS server for the names of remote addresses (reverse DNS). And when you open a link (project page, website) in your browser.

Process, service, session and connection data are read from this machine through Windows interfaces and only shown – they are not stored, except for the app history below.

**Files you create yourself:** the *System report* (HTML) and *dump files* (.dmp) are written only to the place you choose in the save dialog. A dump file contains the full memory of the process, which can include passwords, documents and other personal data – handle it like the data itself.

## Data stored by TaskNova

| Location | Content |
| --- | --- |
| `%AppData%\TaskNova\settings.json` | Settings (theme, accent, window position, intervals …) |
| `%LocalAppData%\TaskNova\app-history.json` | App history: per program name its CPU time, I/O bytes, run time, first and last seen (switch off in the settings, delete on the App history page) |
| Task Scheduler `\TaskNova\Tray <user>` | Only when *Start TaskNova in the tray when I sign in* is on |
| `HKCU\Software\Classes\DesktopBackground\Shell\TaskNova` | Only when the desktop context menu entry is on |
| `HKLM\…\Image File Execution Options\taskmgr.exe` → `Debugger` | Only when *Replace the Windows Task Manager* is on |

Portable mode (file `tasknova.portable` next to the executable): settings and app history are stored in the `data` folder next to the app.
