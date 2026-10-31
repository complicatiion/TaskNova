# Legal Notice

## Disclaimer

TaskNova is provided **“as is”**, without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose and non-infringement (see the MIT License in [LICENSE.md](LICENSE.md)).

To the extent permitted by applicable law, the author is not liable for any damages arising from the use of TaskNova, including data loss, system or application malfunctions, or interrupted operation. Mandatory statutory liability (e.g. for intent or gross negligence, fraudulently concealed defects, or under product liability law) remains unaffected.

## Ending, suspending and changing processes

Ending a process or process tree discards its unsaved data. Ending critical Windows processes (e.g. `csrss.exe`, `wininit.exe`, `smss.exe`) makes Windows unusable or shuts it down immediately – TaskNova warns before it does this. Suspending a process freezes it until it is resumed; realtime priority can block the whole system.

## Operator responsibility

TaskNova is a tool for users and administrators. The operator is responsible for:

- ending, suspending and re-prioritizing processes and changing their processor affinity,
- starting, stopping and restarting services and changing their startup type (stopping a service can interrupt programs or users that depend on it),
- disconnecting and signing out user sessions and sending messages to them on terminal servers (inform the users concerned),
- switching startup entries on or off,
- replacing the Windows Task Manager (Image File Execution Options) and the desktop context menu entry,
- compliance with company policies and applicable data protection law (the Users and Network pages show user names, client names and addresses of this machine).
