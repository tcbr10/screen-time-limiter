# Handoff

Updated 2026-11-16, end of the first Windows test session. Read this first, then [README.md](README.md).

## Where things stand

- Builds on .NET SDK 10.0.404 with no errors or warnings. 47 rule tests pass (`dotnet test`).
- Installed with `scripts/install.ps1` on a Windows 11 test VM and tested end to end (results below).
- The UI was rebuilt to match the original mockup (mirrored for Hebrew). Screenshots: [docs/screenshots](docs/screenshots).
- There is still no git repository. Nothing has been committed anywhere.

## Test VM

Windows 11 Pro (tiny11 on dockur/CasaOS). SSH on **port 2222** (port 22 is the Ubuntu host), browser
viewer on 8006. Accounts and the test PIN are in `test-vm.local.md` (not for the repo).
Source is copied to `C:\dev\screen-limiter`; helpers for driving it remotely are in [tests/vm](tests/vm).
UAC is turned off on this VM (`EnableLUA=0`), which is why the console opens without a prompt there.

## What was tested, and how it went

| Check | Result |
| --- | --- |
| Admin console opens, Hebrew, right-to-left; switching to English and back rebuilds the window | Pass |
| Lists local users; admins shown as "never limited" and can't be limited from the console | Pass |
| Daily, weekly, allowed hours, per-day editing, save, discard, add time | Pass |
| Parent PIN: format check, mismatch check, save | Pass |
| Agent starts in the limited user's session; tray icon pinned to the taskbar | Pass |
| Time-left popup from the tray; 5-minute warning card | Pass |
| Lock screen at zero, with "More time starts tomorrow at 00:00" / allowed-hours / sign-out texts | Pass |
| PIN on the lock screen: empty, wrong, right (adds the full 15 minutes) | Pass |
| Five wrong PINs lock PIN entry | Pass |
| Agent killed: service starts it again (14 s) | Pass |
| Agent killed while time is up: service disconnects the session (12 s); overlay returns on reconnect | Pass |
| Locked screen (Win+L) pauses the count; unlocking resumes it | Pass |
| Sign-out mode signs the user out after 30 s | Pass |
| Signing out while the overlay is up isn't blocked | Pass |
| Service starts after a restart; usage survives it | Pass |
| As a standard user: can't read settings, change them over the pipe, stop/reconfigure/kill the service, write the program folder or its registry key | Pass |
| Grants, disconnects and sign-outs appear in the Application event log (source `ScreenLimiter`) | Pass |

The limit tests ran on the Docker admin account (a limit written straight into the config, since the
console refuses admins) because signing in as the `kid` account needs its password typed at the
Windows sign-in screen. The standard-user checks used a "Basic User" token (Administrators disabled).

## Fixed in this session

- Time kept counting behind the lock screen, so extra time granted later was partly used up already.
- Windows 11 hid the tray icon in the overflow area; the agent now asks for it to be shown.
- The balloon warning never appeared (Windows notifications); replaced by the agent's own warning card.
- The console window was taller than a 720-pixel screen; it now fits the work area.
- Information events (grants, disconnects) weren't reaching the event log; now they do, under one source.
- Bypass: a standard user could change the time zone to move "today" and the allowed hours. Limits now
  follow the time zone saved by the administrator (`AppConfig.TimeZoneId`).
- Bypass: Safe Mode (reachable by anyone from the sign-in screen) didn't run the service. The installer
  now registers it under `SafeBoot\Minimal` and `SafeBoot\Network`; the uninstaller removes that.

## UI compared with the mockup

Matches the mockup's layout, sizes and colours: header bar doubling as the title bar (app name, "Protection
on" with a shield), users with initials avatars and coloured status lines, admins greyed with a lock,
name with a "Limit active" badge, "+ Add time today", segmented mode control, rounded sliders, option rows
with dividers, Discard / Save. Tray popup and lock screen follow the mockup cards.

Deliberate differences:
- A fourth segment, "No limit", so a limit can be turned off (the mockup had no way to do that).
- "Add time today" opens a small menu (+15 min, +30 min, +1 h, +2 h) instead of adding a fixed amount.
- Parent PIN and language sit at the bottom of the users column (the mockup didn't show them).
- The lock screen has no separate "Add time" button, as in the mockup: type the PIN and press Enter.
- The tray popup leaves out the mockup's "Read only." note, which looked like an annotation.
- Slider steps are 15 minutes (the mockup used 30), so the shortest limit is 15 minutes.

## Still open

- **UAC prompt** not seen: UAC is off on the VM. To check it, turn it on there (as admin:
  `reg add HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System /v EnableLUA /t REG_DWORD /d 1 /f`, restart).
- **Signing in as `kid`** by hand, to see it from a real standard account (everything was tested, but on Docker).
- **Safe Mode** itself wasn't booted; only the registration was checked.
- Multi-monitor and mixed-DPI placement of the lock screen weren't tested (the VM has one screen).
- `kid` currently has a 5-minute daily limit plus 15 minutes granted today (resets tomorrow).
- Consider `git init` and a first commit. Ask the owner first.

## Known gaps, accepted for the first version

- No usage history or reports; only today's and this week's totals are kept.
- No installer package; installation is the PowerShell script, which builds from source.
- A determined user can put Task Manager over the lock screen; killing the agent then disconnects the
  session, so this gains them nothing lasting.
- Allowed-hours windows can't cross midnight (end may be 24:00 at most).
- An admin who moves the clock forward and back can leave a user's day counter stuck until that date passes.

## How the owner likes to work

- Wants a plan and mockup before building, then direct building without many questions.
- Ask before installing anything on their own machines (the test VM was given for testing).
- Hebrew speaker; UI text should stay natural Hebrew, with English as the second language.
