#!/usr/bin/env bash
# Programmatic test for macOS screen-lock prevention.
#
# Harvests natural idle periods (no need to deliberately step away).
# Configures short screensaver settings, watches for any "Display is
# turned off" event in `pmset -g log`, and at the end reports:
#   - how many display-off events fired during the watch
#   - the longest "real hardware idle" window observed (gap between
#     real-keyboard/mouse events in pmset log)
#
# The longest real-hardware-idle is what tells us whether the test had
# real signal: if Roy never went idle, the test is NO-DATA.
#
# Critical: "HIDIdleTime" is the WRONG signal for active-mode tests --
# NeverAway resets it every tap, so it can't grow past ~10s. The
# real-hardware-idle is the gap between WindowServer's "Created
# UserIsActive" entries that mention a hardware product (e.g.
# AppleHIDKeyboardEventDriver, AppleMultitouchDevice). Those entries
# fire only on physical input.
#
# Usage:
#   ./test-lock-prevention.sh                          # default 30min watch
#   ./test-lock-prevention.sh --max-watch-minutes 60   # custom cap
#   ./test-lock-prevention.sh --idle-threshold 90      # min real idle for valid signal (seconds)
#   ./test-lock-prevention.sh --screensaver-idle 60    # screensaver idle to apply

set -euo pipefail

MAX_WATCH_MINUTES=30
IDLE_THRESHOLD_SEC=90
SCREENSAVER_IDLE_SEC=60
SAMPLE_INTERVAL_SEC=10

while [ $# -gt 0 ]; do
  case "$1" in
    --max-watch-minutes) MAX_WATCH_MINUTES="$2"; shift 2 ;;
    --idle-threshold)    IDLE_THRESHOLD_SEC="$2"; shift 2 ;;
    --screensaver-idle)  SCREENSAVER_IDLE_SEC="$2"; shift 2 ;;
    --sample-interval)   SAMPLE_INTERVAL_SEC="$2"; shift 2 ;;
    -h|--help) sed -n '2,30p' "$0"; exit 0 ;;
    *) echo "unknown arg: $1" >&2; exit 1 ;;
  esac
done

# seconds since last real-hardware HID input.
#
# Reads the age (HH:MM:SS field) of the current WindowServer UserIsActive
# assertion that's named with a "product:" tag -- those are tied to real
# hardware (AppleHIDKeyboardEventDriver, AppleMultitouchDevice, USB Receiver,
# etc.). Our synthetic CGEvents create WindowServer UserIsActive too but
# they're named with "pid:<NeverAway> process:neveraway" instead, no
# "product:" -- so the filter cleanly isolates real input.
#
# If no such line exists in `pmset -g assertions`, the assertion has expired
# (>600s since last real input) -- we return 9999 as a sentinel for "very
# idle".
real_hw_idle_seconds() {
  pmset -g assertions 2>/dev/null | awk '
    /WindowServer.*UserIsActive/ && /product:/ {
      split($4, t, ":")
      print t[1]*3600 + t[2]*60 + t[3]
      found=1
      exit
    }
    END { if (!found) print 9999 }'
}

# count of "Display is turned off" entries in pmset log since given epoch.
display_off_count_since() {
  local since="$1"
  pmset -g log 2>/dev/null |
    awk -v since="$since" '
      /Display is turned off/ {
        ts = $1 " " $2
        cmd = "date -j -f \"%Y-%m-%d %H:%M:%S\" \"" ts "\" +%s 2>/dev/null"
        cmd | getline epoch
        close(cmd)
        if (epoch >= since) count++
      }
      END { print count+0 }'
}

cleanup() {
  echo
  echo "[cleanup] restoring screensaver defaults..."
  defaults -currentHost delete com.apple.screensaver idleTime 2>/dev/null || true
  defaults delete com.apple.screensaver askForPassword 2>/dev/null || true
  defaults delete com.apple.screensaver askForPasswordDelay 2>/dev/null || true
  echo "[cleanup] done."
}
trap cleanup EXIT INT TERM

# --- setup ---

echo "[setup] applying fast-cycle screensaver settings..."
defaults -currentHost write com.apple.screensaver idleTime -int "$SCREENSAVER_IDLE_SEC"
defaults write com.apple.screensaver askForPassword -int 1
defaults write com.apple.screensaver askForPasswordDelay -int 0

NEVERAWAY_PID=$(pgrep -f 'NeverAway.app/Contents/MacOS/neveraway' || true)
if [ -n "$NEVERAWAY_PID" ]; then
  ASSERTION_COUNT=$(pmset -g assertions | grep -c 'named: "NeverAway"' || true)
  MODE="ACTIVE (NeverAway PID $NEVERAWAY_PID, $ASSERTION_COUNT IOPM assertions held)"
else
  MODE="CONTROL (NeverAway NOT running -- lock SHOULD fire after idle)"
fi

echo "[setup] mode: $MODE"
echo "[setup] screensaver idle = ${SCREENSAVER_IDLE_SEC}s, lock-immediately"
echo "[setup] watching for ${MAX_WATCH_MINUTES} min; min real-hw-idle for valid signal: ${IDLE_THRESHOLD_SEC}s"
echo

# --- watch loop ---

start_epoch=$(date +%s)
end_epoch=$((start_epoch + MAX_WATCH_MINUTES * 60))
max_real_hw_idle=0

while [ "$(date +%s)" -lt "$end_epoch" ]; do
  now=$(date +%s)
  real_idle=$(real_hw_idle_seconds)
  if [ "$real_idle" -gt "$max_real_hw_idle" ]; then
    max_real_hw_idle=$real_idle
  fi
  display_offs=$(display_off_count_since "$start_epoch")
  elapsed=$((now - start_epoch))
  printf "[%s] elapsed=%ds real-hw-idle=%ds max-idle-seen=%ds display-offs=%d\n" \
    "$(date '+%H:%M:%S')" "$elapsed" "$real_idle" "$max_real_hw_idle" "$display_offs"
  sleep "$SAMPLE_INTERVAL_SEC"
done

# --- verdict ---

final_display_offs=$(display_off_count_since "$start_epoch")

echo
echo "==== verdict ===="
echo "mode: $MODE"
echo "watch duration: ${MAX_WATCH_MINUTES} min"
echo "max real-hardware idle observed: ${max_real_hw_idle}s"
echo "display-off events during watch: $final_display_offs"
echo

if [ "$max_real_hw_idle" -lt "$IDLE_THRESHOLD_SEC" ]; then
  echo "result: NO-DATA -- you stayed active (max idle ${max_real_hw_idle}s < threshold ${IDLE_THRESHOLD_SEC}s)"
  echo "        bump --max-watch-minutes or step away at least once during the watch"
  exit 2
elif [ "$final_display_offs" -gt 0 ]; then
  echo "result: FAIL -- screen lock fired ${final_display_offs} time(s) during watch"
  exit 1
else
  echo "result: PASS -- ${max_real_hw_idle}s of real-hardware idle observed, zero display-off events"
  exit 0
fi
