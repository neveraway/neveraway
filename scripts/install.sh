#!/bin/bash
# NeverAway installer / upgrader (macOS, Apple Silicon).
#
#   curl -fsSL https://raw.githubusercontent.com/neveraway/neveraway/master/scripts/install.sh | bash
#
# Resolves the latest GitHub release, downloads the notarized .app zip,
# and verifies its Developer ID and notarization before replacing anything.
# Moving a staged bundle avoids extracting over a live app (App Management).
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

ditto -x -k "$TMP/NeverAway.zip" "$TMP/extracted"
READY="$TMP/extracted/NeverAway.app"
[ -d "$READY" ] && [ ! -L "$READY" ] || { echo "error: missing app bundle" >&2; exit 1; }
REQUIREMENT='anchor apple generic and identifier "com.royashbrook.neveraway" and certificate 1[field.1.2.840.113635.100.6.2.6] exists and certificate leaf[field.1.2.840.113635.100.6.1.13] exists and certificate leaf[subject.OU] = "44Y2L8A2CV"'
verify_app() {
  codesign --verify --deep --strict --verbose=2 -R "=$REQUIREMENT" "$1"
  spctl --assess --type execute --verbose=2 "$1"
}
verify_app "$READY"

STAGE=$(mktemp -d /Applications/.neveraway.XXXXXX)
OLD="$STAGE/previous.app"
INSTALLED=0
cleanup() {
  if [ "$INSTALLED" = 0 ] && [ -d "$OLD" ] && [ ! -e "$APP" ]; then
    mv "$OLD" "$APP" || echo "error: restore the previous install from $OLD" >&2
  fi
  if [ "$INSTALLED" = 1 ] || [ ! -e "$OLD" ]; then
    rm -rf "$STAGE"
  else
    echo "previous installation retained at $OLD" >&2
  fi
  rm -rf "$TMP"
}
trap cleanup EXIT
ditto "$READY" "$STAGE/NeverAway.app"
verify_app "$STAGE/NeverAway.app"
if [ -d "$APP" ]; then
  pkill -f 'NeverAway.app/Contents/MacOS/neveraway' 2>/dev/null || true
  sleep 1
  mv "$APP" "$OLD"
fi
mv "$STAGE/NeverAway.app" "$APP"
INSTALLED=1

open "$APP"
echo "NeverAway installed: look for the no-entry glyph in the menu bar."
echo "First install? macOS will prompt once for Accessibility permission."
