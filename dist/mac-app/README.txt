NeverAway -- macOS menu bar app (Apple Silicon)
===============================================

What this does
--------------
Sits in your menu bar (top-right of the screen, near the clock /
wifi / battery widgets) and posts a zero-pixel mouse-move event
every 10 seconds so Teams / Slack / similar apps don't show you
as "Away" and the screen doesn't lock. The cursor doesn't actually
move (delta is zero), so you won't see or feel anything.

How to run
----------
1. Drag NeverAway.app into your /Applications folder (or run it
   from wherever you unzipped it).

2. Double-click NeverAway.app.

   NeverAway is signed with an Apple Developer ID certificate and
   notarized by Apple, so gatekeeper accepts it on first launch
   with no warning. (You may briefly see "this app is from the
   internet, open?" -- click Open.)

3. Look in your menu bar (top-right of the screen, near the
   clock / wifi / battery widgets). You should see a "no entry"
   glyph (⛔) -- NeverAway is running.

4. Click the icon for the menu:
       Pause                       toggle on/off (icon flips to a shield 🛡 when paused)
       Auto-off in 2 hours         click to arm Slot 1 (Duration)
       Auto-off at 6:00 PM         click to arm Slot 2 (Absolute)
       Configure auto-off...       change the slot values
       Cancel scheduled auto-off   visible when a slot is armed
       Quit NeverAway

5. The first time NeverAway fires a tap (within ~10s of launch),
   macOS will show an Accessibility permission prompt:

       "NeverAway wants to control your computer using
        accessibility features."

   Click "Open System Settings", flip the toggle next to
   NeverAway in the Accessibility list, then re-launch
   NeverAway. (One-time setup.)

That's it. Quit via the menu, or it'll keep running until you
log out / shut down. To start automatically at login, drag
NeverAway.app into System Settings -> General -> Login Items.

Verifying the signature
-----------------------
If you want to confirm the binary is the one we shipped:

    codesign -dvv /Applications/NeverAway.app

Look for:
    Authority=Developer ID Application: Roy Ashbrook (44Y2L8A2CV)
    Authority=Developer ID Certification Authority
    Authority=Apple Root CA
    Notarization Ticket=stapled

Source / issues: https://github.com/neveraway/neveraway
