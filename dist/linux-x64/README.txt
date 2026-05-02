NeverAway -- Linux console (x64)
================================

What this does
--------------
Sends a fake F15 keypress every 10 seconds so Teams / Slack /
similar apps don't show you as "Away". F15 isn't on any modern
keyboard, so you won't see or feel anything.

How to run
----------
1. Install xdotool (we shell out to it for the keypress):

       sudo apt install xdotool        # Debian / Ubuntu
       sudo dnf install xdotool        # Fedora
       sudo pacman -S xdotool          # Arch

2. Make the binary executable:

       chmod +x ./neveraway

3. Run it:

       ./neveraway

4. Press Ctrl+C to stop.

Note: xdotool requires an X11 session. On Wayland you'll need
XWayland, or a Wayland-native equivalent (not currently shipped).

Source / issues: https://github.com/neveraway/neveraway
