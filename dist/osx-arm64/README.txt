NeverAway -- macOS console (Apple Silicon)
==========================================

What this does
--------------
Sends a fake F15 keypress every 10 seconds so Teams / Slack /
similar apps don't show you as "Away". F15 isn't on any modern
Mac keyboard, so you won't see or feel anything.

How to run
----------
1. Open Terminal in this folder.

2. Clear the macOS download-quarantine flag (set automatically
   when the zip was downloaded from the internet):

       xattr -d com.apple.quarantine ./neveraway

3. Make the binary executable:

       chmod +x ./neveraway

4. Run it:

       ./neveraway

5. The first run will trigger an Accessibility permission prompt
   (we use osascript to send the keypress). Allow it in:

       System Settings -> Privacy & Security -> Accessibility

   Then run ./neveraway again.

6. Press Ctrl+C in the terminal to stop.

To run it in the background, use nohup or a launchd agent --
out of scope for this README.

Source / issues: https://github.com/neveraway/neveraway
