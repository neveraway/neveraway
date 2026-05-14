#!/usr/bin/env bash
# Comprehensive gate test: sets each macOS idle timer to a distinct
# short value, then runs three phases:
#
#   phase A: NeverAway ON   -- expect ZERO gate events over phase duration
#   phase B: NeverAway OFF  -- expect each gate to fire at its predicted time
#   phase C: NeverAway ON   -- expect ZERO again (re-arm validation)
#
# Each phase duration is 3 min by default; total ~10 min.
#
# Settings applied (distinct timings so each gate is identifiable):
#   screensaver idleTime    = 30s  -> screensaver starts at T+30s
#   askForPasswordDelay     =  5s  -> lock (screensaver path)  at T+35s
#   pmset displaysleep      =  1m  -> display goes off  at T+60s
#   sysadminctl screenLock  = 15s  -> lock (display path)  at T+75s
#   pmset sleep             =  2m  -> system sleeps at T+120s
#
# Restores original settings on exit via trap.
#
# User intervention required:
#   - sudo password (for pmset + sysadminctl), prompted at start
#   - account password (for sysadminctl -screenLock), prompted at start
#   - DO NOT TOUCH keyboard or mouse during phases A and C
#     (any real input refreshes the idle counters and skews the test)
#   - phase B kills NeverAway; resumes via menu at end OR test relaunches
#     it for phase C
#
# Exit: 0 = all phases PASS, 1 = any FAIL

set -uo pipefail

PHASE_DURATION_SEC=${PHASE_DURATION_SEC:-180}
PHASES="${PHASES:-A,B,C}"
NEVERAWAY_APP="${NEVERAWAY_APP:-/Applications/NeverAway.app}"

# settings being applied during the test
TEST_SS_IDLE=30
TEST_SS_PW_DELAY=5
TEST_DISPLAYSLEEP=1     # minutes (smallest pmset value)
TEST_SCREENLOCK=15      # seconds
TEST_SLEEP=2            # minutes

# ----- helpers -----

now_epoch() { date +%s; }
hms()       { date '+%H:%M:%S'; }

iopm_count() { pmset -g assertions | grep -c 'named: "NeverAway"' || true; }

events_since() {
  local epoch="$1"
  local pattern="$2"
  pmset -g log 2>/dev/null | awk -v since="$epoch" -v pat="$pattern" '
    $0 ~ pat {
      ts = $1 " " $2
      cmd = "date -j -f \"%Y-%m-%d %H:%M:%S\" \"" ts "\" +%s 2>/dev/null"
      cmd | getline e
      close(cmd)
      if (e >= since) c++
    }
    END { print c+0 }'
}

count_display_off()  { events_since "$1" "Display is turned off"; }
count_display_on()   { events_since "$1" "Display is turned on"; }
count_sleep_events() { events_since "$1" "Sleep "; }
count_wake_events()  { events_since "$1" "Wake from"; }

# ----- backup current settings -----

backup_displaysleep=$(pmset -g | awk '/^ displaysleep / {print $2}')
backup_sleep=$(pmset -g | awk '/^ sleep / {print $2}')
backup_screenlock=$(sysadminctl -screenLock status 2>&1 | awk '/screenLock delay is/ {print $(NF-1)}')
backup_ss_idle=$(defaults -currentHost read com.apple.screensaver idleTime 2>/dev/null || echo "unset")
backup_ss_pw=$(defaults read com.apple.screensaver askForPassword 2>/dev/null || echo "unset")
backup_ss_pw_delay=$(defaults read com.apple.screensaver askForPasswordDelay 2>/dev/null || echo "unset")

echo "[backup] displaysleep=$backup_displaysleep min"
echo "[backup] sleep=$backup_sleep min"
echo "[backup] screenLock=$backup_screenlock s"
echo "[backup] screensaver idleTime=$backup_ss_idle"
echo "[backup] askForPassword=$backup_ss_pw"
echo "[backup] askForPasswordDelay=$backup_ss_pw_delay"
echo

# ----- prompt for credentials once -----

echo "this test needs admin sudo (for pmset, sysadminctl) and your account password (sysadminctl -screenLock)."
sudo -v
echo "enter your account password (for sysadminctl -screenLock, same password as login):"
read -rs ACCOUNT_PW
echo

# ----- cleanup trap -----

cleanup() {
  echo
  echo "[cleanup] restoring all timers..."
  sudo pmset -a displaysleep "$backup_displaysleep" >/dev/null 2>&1 || true
  sudo pmset -a sleep "$backup_sleep" >/dev/null 2>&1 || true
  echo "$ACCOUNT_PW" | sudo sysadminctl -screenLock "$backup_screenlock" -password - >/dev/null 2>&1 || true

  if [ "$backup_ss_idle" = "unset" ]; then
    defaults -currentHost delete com.apple.screensaver idleTime 2>/dev/null || true
  else
    defaults -currentHost write com.apple.screensaver idleTime -int "$backup_ss_idle"
  fi
  if [ "$backup_ss_pw" = "unset" ]; then
    defaults delete com.apple.screensaver askForPassword 2>/dev/null || true
  else
    defaults write com.apple.screensaver askForPassword -int "$backup_ss_pw"
  fi
  if [ "$backup_ss_pw_delay" = "unset" ]; then
    defaults delete com.apple.screensaver askForPasswordDelay 2>/dev/null || true
  else
    defaults write com.apple.screensaver askForPasswordDelay -int "$backup_ss_pw_delay"
  fi

  echo "[cleanup] done."
}
trap cleanup EXIT INT TERM

