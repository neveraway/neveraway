NeverAway -- Windows tray
=========================

What this does
--------------
Sends a fake F24 keypress every 10 seconds so Teams / Slack /
similar apps don't show you as "Away". F24 isn't on any modern
keyboard, so you won't see or feel anything.

How to run
----------
0. Or skip the zip entirely -- one-line install/upgrade from
   PowerShell (installs to %LOCALAPPDATA%\NeverAway, makes a
   Start Menu shortcut, no SmartScreen warning):

       irm https://raw.githubusercontent.com/neveraway/neveraway/master/scripts/install.ps1 | iex

1. Double-click neveraway.exe.

   Windows SmartScreen may warn ("Windows protected your PC")
   because the binary isn't code-signed. Click "More info" then
   "Run anyway".

2. The app runs in the system tray (look near the clock).

3. Double-click the tray icon to toggle pause / resume.

4. Right-click the tray icon and pick Exit to quit.

That's it. Nothing to configure.

Source / issues: https://github.com/neveraway/neveraway
