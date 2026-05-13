# macOS screen-lock investigation (v3.0.1)

issue: [neveraway/neveraway#6](https://github.com/neveraway/neveraway/issues/6). PR: [#8](https://github.com/neveraway/neveraway/pull/8).

cold-read context: this is the investigation log for why v3.0.0 mac prevented display darkening but didn't prevent the screen lock, and what we tried. write-up is here so next time anyone (incl. me) hits this kind of macOS idle-counter problem, the diagnostic procedure and the dead ends are recorded — not just the conclusion.

## the bug, in one paragraph

v3.0.0 mac uses a synthetic F19 keystroke via `CGEventCreateKeyboardEvent` + `CGEventPost`, fired every 10s. it correctly prevents display dimming (HIDIdleTime resets) and keeps Teams / Slack from going Away. but after the system's configured screen-lock delay, the screen locks anyway. macOS apparently distinguishes synthetic input (`IOHIDSystem` path, via CGEventPost) from real hardware input (`AppleUserHIDEventService` path, via physical keyboard / mouse / trackpad), and the lock-screen timer is gated on the latter only.

## counters and assertions on macOS, in plain words

modern macOS has at least *four* different "is the user here?" measurements, each driving different behavior:

1. **`HIDIdleTime`** (in `ioreg -c IOHIDSystem`) — nanoseconds since last HID input. resets on both real and synthetic events. drives **display dimming / display sleep**. this is the one HM's diagnostic in the v3 ship arc proved we were resetting.
2. **`UserIsActive` IOPM assertion** (in `pmset -g assertions`) — declared by `WindowServer` when a HID event fires, or by an app calling `IOPMAssertionDeclareUserActivity`. resets on both real and synthetic events. **600s timeout** if not refreshed. our mouse-jiggle creates these too — `pmset` shows them named `pid:<NeverAway> process:neveraway`.
3. **`PreventUserIdleDisplaySleep` / `PreventUserIdleSystemSleep` IOPM assertions** — declarative "do not auto-sleep" assertions held by an app for as long as it wants. visible in `pmset -g assertions`. independent of any idle counter; the OS just doesn't auto-sleep while these are held.
4. **the screen-lock idle counter** — the one driving `sysadminctl -screenLock <delay>`. **this is the counter we can't reach from userspace.** empirically, none of the three above prevent it from firing.

the v3 mental model assumed (1) was the universal counter ("if we reset HIDIdleTime, everything else falls in line"). that's right for display-dim, wrong for screen-lock.

## diagnostic commands (cold-read friendly)

paste any of these into terminal to inspect state. these are the load-bearing ones from this investigation:

### check the lock timer and current settings

```bash
# the canonical screen-lock delay, in seconds. requires sudo to change.
sysadminctl -screenLock status

# screen-saver idle time (seconds before saver starts).
# unset = use system default. set with `defaults -currentHost write`.
defaults -currentHost read com.apple.screensaver idleTime

# password-required-on-screensaver settings.
defaults read com.apple.screensaver askForPassword
defaults read com.apple.screensaver askForPasswordDelay
```

### check the HID idle counter (HIDIdleTime)

```bash
ioreg -c IOHIDSystem -r -d 1 -k HIDIdleTime | awk '/HIDIdleTime/ {printf "%.2fs\n", $NF/1e9}'
```

returns one number in seconds. expectation while NeverAway is active and hands are off: stays ≤10s (it should sawtooth as the 10s tap loop resets it). while paused: grows monotonically.

time-series version that runs for 60s (hands off keyboard / mouse during the run):

```bash
for i in 1 2 3 4 5 6; do
  printf "t=%2ds  " $((i*12-12))
  ioreg -c IOHIDSystem -r -d 1 -k HIDIdleTime | awk '/HIDIdleTime/ {printf "HIDIdle=%.2fs\n", $NF/1e9}'
  sleep 12
done
```

### check IOPM assertions

```bash
pmset -g assertions
```

look for entries owned by NeverAway's PID. with the v3.0.1 build (kitchen-sink), you should see *three* assertions named `"NeverAway"`:

- `PreventUserIdleSystemSleep`
- `PreventUserIdleDisplaySleep`
- `UserIsActive` (timeout 600s, refreshed each tap)

plus a fourth, owned by `WindowServer` and named `...service:IOHIDSystem pid:<NeverAway> process:neveraway` — that's the assertion WindowServer creates in response to our synthetic mouse-move events.

### check that NeverAway is actually running

```bash
pgrep -lf 'NeverAway.app/Contents/MacOS/neveraway'
```

returns PID + path. empty = not running. uptime check:

```bash
ps -p <PID> -o etime=
```

## the test cycle, both versions

### normal (slow) test — uses the real screen-lock delay

1. `sysadminctl -screenLock status` — note the delay (default 900s = 15min on roy's machine)
2. launch NeverAway via `open /path/to/NeverAway.app`
3. verify with `pmset -g assertions` that all four layers are showing up
4. step away from the machine for at least `<screenLock delay> + 5 min`
5. come back; if screen locked, the prevention failed. if still unlocked, it worked.

slow because (4) is real time. used for the canonical validation.

### fast (60-second cycle) test — uses screensaver as a shorter lock path

idea: shrink the cycle by setting the screensaver to start at 60s idle and lock immediately when it does. *if* the screen-lock cascade fires via the screensaver path, this gets us a 1-2 min test cycle instead of 15+ min.

```bash
defaults -currentHost write com.apple.screensaver idleTime -int 60
defaults write com.apple.screensaver askForPassword -int 1
defaults write com.apple.screensaver askForPasswordDelay -int 0
```

then:

1. launch NeverAway
2. verify `pmset -g assertions` shows the four layers
3. walk away (hands off keyboard / mouse) for ~2 min
4. observe whether lock fires

restore original settings after testing:

```bash
defaults -currentHost delete com.apple.screensaver idleTime
defaults delete com.apple.screensaver askForPassword
defaults delete com.apple.screensaver askForPasswordDelay
```

caveat: the screen-saver path is one route into the lock. the canonical `screenLock` timer is a separate route. if the screensaver doesn't trigger the lock on roy's macOS version, this fast-cycle test won't observe the real bug — fall back to the slow version.

## attempts and what they showed (newest first)

### attempt 3: kitchen-sink (current PR head — testing now, 2026-05-13)

`MacInputSimulator` holds:

- `IOPMAssertionCreateWithName(PreventUserIdleDisplaySleep)` — persistent for app lifetime
- `IOPMAssertionCreateWithName(PreventUserIdleSystemSleep)` — persistent for app lifetime
- `IOPMAssertionDeclareUserActivity(kIOPMUserActiveLocal)` — refreshed each Tap()
- the existing zero-pixel `CGEventCreateMouseEvent(kCGEventMouseMoved)` — still fires each Tap()

verified via `pmset -g assertions` that all four assertions show up under NeverAway's pid.

result: **pending empirical validation** as of this writing. testing under the 60s screensaver cycle.

### attempt 2: zero-pixel mouse-jiggle alone (PR draft, before kitchen sink)

idea from apple dev forum #26776 + amphetamine's "automated mouse cursor movement" feature: mouse-move events might trigger different idle counters than keyboard events.

`MacInputSimulator.Tap()` swapped the F19 keystroke for `CGEventCreateMouseEvent(kCGEventMouseMoved, currentPos)` + `CGEventPost(kCGHIDEventTap, ...)`. zero-pixel delta, cursor doesn't visibly move.

result: **the lock still fired.** but the diagnostic data was extremely useful — we confirmed:

- HIDIdleTime sawtooth never exceeded 10s (`0.10, 0.18, 8.38, 0.41, 2.44, 4.47`) → events ARE reaching the HID layer
- pmset showed `UserIsActive` assertion named `service:IOHIDSystem pid:<NeverAway> process:neveraway` → events ARE creating the UserIsActive assertion via WindowServer

so the failure was NOT "events aren't reaching the OS." failure was "the screen lock counter is independent of HIDIdleTime AND of the WindowServer-mediated UserIsActive."

### attempt 1: original v3.0.0 — F19 keystroke

`CGEventCreateKeyboardEvent(KVK_F19=80)` + `CGEventPost(kCGHIDEventTap, ...)`, every 10s. F19 picked because it has no system mapping on any modern mac keyboard.

result: **prevented display dim + suppressed Teams Away (working as designed for those purposes), but lock still fired** after the configured `screenLock` delay.

post-investigation read: F19 hits the same `IOHIDSystem` path that mouse-jiggle does, so it has the same shape of failure on the lock-screen counter. the mental model in HM's v3 ship arc — "F19 resets HIDIdleTime which suppresses display sleep / system sleep / Teams presence" — was true, but **HIDIdleTime is not the counter the screen-lock uses**. that's the load-bearing correction.

## things we considered and discarded

- **`IOPMAssertionDeclareUserActivity` was originally proposed as the v3.0.1 fix before the mouse-jiggle attempt.** apple dev forum #26776 reports it does not prevent screen saver; our own data shows it gets implicitly invoked (via WindowServer) by any synthetic input event anyway, and it doesn't prevent the lock. we still include the explicit call in the kitchen-sink layer for completeness, but evidence suggests it isn't the missing piece.
- **caffeinate as a subprocess** — same shape as IOPMAssertion (caffeinate just creates the same assertions we now create directly).
- **`CGWarpMouseCursorPosition`** — different API that actually moves the cursor pointer rather than posting an event. would still go through the synthetic path; evidence suggests no different result, and would visibly move the cursor.
- **virtual HID device via `IOHIDUserDevice`** — would make events appear to come from real hardware (the `AppleUserHIDEventService` path). technically the right fix if userspace-CGEvent really can't reach the screen-lock counter. **but** building this requires writing an HID report descriptor, registering a virtual device, dispatching reports — ~200-400 lines of P/Invoke vs our current ~150. it also conflicts with v3's "no Xcode workload, no driver-sign hassle, just net10 + p/invoke" design value. deferred as a v3.0.2 fallback if even the kitchen-sink doesn't fix it.

## what we'll do based on the outcome

**if the kitchen sink works** (lock no longer fires in either the 60s fast-cycle or the 900s slow-cycle test):

- mark PR #8 ready-for-review
- merge to master
- tag `v3.0.1`
- cut release with fresh mac asset + windows v3.0.0 asset byte-reattached
- this doc stays in the repo as the investigation log

**if the kitchen sink doesn't work** (lock still fires despite all four layers):

- accept that **screen-lock prevention is not achievable from userspace on modern macOS without a virtual HID device**
- update the dist mac README to clearly state: prevents away-status + display dim, does NOT prevent screen lock (configure your Lock Screen settings if you want the screen to stay unlocked)
- update PR #8 to reflect the docs-only outcome
- this doc still gets committed as the investigation log
- the virtual-HID-device path remains a v3.0.2 option for anyone who wants to chase the last 10%

## one-liner cleanup if you trashed your screensaver settings testing this

```bash
defaults -currentHost delete com.apple.screensaver idleTime
defaults delete com.apple.screensaver askForPassword
defaults delete com.apple.screensaver askForPasswordDelay
```

restores to system defaults (whatever System Settings > Lock Screen says).
