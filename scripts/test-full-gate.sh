#!/usr/bin/env bash
# The "real test": walks every gate in the lock-prevention sequence
# and reports per-gate PASS/FAIL.
#
# Gates:
#   1. arm slot 1 -- NeverAway stays active
#   2. machine stays awake for the slot duration (no Display-off event)
#   3. slot duration reached -- NeverAway auto-offs (3 IOPM released)
#   4. after sysadminctl -screenLock delay post-auto-off -- display
#      actually turns off (Display is turned off event in pmset log)
#   5. unlock event -- NeverAway auto-re-arms (3 IOPM return)
#   6. machine awake again (no Display-off events for next minute)
#
# Method: arm via osascript, observe via pmset assertions + pmset log,
# trigger the unlock via synthetic distributed notification.
#
# User intervention:
#   - one-time grants per `docs/test-plan.md` (NeverAway Accessibility,
#     System Events Accessibility, sudo for sysadminctl screenLock)
#   - DO NOT TOUCH the keyboard or mouse during the test -- real input
#     events would mask the auto-off-then-lock sequence by refreshing
#     the WindowServer UserIsActive assertion
#
# Total run time: slot_duration + screenLock_delay + ~30s buffer.
# At slot=60s and screenLock=60s, that's ~2.5min minimum.
#
# Exit: 0 = all gates PASS, 1 = any FAIL, 2 = NO-DATA

set -euo pipefail

WAIT_FIRE_MAX_SEC=${WAIT_FIRE_MAX_SEC:-120}    # how long to wait for auto-off
WAIT_LOCK_MAX_SEC=${WAIT_LOCK_MAX_SEC:-180}    # how long to wait for screen-lock fire
WAIT_AUTOON_MAX_SEC=${WAIT_AUTOON_MAX_SEC:-30} # how long to wait for auto-on

iopm_count() {
  pmset -g assertions | grep -c 'named: "NeverAway"' || true
}

display_off_since() {
  local epoch="$1"
  pmset -g log 2>/dev/null | awk -v since="$epoch" '
    /Display is turned off/ {
      ts = $1 " " $2
      cmd = "date -j -f \"%Y-%m-%d %H:%M:%S\" \"" ts "\" +%s 2>/dev/null"
      cmd | getline e
      close(cmd)
      if (e >= since) c++
    }
    END { print c+0 }'
}

declare -a RESULTS

note() { local r="$1" m="$2"; RESULTS+=("$r  $m"); echo "[$r] $m"; }

# preconditions
if ! pgrep -f 'NeverAway.app/Contents/MacOS/neveraway' >/dev/null; then
  echo "[FAIL] NeverAway not running"
  exit 2
fi
if [ "$(iopm_count)" -lt 3 ]; then
  echo "[FAIL] NeverAway not holding its 3 IOPM assertions; ensure Accessibility granted + active state"
  exit 2
fi
lock_delay=$(sysadminctl -screenLock status 2>&1 | awk '/screenLock delay is/ {print $(NF-1)}')
if [ -z "${lock_delay:-}" ]; then lock_delay=900; fi
echo "[setup] screenLock delay = ${lock_delay}s"
echo "[setup] DO NOT TOUCH keyboard/mouse during the test"
echo

# ---- gate 1: arm slot 1 ----
arm_t=$(date +%s)
osascript -e 'tell application "System Events" to tell process "neveraway" to click menu item "Auto-off in 1 minute" of menu 1 of menu bar item 1 of menu bar 1' >/dev/null
sleep 1
items=$(osascript -e 'tell application "System Events" to tell process "neveraway" to get name of every menu item of menu 1 of menu bar item 1 of menu bar 1')
if [[ "$items" == *"(once)"* ]]; then
  note PASS "gate 1: slot 1 armed (menu shows once)"
else
  note FAIL "gate 1: slot arm did not stick (menu: $items)"
fi

# ---- gates 2+3: wait for auto-off ----
deadline=$((arm_t + WAIT_FIRE_MAX_SEC))
fire_t=0
display_off_before_fire=$(display_off_since "$arm_t")
while [ "$(date +%s)" -lt "$deadline" ]; do
  c=$(iopm_count)
  if [ "$c" -lt 3 ]; then
    fire_t=$(date +%s)
    break
  fi
  sleep 2
done

if [ "$fire_t" -eq 0 ]; then
  note FAIL "gate 3: auto-off did NOT fire within ${WAIT_FIRE_MAX_SEC}s"
  echo
  echo "test stopping; can't validate downstream gates without auto-off."
  exit 1
fi
elapsed=$((fire_t - arm_t))
note PASS "gate 3: auto-off fired after ${elapsed}s (NeverAway IOPM released)"

# Did display turn off DURING the arm window (it shouldn't have)?
display_off_during_active=$(($(display_off_since "$arm_t") - display_off_before_fire))
if [ "$display_off_during_active" -eq 0 ]; then
  note PASS "gate 2: machine stayed awake during the arm window (no Display-off events)"
else
  note FAIL "gate 2: $display_off_during_active Display-off event(s) fired DURING the arm window"
fi

# ---- gate 4: display actually turns off post-auto-off ----
lock_deadline=$((fire_t + lock_delay + WAIT_LOCK_MAX_SEC - WAIT_FIRE_MAX_SEC))
lock_t=0
while [ "$(date +%s)" -lt "$lock_deadline" ]; do
  c=$(display_off_since "$fire_t")
  if [ "$c" -ge 1 ]; then
    lock_t=$(date +%s)
    break
  fi
  sleep 5
done

if [ "$lock_t" -eq 0 ]; then
  note FAIL "gate 4: display did NOT turn off within ${lock_delay}s post-auto-off"
else
  elapsed_lock=$((lock_t - fire_t))
  note PASS "gate 4: display turned off ${elapsed_lock}s post-auto-off (matches ~${lock_delay}s screenLock delay)"
fi

# ---- gate 5: trigger unlock + auto-on ----
post_t=$(date +%s)
swift -e 'import Foundation; DistributedNotificationCenter.default().post(name: NSNotification.Name("com.apple.screenIsUnlocked"), object: nil); RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.5))' 2>/dev/null
deadline=$((post_t + WAIT_AUTOON_MAX_SEC))
autoon_t=0
while [ "$(date +%s)" -lt "$deadline" ]; do
  c=$(iopm_count)
  if [ "$c" -ge 3 ]; then
    autoon_t=$(date +%s)
    break
  fi
  sleep 1
done

if [ "$autoon_t" -eq 0 ]; then
  note FAIL "gate 5: auto-on did NOT fire after synthetic unlock notification"
else
  elapsed_on=$((autoon_t - post_t))
  note PASS "gate 5: auto-on fired ${elapsed_on}s after synthetic unlock (NeverAway IOPM back)"
fi

# ---- gate 6: machine stays awake after auto-on (brief check) ----
sleep 20
display_off_after_autoon=$(display_off_since "$autoon_t")
if [ "$display_off_after_autoon" -eq 0 ]; then
  note PASS "gate 6: machine stayed awake post-auto-on (no Display-off in 20s window)"
else
  note FAIL "gate 6: $display_off_after_autoon Display-off events post-auto-on"
fi

# ---- summary ----
echo
echo "============================="
echo "full-gate test summary"
echo "============================="
for r in "${RESULTS[@]}"; do
  echo "  $r"
done

# Any FAIL → exit 1
for r in "${RESULTS[@]}"; do
  case "$r" in FAIL*) exit 1 ;; esac
done
exit 0
