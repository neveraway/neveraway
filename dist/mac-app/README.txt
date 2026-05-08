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

2. First-launch gatekeeper bypass.

   NeverAway is ad-hoc signed but not signed with an Apple
   Developer ID, so on first launch macOS blocks it with
   either "could not be verified" or "is damaged". Right-click
   -> Open used to bypass this; it doesn't work on modern macOS
   (Sonoma / Sequoia / later). Two ways to get past it:

   Mouse-only path (Apple's intended flow):
     a. Try to open NeverAway.app -> blocked dialog appears
     b. Open System Settings -> Privacy & Security
     c. Scroll down to the "Security" section
     d. Look for "NeverAway was blocked..." with an
        "Open Anyway" button -- click it
     e. Confirm in the dialog
     f. Now double-click NeverAway.app -- it'll launch

   Terminal one-liner (faster if you have a terminal open):
     xattr -dr com.apple.quarantine /path/to/NeverAway.app
     # then double-click

   Either is one-time. After first successful launch, macOS
   remembers the user-approved decision.

3. Look in your menu bar (top-right of the screen, near the
   clock / wifi / battery widgets). You should see a "no entry"
   glyph (⛔) -- NeverAway is running.

4. Click the icon for the menu:
       Pause       toggle on/off (icon flips to a shield 🛡 when paused)
       Quit NeverAway

5. The first time NeverAway taps a key (within ~10s of launch),
   macOS will show an Accessibility permission prompt:

       "NeverAway wants to control your computer using
        accessibility features."

   Click "Open System Settings", flip the toggle next to
   NeverAway in the Accessibility list, then re-launch
   NeverAway. (One-time setup.)

That's it. Quit via the menu, or it'll keep running until you
log out / shut down. To start automatically at login, drag
NeverAway.app into System Settings -> General -> Login Items.

Source / issues: https://github.com/neveraway/neveraway
