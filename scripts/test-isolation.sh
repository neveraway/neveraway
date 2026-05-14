#!/usr/bin/env bash
# Isolation test for macOS screen-lock prevention mechanisms.
#
# Runs through several activity-generator variants sequentially, each
# under the same fast-cycle screensaver settings, and reports which
# variants prevented the lock from firing.
#
# Activity generators tested:
#   control            -- nothing; lock SHOULD fire
#   caffeinate-u       -- caffeinate -u  (UserIsActive only)
#   caffeinate-d       -- caffeinate -d  (PreventUserIdleDisplaySleep only)
#   caffeinate-i       -- caffeinate -i  (PreventUserIdleSystemSleep only)
#   caffeinate-diu     -- caffeinate -d -i -u  (all three IOPM assertions, no mouse-jiggle)
#   neveraway          -- NeverAway.app (kitchen sink: 3 IOPM + mouse-jiggle)
#
# Each variant runs for VARIANT_DURATION_MIN minutes. Real-hardware idle
# is harvested from the WindowServer UserIsActive assertion age in
# `pmset -g assertions`. Display-off events are counted in `pmset -g log`.
#
# IMPORTANT: between variants the script waits for the *previous*
# variant's UserIsActive assertion to time out (up to 600s) before
# starting the next variant. Otherwise the lingering assertion would
# contaminate the new variant -- WindowServer-owned UserIsActive
# assertions cannot be force-released, only timed-out. With 3 variants
# the test budget is roughly 3 * (10min wait + 5min test) = 45min.
#
# Verdict per variant:
#   PASS     -- real idle >= threshold AND zero display-off events
#   FAIL     -- display-off event fired during variant window
#   NO-DATA  -- never observed real-hw-idle >= threshold (test inconclusive)
#
# Usage:
#   ./test-isolation.sh                                    # run defaults
#   ./test-isolation.sh --variants control,caffeinate-u    # subset
#   ./test-isolation.sh --variant-minutes 5                # custom per-variant time
#
# This is the test that answers "which layer is the load-bearing one?".

set -euo pipefail

VARIANT_DURATION_MIN=5
IDLE_THRESHOLD_SEC=90
SCREENSAVER_IDLE_SEC=60
SAMPLE_INTERVAL_SEC=10
INTER_VARIANT_WAIT_MAX_SEC=720  # 12 min cap on assertion-expiry wait
VARIANTS_DEFAULT="control caffeinate-diu neveraway"
VARIANTS="$VARIANTS_DEFAULT"
NEVERAWAY_APP="/Users/roy/gh/neveraway/publish/mac-app/NeverAway.app"

while [ $# -gt 0 ]; do
  case "$1" in
    --variant-minutes)   VARIANT_DURATION_MIN="$2"; shift 2 ;;
    --idle-threshold)    IDLE_THRESHOLD_SEC="$2"; shift 2 ;;
    --screensaver-idle)  SCREENSAVER_IDLE_SEC="$2"; shift 2 ;;
    --sample-interval)   SAMPLE_INTERVAL_SEC="$2"; shift 2 ;;
    --variants)          VARIANTS=$(echo "$2" | tr ',' ' '); shift 2 ;;
    --neveraway-app)     NEVERAWAY_APP="$2"; shift 2 ;;
    --wait-max)          INTER_VARIANT_WAIT_MAX_SEC="$2"; shift 2 ;;
    -h|--help) sed -n '2,30p' "$0"; exit 0 ;;
    *) echo "unknown arg: $1" >&2; exit 1 ;;
  esac
done

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

stop_all_generators() {
  pkill -f 'NeverAway.app/Contents/MacOS/neveraway' 2>/dev/null || true
  pkill -x caffeinate 2>/dev/null || true
  sleep 1
}

# How many seconds until the most-recent contamination expires?
# Looks at any WindowServer UserIsActive whose name mentions caffeinate
# or "process:neveraway" -- these are residue from prior variants.
# Returns the max remaining-timeout in seconds, or 0 if none found.
contamination_remaining_seconds() {
  pmset -g assertions 2>/dev/null | awk '
    BEGIN { max=0; cur_is_residue=0 }
    /WindowServer.*UserIsActive/ {
      cur_is_residue = ($0 ~ /(caffeinate|process:neveraway)/) ? 1 : 0
      next
    }
    cur_is_residue && /Timeout will fire in/ {
      for (i=1; i<=NF; i++) {
        if ($i == "in" && $(i+1) ~ /^[0-9]+$/) {
          if ($(i+1) > max) max = $(i+1)
        }
      }
      cur_is_residue = 0
    }
    END { print max }'
}

wait_for_clean_state() {
  local cap="$1"
  local waited=0
  while [ "$waited" -lt "$cap" ]; do
    local remaining
    remaining=$(contamination_remaining_seconds)
    if [ "$remaining" -le 1 ]; then
      echo "  [clean] state is clean (no residual UserIsActive from prior variants)"
      return 0
    fi
    if [ "$((remaining + waited))" -gt "$cap" ]; then
      echo "  [clean] residual assertion has ${remaining}s remaining but exceeds wait cap (${cap}s) -- proceeding anyway, results may be contaminated"
      return 0
    fi
    echo "  [clean] residual UserIsActive assertion has ${remaining}s remaining; waiting..."
    sleep $(( remaining > 60 ? 60 : remaining ))
    waited=$(( waited + (remaining > 60 ? 60 : remaining) ))
  done
}