# ----- apply test settings -----

echo "[setup] applying test timers..."
sudo pmset -a displaysleep "$TEST_DISPLAYSLEEP"
sudo pmset -a sleep "$TEST_SLEEP"
echo "$ACCOUNT_PW" | sudo sysadminctl -screenLock "$TEST_SCREENLOCK" -password - >/dev/null 2>&1
defaults -currentHost write com.apple.screensaver idleTime -int "$TEST_SS_IDLE"
defaults write com.apple.screensaver askForPassword -int 1
defaults write com.apple.screensaver askForPasswordDelay -int "$TEST_SS_PW_DELAY"

echo "[setup] timers applied. expected gate timings (T = start of phase, idle):"
echo "[setup]   T+${TEST_SS_IDLE}s    screensaver"
echo "[setup]   T+$((TEST_SS_IDLE + TEST_SS_PW_DELAY))s   lock (screensaver path)"
echo "[setup]   T+$((TEST_DISPLAYSLEEP * 60))s    display off"
echo "[setup]   T+$((TEST_DISPLAYSLEEP * 60 + TEST_SCREENLOCK))s    lock (display path)"
echo "[setup]   T+$((TEST_SLEEP * 60))s   system sleep"
echo

# ----- phase runner -----

declare -a PHASE_RESULTS

run_phase() {
  local label="$1"
  local neveraway_state="$2"  # ON | OFF
  local expect="$3"           # zero | events

  echo "============================="
  echo "phase $label: NeverAway $neveraway_state ($PHASE_DURATION_SEC s)"
  echo "============================="

  # state the precondition
  if [ "$neveraway_state" = "ON" ]; then
    if ! pgrep -f 'NeverAway.app/Contents/MacOS/neveraway' >/dev/null; then
      echo "[setup] NeverAway not running -- launching"
      open "$NEVERAWAY_APP"
      sleep 3
    fi
    if [ "$(iopm_count)" -lt 3 ]; then
      echo "[FAIL] NeverAway running but not holding its 3 IOPM assertions"
      PHASE_RESULTS+=("phase $label FAIL precondition")
      return
    fi
  else
    if pgrep -f 'NeverAway.app/Contents/MacOS/neveraway' >/dev/null; then
      echo "[setup] killing NeverAway"
      pkill -f 'NeverAway.app/Contents/MacOS/neveraway' || true
      sleep 2
    fi
  fi

  # baseline event counts
  baseline=$(now_epoch)
  echo "[$(hms)] phase $label start. DO NOT TOUCH keyboard/mouse for $PHASE_DURATION_SEC s."

  # wait the phase duration. periodic status prints every 30s.
  end=$((baseline + PHASE_DURATION_SEC))
  while [ "$(now_epoch)" -lt "$end" ]; do
    sleep 30
    elapsed=$(( $(now_epoch) - baseline ))
    do_off=$(count_display_off "$baseline")
    do_on=$(count_display_on "$baseline")
    sl=$(count_sleep_events "$baseline")
    wk=$(count_wake_events "$baseline")
    echo "  [$(hms)] elapsed=${elapsed}s display-off=$do_off display-on=$do_on sleep=$sl wake=$wk"
  done

  # final counts
  do_off=$(count_display_off "$baseline")
  do_on=$(count_display_on "$baseline")
  sl=$(count_sleep_events "$baseline")
  wk=$(count_wake_events "$baseline")

  if [ "$expect" = "zero" ]; then
    if [ "$do_off" -eq 0 ] && [ "$sl" -eq 0 ]; then
      verdict=PASS
    else
      verdict=FAIL
    fi
  else
    if [ "$do_off" -gt 0 ]; then
      verdict=PASS
    else
      verdict=FAIL
    fi
  fi

  echo "[$(hms)] phase $label complete. display-off=$do_off display-on=$do_on sleep=$sl wake=$wk"
  echo "[$verdict] phase $label expected=$expect, observed display-off=$do_off"
  PHASE_RESULTS+=("phase $label $verdict (display-off=$do_off sleep=$sl)")
  echo
}

# ----- run phases -----

IFS=',' read -ra PHASE_LIST <<< "$PHASES"
for p in "${PHASE_LIST[@]}"; do
  case "$p" in
    A) run_phase A ON  zero ;;
    B) run_phase B OFF events ;;
    C) run_phase C ON  zero ;;
    *) echo "unknown phase: $p" >&2 ;;
  esac
done

# ----- summary -----

echo
echo "============================="
echo "comprehensive-gates summary"
echo "============================="
for r in "${PHASE_RESULTS[@]}"; do
  echo "  $r"
done

for r in "${PHASE_RESULTS[@]}"; do
  case "$r" in *FAIL*) exit 1 ;; esac
done
exit 0
