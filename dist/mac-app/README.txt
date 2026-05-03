NeverAway -- macOS menu bar app (Apple Silicon)
===============================================

What this does
--------------
Sits in your menu bar (top-right of the screen, near the clock /
wifi / battery widgets) and sends a fake F19 keypress every 10
seconds so Teams / Slack / similar apps don't show you as "Away".
F19 isn't on any modern Mac keyboard and has no system mapping,
so you won't see or feel anything.

How to run
----------
1. Drag NeverAway.app into your /Applications folder (or run it
   from wherever you unzipped it).

2. The first time you double-click it, macOS will block it as
   "downloaded from the internet, unidentified developer":

       Right-click (or Control-click) NeverAway.app
       -> "Open"
       -> "Open" again in the dialog

   This is a one-time gatekeeper bypass for unsigned apps.

3. Look in your menu bar (top-right of the screen, near the
   clock / wifi / battery widgets). You should see a "no entry"
   glyph (⛔) -- NeverAway is running.

4. Click the icon for the menu:
       Pause       toggle on/off (icon flips to a shield 🛡 when paused)
       Quit NeverAway

5. The first time NeverAway taps a key, macOS will show an
   Accessibility permission prompt:

       "NeverAway wants to control your computer using
        accessibility features."

   Click "Open System Settings", flip the toggle next to
   NeverAway in the Accessibility list, then re-launch
   NeverAway. (One-time setup.)

That's it. Quit via the menu, or it'll keep running until you
log out / shut down. To start automatically at login, drag
NeverAway.app into System Settings -> General -> Login Items.

Troubleshooting
---------------
"NeverAway.app is damaged and can't be opened":

  This happens if macOS thinks the gatekeeper signature is
  invalid. Strip the download-quarantine flag manually:

      xattr -dr com.apple.quarantine /path/to/NeverAway.app

  Then double-click again. The app is signed (ad-hoc) but
  unsigned by an Apple Developer ID, so gatekeeper applies
  extra scrutiny on first launch.

Source / issues: https://github.com/neveraway/neveraway
