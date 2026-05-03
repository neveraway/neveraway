namespace NeverAway.Core;

// Auto-off scheduler -- decides when NeverAway should switch itself off,
// and (paired with the platform's lock/unlock events in the consuming
// app) when it should switch itself back on.
//
// Two slots, only one armed at a time. Each slot has:
//   Kind  = Duration | Absolute   (set via Configure dialog)
//   Value = TimeSpan              (the duration or time-of-day)
//   Mode  = Off | Once | Daily    (cycled by clicking the slot)
//
// Click semantics:
//   Duration slot cycles Off <-> Once   (one-shot timer)
//   Absolute slot cycles Off -> Once -> Daily -> Off
//
// Daily mode auto-rearms after firing -- FireAt recomputes to the next
// matching time-of-day. Once mode unchecks after firing.
//
// State is per-session: no persistence across app restart. The consuming
// app reacts to lock/unlock events to handle the auto-on side.
public sealed class AutoOffSchedule
{
    public enum SlotKind { Duration, Absolute }
    public enum SlotMode { Off, Once, Daily }
    public enum OffCause { None, Manual, Auto }

    public sealed class Slot
    {
        public SlotKind Kind { get; set; }
        public TimeSpan Value { get; set; }
        public SlotMode Mode { get; internal set; } = SlotMode.Off;

        // Reset Mode to Off. Used by the consuming app's Configure dialog
        // when the user changes Kind -- the old Mode (e.g., Daily) may
        // be invalid for the new Kind, so cleanest to drop it.
        public void Reset() => Mode = SlotMode.Off;
    }

    public Slot Slot1 { get; } = new() { Kind = SlotKind.Duration, Value = TimeSpan.FromHours(2) };
    public Slot Slot2 { get; } = new() { Kind = SlotKind.Absolute, Value = new TimeSpan(18, 0, 0) };

    public DateTime? FireAt { get; private set; }

    // Why NeverAway is currently off, set by the consuming app when it
    // toggles. Auto-on logic reads this to decide whether to re-arm
    // on a lock/unlock event.
    public OffCause Cause { get; set; } = OffCause.None;

    // Cycle a slot's Mode by one click. Activating a slot deactivates
    // the other slot (only one schedule armed at a time). Recomputes
    // FireAt based on the new state.
    public void Cycle(Slot slot, DateTime now)
    {
        var other = ReferenceEquals(slot, Slot1) ? Slot2 : Slot1;
        var nextMode = NextMode(slot);
        slot.Mode = nextMode;
        if (nextMode != SlotMode.Off) other.Mode = SlotMode.Off;
        Recompute(now);
    }

    // Cancel any active schedule -- both slots back to Off, FireAt cleared.
    // Used by the explicit "Cancel scheduled auto-off" menu item AND when
    // the user manually toggles NeverAway off (clear pending schedule
    // since the off it would have triggered already happened).
    public void Cancel()
    {
        Slot1.Mode = SlotMode.Off;
        Slot2.Mode = SlotMode.Off;
        FireAt = null;
    }

    public bool ShouldFire(DateTime now) =>
        FireAt is { } t && now >= t;

    // Called by the consuming app after the schedule fires (NeverAway
    // toggled off). For Once: clear the slot. For Daily: recompute
    // FireAt to tomorrow same time so the slot stays armed.
    public void OnFired(DateTime now)
    {
        var active = ActiveSlot;
        if (active is null) return;
        if (active.Mode == SlotMode.Once)
        {
            active.Mode = SlotMode.Off;
            FireAt = null;
        }
        else if (active.Mode == SlotMode.Daily)
        {
            // Daily only valid for Absolute slots, and FireAt for Daily
            // is always today/tomorrow at AbsoluteValue. After firing,
            // next fire is tomorrow at the same time.
            FireAt = NextFireTime(now, active.Value);
        }
    }

    public Slot? ActiveSlot
    {
        get
        {
            if (Slot1.Mode != SlotMode.Off) return Slot1;
            if (Slot2.Mode != SlotMode.Off) return Slot2;
            return null;
        }
    }

    // Pure helper: next firing time for an absolute time-of-day given
    // the current time. Today if not yet reached; tomorrow if today's
    // slot is already past (treat exact-match as past so the schedule
    // advances rather than firing instantly).
    public static DateTime NextFireTime(DateTime now, TimeSpan timeOfDay)
    {
        var todayAt = now.Date.Add(timeOfDay);
        return now < todayAt ? todayAt : todayAt.AddDays(1);
    }

    private SlotMode NextMode(Slot slot) => (slot.Kind, slot.Mode) switch
    {
        (SlotKind.Duration, SlotMode.Off) => SlotMode.Once,
        (SlotKind.Duration, _)            => SlotMode.Off,
        (SlotKind.Absolute, SlotMode.Off)   => SlotMode.Once,
        (SlotKind.Absolute, SlotMode.Once)  => SlotMode.Daily,
        (SlotKind.Absolute, SlotMode.Daily) => SlotMode.Off,
        _ => SlotMode.Off,
    };

    // Re-derive FireAt from the currently-active slot's Kind + Value.
    // Public because the consuming Configure dialog calls this after
    // committing edits -- a Value change on an active slot needs to
    // refresh FireAt without re-cycling Mode.
    public void Recompute(DateTime now)
    {
        var active = ActiveSlot;
        if (active is null) { FireAt = null; return; }
        FireAt = active.Kind switch
        {
            SlotKind.Duration => now + active.Value,
            SlotKind.Absolute => NextFireTime(now, active.Value),
            _ => null,
        };
    }
}
