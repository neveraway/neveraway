#!/bin/bash
# NeverAway installer / upgrader (macOS, Apple Silicon).
#
#   curl -fsSL https://raw.githubusercontent.com/neveraway/neveraway/master/scripts/install.sh | bash
#
# Resolves the latest GitHub release, downloads the notarized .app zip,
# and installs it to /Applications. If a copy is already installed it is
# quit and moved aside first: extracting over an installed bundle trips
# macOS App Management ("Operation not permitted"), so never unzip in
# place. The old bundle is restored if the install fails.
set -euo pipefail

REPO="neveraway/neveraway"
APP="/Applications/NeverAway.app"

[ "$(uname -s)" = "Darwin" ] || { echo "error: macOS only" >&2; exit 1; }
[ "$(uname -m)" = "arm64" ]  || { echo "error: Apple Silicon only (release is osx-arm64)" >&2; exit 1; }

# Latest release zip URL via the GitHub API -- no gh CLI needed.
URL=$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
  | grep -o '"browser_download_url": *"[^"]*/NeverAway-[0-9][^"]*\.zip"' \
  | cut -d'"' -f4 | head -1)
[ -n "$URL" ] || { echo "error: could not resolve latest release zip" >&2; exit 1; }
echo "downloading $URL"

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
curl -fsSL -o "$TMP/NeverAway.zip" "$URL"

OLD=""
if [ -d "$APP" ]; then
  echo "existing install found -- quitting and moving it aside"
  pkill -f 'NeverAway.app/Contents/MacOS/neveraway' 2>/dev/null || true
  sleep 1
  OLD="$TMP/NeverAway-old.app"
  mv "$APP" "$OLD"
fi

if ! ditto -x -k "$TMP/NeverAway.zip" /Applications/; then
  echo "error: extract failed" >&2
  [ -n "$OLD" ] && mv "$OLD" "$APP" && echo "old version restored" >&2
  exit 1
fi

open "$APP"
echo "NeverAway installed: look for the no-entry glyph in the menu bar."
echo "First install? macOS will prompt once for Accessibility permission."
