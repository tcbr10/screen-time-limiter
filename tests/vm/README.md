# Testing on a Windows VM over SSH

Helpers used to test the installed program from another machine, without clicking through it by hand.
Copy them to `C:\dev\` on the test PC.

| File | What it does |
| --- | --- |
| `uirunner.ps1` | Runs `C:\dev\ui.ps1` inside the signed-in user's desktop session (an interactive scheduled task), waits, prints its output. Run it from an elevated SSH session. |
| `hidden.vbs` | Starts that PowerShell with no window, so it doesn't take focus away from the program under test (popups close when they lose focus). |
| `uilib.ps1` | For scripts run by `uirunner.ps1`: screenshots (`Shot`), finding and clicking controls through UI Automation (`Win`, `Find`, `FindId`, `FindBtn`, `Click`, `TypeInto`, `Texts`, `Dump`). |
| `pipelib.ps1` | Talks to the service's pipe directly: `SLPipe '{"Op":"status"}'`, `SLStatus`. |

Things learned along the way:

- PowerShell 5 reads scripts without a byte-order mark as ANSI. Save scripts as UTF-8 **with** BOM, or Hebrew names won't match.
- Set `[Console]::OutputEncoding = [Text.Encoding]::UTF8` before printing Hebrew over SSH.
- Standard-user checks without a second account's password: from the desktop session,
  `runas /trustlevel:0x20000 "powershell ..."` runs with the Administrators group disabled.
- A locked or disconnected console session can be brought back without a password by a SYSTEM task:
  `tsdiscon 1` then `tscon 1 /dest:console` (just `tscon` if already disconnected).
- Usage is saved to disk every 30 seconds; ask the service over the pipe for exact numbers.
