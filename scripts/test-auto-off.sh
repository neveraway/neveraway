#!/usr/bin/env bash
# Validates that arming slot 1 causes NeverAway to auto-off at the
# scheduled time.
#
# Method:
#   1. confirm NeverAway is running with all 3 IOPM assertions held
#   2. arm slot 1 via osascript UI-scripting
#   3. watch pmset assertions for the count to drop below 3
#   4. report timing
#
# User intervention (one-time, first run only):
#   - macOS prompts "System Events wants permission to control NeverAway"
#     when osascript tries to click the menu item. Grant in System Settings
#     > Privacy & Security > Automation (or Accessibility, depending on
#     macOS version). Subsequent runs are silent.
#
# Exit: 0 = PASS, 1 = FAIL (no auto-off in window), 2 = NO-DATA (precondition failed)

set -euo pipefail

WAIT_MAX_SEC=${WAIT_MAX_SEC:-120}

# 1. precondition: NeverAway running with 3 IOPM assertions
if ! pgrep -f 'NeverAway.app/Contents/MacOS/neveraway' >/dev/null; then
  echo "[FAIL] NeverAway not running; launch it before running this test."
  exit 2
fi
count=$(pmset -g assertions | grep -c 'named: "NeverAway"' || true)
if [ "$count" -lt 3 ]; then
  echo "[FAIL] NeverAway has only $count of 3 expected IOPM assertions held."
  echo "       check Accessibility permission for NeverAway, or wait a tap cycle."
  exit 2
fi
echo "[ok] precondition: NeverAway running, 3 IOPM assertions held"

# 2. arm slot 1
arm_t=$(date +%s)
osascript -e 'tell application "System Events" to tell process "neveraway" to click menu item "Auto-off in 1 minute" of menu 1 of menu bar item 1 of menu bar 1' >/dev/null
echo "[arm] $(date '+%H:%M:%S') slot 1 clicked"

# verify the arming took effect
sleep 1
items=$(osascript -e 'tell application "System Events" to tell process "neveraway" to get name of every menu item of menu 1 of menu bar item 1 of menu bar 1')
if [[ "$items" != *"(once)"* ]]; then
  echo "[FAIL] menu doesn't show (once) after click — arming did not take effect"
  echo "       menu items: $items"
  exit 1
fi
echo "[ok] menu confirms slot armed: $items"

# 3. wait for auto-off (NeverAway IOPM count drops below 3)
deadline=$((arm_t + WAIT_MAX_SEC))
while [ "$(date +%s)" -lt "$deadline" ]; do
  c=$(pmset -g assertions | grep -c 'named: "NeverAway"' || true)
  if [ "$c" -lt 3 ]; then
    fire_t=$(date +%s)
    elapsed=$((fire_t - arm_t))
    echo "[FIRE] $(date '+%H:%M:%S') auto-off fired after ${elapsed}s (NeverAway IOPM count $c < 3)"
    echo "[PASS] auto-off cycle works"
    exit 0
  fi
  sleep 2
done

echo "[FAIL] auto-off did NOT fire within ${WAIT_MAX_SEC}s of arm"
exit 1
