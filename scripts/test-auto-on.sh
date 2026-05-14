#!/usr/bin/env bash
# Validates that NeverAway re-arms on a screen-unlock event after
# having been auto-offed by the schedule.
#
# Method:
#   1. precondition: NeverAway is running but currently auto-offed
#      (its 3 IOPM assertions are NOT held). The simplest way to
#      satisfy this is to run test-auto-off.sh first.
#   2. post a synthetic "com.apple.screenIsUnlocked" notification on
#      NSDistributedNotificationCenter via swift one-liner
#   3. watch for NeverAway's IOPM assertions to return (count >= 3)
#   4. report timing
#
# Real-world alternative: physical lock + unlock will post the same
# notification system-side. This test exists to validate the
# notification-handling code path without needing the user to lock.
#
# User intervention: none (the swift one-liner runs in user-space and
# posts to the default distributed notification center, which is
# accessible to any user-space process).
#
# Exit: 0 = PASS, 1 = FAIL, 2 = NO-DATA (precondition failed)

set -euo pipefail

WAIT_MAX_SEC=${WAIT_MAX_SEC:-30}

# 1. precondition: NeverAway running but currently auto-offed
if ! pgrep -f 'NeverAway.app/Contents/MacOS/neveraway' >/dev/null; then
  echo "[FAIL] NeverAway not running; launch it before running this test."
  exit 2
fi
count=$(pmset -g assertions | grep -c 'named: "NeverAway"' || true)
if [ "$count" -gt 0 ]; then
  echo "[FAIL] NeverAway currently has $count IOPM assertions -- expected 0."
  echo "       run scripts/test-auto-off.sh first to auto-off NeverAway."
  exit 2
fi
echo "[ok] precondition: NeverAway running, currently auto-offed (no IOPM held)"

# 2. post the synthetic unlock notification
post_t=$(date +%s)
swift -e 'import Foundation; DistributedNotificationCenter.default().post(name: NSNotification.Name("com.apple.screenIsUnlocked"), object: nil); RunLoop.current.run(until: Date(timeIntervalSinceNow: 0.5))'
echo "[post] $(date '+%H:%M:%S') synthetic com.apple.screenIsUnlocked posted"

# 3. wait for IOPM assertions to return
deadline=$((post_t + WAIT_MAX_SEC))
while [ "$(date +%s)" -lt "$deadline" ]; do
  c=$(pmset -g assertions | grep -c 'named: "NeverAway"' || true)
  if [ "$c" -ge 3 ]; then
    fire_t=$(date +%s)
    elapsed=$((fire_t - post_t))
    echo "[FIRE] $(date '+%H:%M:%S') auto-on fired after ${elapsed}s (NeverAway IOPM count $c >= 3)"
    echo "[PASS] auto-on cycle works"
    exit 0
  fi
  sleep 1
done

echo "[FAIL] auto-on did NOT fire within ${WAIT_MAX_SEC}s of the synthetic notification"
exit 1