start_generator() {
  local variant="$1"
  case "$variant" in
    control)        ;;  # nothing
    caffeinate-u)   caffeinate -u -t 86400 &  ;;
    caffeinate-d)   caffeinate -d -t 86400 &  ;;
    caffeinate-i)   caffeinate -i -t 86400 &  ;;
    caffeinate-diu) caffeinate -d -i -u -t 86400 &  ;;
    neveraway)
      if [ ! -d "$NEVERAWAY_APP" ]; then
        echo "[error] NeverAway.app not found at $NEVERAWAY_APP" >&2
        return 1
      fi
      open "$NEVERAWAY_APP"
      sleep 2  # let it come up and create assertions
      ;;
    *) echo "[error] unknown variant: $variant" >&2; return 1 ;;
  esac
}

apply_fast_cycle() {
  defaults -currentHost write com.apple.screensaver idleTime -int "$SCREENSAVER_IDLE_SEC"
  defaults write com.apple.screensaver askForPassword -int 1
  defaults write com.apple.screensaver askForPasswordDelay -int 0
}

restore_defaults() {
  defaults -currentHost delete com.apple.screensaver idleTime 2>/dev/null || true
  defaults delete com.apple.screensaver askForPassword 2>/dev/null || true
  defaults delete com.apple.screensaver askForPasswordDelay 2>/dev/null || true
}

cleanup() {
  echo
  echo "[cleanup] stopping all generators..."
  stop_all_generators
  echo "[cleanup] restoring screensaver defaults..."
  restore_defaults
  echo "[cleanup] done."
}
trap cleanup EXIT INT TERM

# --- setup ---

apply_fast_cycle
stop_all_generators

echo "[setup] screensaver idle = ${SCREENSAVER_IDLE_SEC}s, lock-immediately"
echo "[setup] variants to run: $VARIANTS"
echo "[setup] each variant runs for $VARIANT_DURATION_MIN min; real-hw-idle threshold $IDLE_THRESHOLD_SEC s"
echo "[setup] estimated total time: $(( $(echo "$VARIANTS" | wc -w | tr -d ' ') * VARIANT_DURATION_MIN )) min"
echo

# --- per-variant loop ---

declare -a RESULTS_VARIANT
declare -a RESULTS_VERDICT
declare -a RESULTS_IDLE
declare -a RESULTS_OFFS

for variant in $VARIANTS; do
  echo "============================================="
  echo "variant: $variant"
  echo "============================================="

  stop_all_generators
  wait_for_clean_state "$INTER_VARIANT_WAIT_MAX_SEC"
  start_generator "$variant" || { echo "[skip] $variant"; continue; }

  variant_start=$(date +%s)
  variant_end=$((variant_start + VARIANT_DURATION_MIN * 60))
  max_idle=0

  while [ "$(date +%s)" -lt "$variant_end" ]; do
    now=$(date +%s)
    idle=$(real_hw_idle_seconds)
    if [ "$idle" -gt "$max_idle" ]; then max_idle=$idle; fi
    offs=$(display_off_count_since "$variant_start")
    elapsed=$((now - variant_start))
    printf "  [%s] elapsed=%ds idle=%ds max-idle=%ds display-offs=%d\n" \
      "$(date '+%H:%M:%S')" "$elapsed" "$idle" "$max_idle" "$offs"

    # short-circuit on FAIL: if a display-off fired, we have our answer
    if [ "$offs" -gt 0 ]; then
      echo "  early FAIL — display-off fired, ending variant early"
      break
    fi
    sleep "$SAMPLE_INTERVAL_SEC"
  done

  final_offs=$(display_off_count_since "$variant_start")
  if [ "$final_offs" -gt 0 ]; then
    verdict=FAIL
  elif [ "$max_idle" -ge "$IDLE_THRESHOLD_SEC" ]; then
    verdict=PASS
  else
    verdict=NO-DATA
  fi

  RESULTS_VARIANT+=("$variant")
  RESULTS_VERDICT+=("$verdict")
  RESULTS_IDLE+=("$max_idle")
  RESULTS_OFFS+=("$final_offs")

  echo "  result: $verdict (max-idle=${max_idle}s, display-offs=$final_offs)"
  echo
done

# --- results table ---

echo
echo "==============================="
echo "isolation test summary"
echo "==============================="
printf "%-18s %-9s %-10s %-12s\n" "variant" "verdict" "max-idle" "display-offs"
printf "%-18s %-9s %-10s %-12s\n" "------------------" "---------" "----------" "------------"
for i in "${!RESULTS_VARIANT[@]}"; do
  printf "%-18s %-9s %-10s %-12s\n" \
    "${RESULTS_VARIANT[$i]}" \
    "${RESULTS_VERDICT[$i]}" \
    "${RESULTS_IDLE[$i]}s" \
    "${RESULTS_OFFS[$i]}"
done
echo
echo "guide:"
echo "  PASS     = real-hw-idle >= ${IDLE_THRESHOLD_SEC}s AND zero display-off events => this variant prevented the lock"
echo "  FAIL     = display-off event fired during the variant => this variant did NOT prevent the lock"
echo "  NO-DATA  = real-hw-idle never reached threshold => test inconclusive for this variant"
