# NeverAway mac test plan

investigation log lives at [`docs/macos-lock-investigation.md`](macos-lock-investigation.md). this file is the operational test catalogue — what tests exist, what each one validates, and what one-time user intervention is required to run them.

## user prerequisites (one-time intervention)

these grants persist for the same binary signature. rebuilding NeverAway changes the signature hash so the Accessibility grant has to be repeated. on a shipped v3.0.x install (stable signature), all of this is one-time on first run.

1. **Accessibility for NeverAway** — System Settings > Privacy & Security > Accessibility, toggle on for `NeverAway`. without this, `CGEventPost` for the mouse-jiggle layer silently fails (the IOPM assertions still work, but the OS won't see synthetic mouse-move events). triggered automatically by NeverAway's startup prompt if not granted.

2. **Accessibility for System Events** (only if running the test scripts) — same place. when an osascript or test script calls `tell process "neveraway" to click menu item ...`, macOS prompts for this. one-time grant; subsequent osascripts run silently.

3. **screen-lock fast cycle** — `sudo sysadminctl -screenLock 60 -password -` lowers the canonical screen-lock delay to 60 seconds. without this, the slow path tests take 15+ min per iteration. requires admin password. restore with `sudo sysadminctl -screenLock 900 -password -` (default 15 min on most installs).

4. **NSDistributedNotificationCenter post** — no permission needed. any user-space process can post on the default distributed notification center. used by the auto-on synthetic test.

## tests

each test exits 0 on PASS, 1 on FAIL, 2 on NO-DATA (insufficient signal to judge).

### `scripts/test-lock-prevention.sh`

**validates**: kitchen-sink prevents screen lock during natural idle periods.

**method**: applies fast-cycle screensaver settings (60s idle, lock-immediately), runs a watch loop that harvests natural idle windows from the WindowServer UserIsActive assertion age, and reports whether any `Display is turned off` events fired during those windows. restores defaults on exit via trap.

**user intervention**: be naturally idle (away from keyboard / mouse) for at least the `--idle-threshold` (default 90s) at some point during the watch. doesn't have to be deliberate; the script harvests whatever idle windows happen.

**modes**:
- ACTIVE (NeverAway running) — expect PASS, lock should be prevented
- CONTROL (NeverAway killed) — expect FAIL, lock should fire

**caveat**: when killing NeverAway for a control run, the lingering WindowServer UserIsActive assertion (named with NeverAway's pid) persists for up to 600s. the test won't see a clean control until that assertion expires. add a 10-12 min wait before control runs, or use `test-isolation.sh` which handles this automatically.

### `scripts/test-isolation.sh`

**validates**: which layer(s) of the kitchen-sink are individually sufficient to prevent the lock.

**method**: runs activity-generator variants sequentially, with a 10-12 min `wait_for_clean_state` between each variant to let the prior variant's WindowServer UserIsActive assertion expire. variants:
- `control` — nothing running; expect FAIL
- `caffeinate-u` — UserIsActive only; expect ?
- `caffeinate-d` — PreventUserIdleDisplaySleep only; expect ?
- `caffeinate-i` — PreventUserIdleSystemSleep only; expect ?
- `caffeinate-diu` — all three IOPM together, no mouse-jiggle; expect ?
- `neveraway` — full kitchen-sink including mouse-jiggle; expect PASS

**user intervention**: walk away for the duration of the test (default 45 min for the 3-variant fast run, longer for the 6-variant full run). real idle is necessary.

**outcome interpretation**:
- if `caffeinate-diu` PASSes: mouse-jiggle isn't load-bearing, we could simplify NeverAway to just the IOPM assertions
- if `caffeinate-diu` FAILs but `neveraway` PASSes: mouse-jiggle is the load-bearing layer; kitchen-sink stays as-is

### `scripts/test-auto-off.sh` (NEW)

**validates**: arming a slot causes NeverAway to auto-off itself at the scheduled time.

**method**: clicks `Auto-off in 1 minute` via osascript UI-scripting, watches `pmset -g assertions` until the 3 NeverAway-named entries drop below 3, reports the timing.

**user intervention**: only the first run requires granting System Events Accessibility. subsequent runs are silent.

### `scripts/test-auto-on.sh` (NEW)

**validates**: a screen-unlock event triggers NeverAway to re-arm (assuming it was auto-offed, not manual-paused).

**method**: assumes NeverAway is currently auto-offed (run after `test-auto-off.sh`). posts a synthetic `com.apple.screenIsUnlocked` distributed notification via swift one-liner. watches for NeverAway's IOPM assertions to return.

**user intervention**: none (synthetic notification). a real lock+unlock would also work and is the more authentic test.

### `scripts/test-full-gate.sh` (NEW)

**validates**: the **complete gate sequence** — the "real test" — that the machine actually traverses all the lock-relevant states:

1. arm slot 1 → NeverAway stays active
2. machine stays awake for the duration of the slot
3. at slot duration: NeverAway auto-offs
4. after `sysadminctl -screenLock` delay post-auto-off: display actually turns off + screen locks
5. on unlock: NeverAway auto-re-arms
6. IOPM assertions back, machine awake again

each gate is observable from outside the NeverAway process:
- gate 1: pmset shows 3 NeverAway assertions
- gate 2: max real-hw-idle in pmset goes high without display-off
- gate 3: pmset NeverAway assertions drop to 0
- gate 4: pmset log shows `Display is turned off`
- gate 5: pmset shows 3 NeverAway assertions return
- gate 6: pmset log shows `Display is turned on` immediately preceding

**user intervention**: physical lock+unlock (or a synthetic `com.apple.screenIsUnlocked` if testing only the notification-handling). long wait — at least slot-duration + screenLock-delay + small buffer (e.g., 60s slot + 60s lock = 2.5min minimum).

## empirical results so far (2026-05-13)

- **kitchen-sink prevents lock**: PASS over a 2h15min real-world idle observation
- **lock fires after kill**: PASS (control), display-off at ~12 min after kill, confirmed via `pmset -g log`
- **slot 1 click-cycle**: PASS, menu state transitions correctly (Off ↔ Once for Duration, Off → Once → Daily → Off for Absolute)
- **schedule firing**: PASS, slot 1 (1 min Duration) fired at ~60s after arm; IOPM assertions released cleanly
- **auto-on on real lock+unlock**: PASS, NeverAway re-armed within seconds of unlock
- **auto-on on synthetic distributed notification**: PASS, same timing
- **gate 4 (display actually locks post-auto-off)**: ❌ NOT YET VALIDATED. need to run `test-full-gate.sh` to confirm.

## what's known broken

- **Accessibility re-prompt on every rebuild** — unavoidable for ad-hoc-signed bundles, would require Apple Developer ID signing to fix.
- **Configure dialog Kind switching** — phase 2 fixes Slot 1 as Duration, Slot 2 as Absolute. windows side has radio buttons to switch per slot.
