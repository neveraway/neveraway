using Xunit;
using static NeverAway.Core.AutoOffSchedule;

namespace NeverAway.Core.Tests;

public class AutoOffScheduleTests
{
    private static readonly DateTime Now = new(2026, 5, 2, 10, 30, 0); // 10:30 AM
    private static readonly TimeSpan SixPM = new(18, 0, 0);

    [Fact]
    public void NextFireTime_BeforeTimeToday_ReturnsToday()
    {
        var result = NextFireTime(Now, SixPM);
        Assert.Equal(new DateTime(2026, 5, 2, 18, 0, 0), result);
    }

    [Fact]
    public void NextFireTime_AfterTimeToday_ReturnsTomorrow()
    {
        var lateAfternoon = new DateTime(2026, 5, 2, 19, 0, 0);
        var result = NextFireTime(lateAfternoon, SixPM);
        Assert.Equal(new DateTime(2026, 5, 3, 18, 0, 0), result);
    }

    [Fact]
    public void NextFireTime_ExactMatch_ReturnsTomorrow()
    {
        // Treat exact match as past so schedule advances instead of firing instantly.
        var exactly6pm = new DateTime(2026, 5, 2, 18, 0, 0);
        var result = NextFireTime(exactly6pm, SixPM);
        Assert.Equal(new DateTime(2026, 5, 3, 18, 0, 0), result);
    }

    [Fact]
    public void DurationSlot_CyclesOffToOnce()
    {
        var s = new AutoOffSchedule();
        s.Slot1.Kind = SlotKind.Duration;
        s.Slot1.Value = TimeSpan.FromHours(2);

        Assert.Equal(SlotMode.Off, s.Slot1.Mode);
        s.Cycle(s.Slot1, Now);
        Assert.Equal(SlotMode.Once, s.Slot1.Mode);
        Assert.Equal(Now + TimeSpan.FromHours(2), s.FireAt);
    }

    [Fact]
    public void DurationSlot_CyclesOnceToOff()
    {
        var s = new AutoOffSchedule();
        s.Slot1.Kind = SlotKind.Duration;
        s.Slot1.Value = TimeSpan.FromHours(2);

        s.Cycle(s.Slot1, Now); // -> Once
        s.Cycle(s.Slot1, Now); // -> Off
        Assert.Equal(SlotMode.Off, s.Slot1.Mode);
        Assert.Null(s.FireAt);
    }

    [Fact]
    public void AbsoluteSlot_CyclesOffOnceDailyOff()
    {
        var s = new AutoOffSchedule();
        s.Slot2.Kind = SlotKind.Absolute;
        s.Slot2.Value = SixPM;

        Assert.Equal(SlotMode.Off, s.Slot2.Mode);
        s.Cycle(s.Slot2, Now);
        Assert.Equal(SlotMode.Once, s.Slot2.Mode);
        s.Cycle(s.Slot2, Now);
        Assert.Equal(SlotMode.Daily, s.Slot2.Mode);
        s.Cycle(s.Slot2, Now);
        Assert.Equal(SlotMode.Off, s.Slot2.Mode);
    }

    [Fact]
    public void ActivatingSlot_DeactivatesOtherSlot()
    {
        var s = new AutoOffSchedule();
        s.Slot1.Kind = SlotKind.Duration;
        s.Slot1.Value = TimeSpan.FromHours(2);
        s.Slot2.Kind = SlotKind.Absolute;
        s.Slot2.Value = SixPM;

        s.Cycle(s.Slot1, Now); // Slot1 -> Once
        Assert.Equal(SlotMode.Once, s.Slot1.Mode);
        Assert.Equal(SlotMode.Off, s.Slot2.Mode);

        s.Cycle(s.Slot2, Now); // Slot2 -> Once, Slot1 should clear
        Assert.Equal(SlotMode.Off, s.Slot1.Mode);
        Assert.Equal(SlotMode.Once, s.Slot2.Mode);
    }

    [Fact]
    public void OnFired_OnceSlot_ClearsModeAndFireAt()
    {
        var s = new AutoOffSchedule();
        s.Slot1.Kind = SlotKind.Duration;
        s.Slot1.Value = TimeSpan.FromHours(2);
        s.Cycle(s.Slot1, Now);

        var fireTime = Now + TimeSpan.FromHours(2);
        s.OnFired(fireTime);

        Assert.Equal(SlotMode.Off, s.Slot1.Mode);
        Assert.Null(s.FireAt);
    }

    [Fact]
    public void OnFired_DailySlot_RecomputesToTomorrow()
    {
        var s = new AutoOffSchedule();
        s.Slot2.Kind = SlotKind.Absolute;
        s.Slot2.Value = SixPM;
        s.Cycle(s.Slot2, Now); // -> Once
        s.Cycle(s.Slot2, Now); // -> Daily
        Assert.Equal(SlotMode.Daily, s.Slot2.Mode);
        Assert.Equal(new DateTime(2026, 5, 2, 18, 0, 0), s.FireAt);

        // Simulate fire at exactly 6pm today
        var fireTime = new DateTime(2026, 5, 2, 18, 0, 0);
        s.OnFired(fireTime);

        // Slot stays Daily, FireAt advances to tomorrow same time
        Assert.Equal(SlotMode.Daily, s.Slot2.Mode);
        Assert.Equal(new DateTime(2026, 5, 3, 18, 0, 0), s.FireAt);
    }

    [Fact]
    public void ShouldFire_BeforeFireTime_False()
    {
        var s = new AutoOffSchedule();
        s.Slot1.Kind = SlotKind.Duration;
        s.Slot1.Value = TimeSpan.FromHours(2);
        s.Cycle(s.Slot1, Now);

        Assert.False(s.ShouldFire(Now + TimeSpan.FromHours(1)));
    }

    [Fact]
    public void ShouldFire_AtOrAfterFireTime_True()
    {
        var s = new AutoOffSchedule();
        s.Slot1.Kind = SlotKind.Duration;
        s.Slot1.Value = TimeSpan.FromHours(2);
        s.Cycle(s.Slot1, Now);

        Assert.True(s.ShouldFire(Now + TimeSpan.FromHours(2)));
        Assert.True(s.ShouldFire(Now + TimeSpan.FromHours(3)));
    }

    [Fact]
    public void ShouldFire_NoSchedule_False()
    {
        var s = new AutoOffSchedule();
        Assert.False(s.ShouldFire(Now));
    }

    [Fact]
    public void Cancel_ClearsBothSlots()
    {
        var s = new AutoOffSchedule();
        s.Slot1.Kind = SlotKind.Duration;
        s.Slot1.Value = TimeSpan.FromHours(2);
        s.Cycle(s.Slot1, Now);
        Assert.Equal(SlotMode.Once, s.Slot1.Mode);
        Assert.NotNull(s.FireAt);

        s.Cancel();
        Assert.Equal(SlotMode.Off, s.Slot1.Mode);
        Assert.Equal(SlotMode.Off, s.Slot2.Mode);
        Assert.Null(s.FireAt);
    }
}
