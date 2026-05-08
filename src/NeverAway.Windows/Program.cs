// WinForms tray app -- preserves the v2 UX. Double-click the tray icon
// or use the context menu to toggle on/off; "Exit" stops cleanly.
//
// v3 additions:
//   - Two auto-off slots in the menu, each clickable to cycle modes.
//     Duration cycles Off <-> Once. Absolute cycles Off -> Once -> Daily -> Off.
//   - Configure auto-off... opens a modal dialog to set slot Kind + Value.
//   - Auto-on triggered by SessionUnlock or system Resume, but ONLY if
//     NeverAway was switched off automatically (not manual).

using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using NeverAway.Core;
using static NeverAway.Core.AutoOffSchedule;

namespace NeverAway.Windows;

internal class TrayApp : Form
{
    private readonly NotifyIcon trayIcon;
    private readonly IInputSimulator sim = InputSimulator.ForCurrentOs();
    private readonly Icon activeIcon = new(SystemIcons.Error, 40, 40);
    private readonly Icon inactiveIcon = new(SystemIcons.Shield, 40, 40);
    private readonly AutoOffSchedule schedule = new();
    private CancellationTokenSource? source;
    private bool isActive;

    private ToolStripMenuItem neverAwayItem = null!;
    private ToolStripMenuItem slot1Item = null!;
    private ToolStripMenuItem slot2Item = null!;
    private ToolStripMenuItem cancelScheduleItem = null!;

    [STAThread]
    private static void Main() => Application.Run(new TrayApp());

    public TrayApp()
    {
        var menu = new ContextMenuStrip();

        neverAwayItem = new ToolStripMenuItem("Never Away?");
        neverAwayItem.Click += (_, _) => Toggle();
        menu.Items.Add(neverAwayItem);

        menu.Items.Add(new ToolStripSeparator());

        slot1Item = new ToolStripMenuItem();
        slot1Item.Click += (_, _) => OnSlotClick(schedule.Slot1);
        menu.Items.Add(slot1Item);

        slot2Item = new ToolStripMenuItem();
        slot2Item.Click += (_, _) => OnSlotClick(schedule.Slot2);
        menu.Items.Add(slot2Item);

        var configureItem = new ToolStripMenuItem("Configure auto-off...");
        configureItem.Click += (_, _) => OpenConfigure();
        menu.Items.Add(configureItem);

        menu.Items.Add(new ToolStripSeparator());

        cancelScheduleItem = new ToolStripMenuItem("Cancel scheduled auto-off");
        cancelScheduleItem.Click += (_, _) => OnCancelSchedule();
        cancelScheduleItem.Visible = false;
        menu.Items.Add(cancelScheduleItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => Exit();
        menu.Items.Add(exitItem);

        trayIcon = new NotifyIcon
        {
            Icon = activeIcon,
            Visible = true,
            ContextMenuStrip = menu,
        };
        trayIcon.DoubleClick += (_, _) => Toggle();

        // Auto-on triggers: when the user comes back to the machine, if
        // NeverAway was switched off automatically (not manual), turn
        // back on so it's ready for them.
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        Toggle(); // start active
        RefreshScheduleMenu();
    }

    protected override void OnLoad(EventArgs e)
    {
        Visible = false;
        ShowInTaskbar = false;
        Opacity = 0;
        base.OnLoad(e);
    }

    private void Toggle(bool isAuto = false)
    {
        isActive = !isActive;
        if (!isActive)
        {
            schedule.Cause = isAuto ? OffCause.Auto : OffCause.Manual;
            // Manual-off clears any pending schedule -- the off it was
            // going to trigger already happened.
            if (!isAuto) schedule.Cancel();
        }
        else
        {
            schedule.Cause = OffCause.None;
        }

        neverAwayItem.Checked = isActive;
        trayIcon.Icon = isActive ? activeIcon : inactiveIcon;
        UpdateTooltip();

        if (isActive) _ = MaintainAsync();
        else source?.Cancel();

        RefreshScheduleMenu();
    }

    private void Exit()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        source?.Cancel();
        isActive = false;
        trayIcon.Visible = false;
        Application.Exit();
    }

    // Tap every 10s while active, AND check for the schedule firing.
    // Mirrors NeverAway.Console's loop with the schedule check added.
    private async Task MaintainAsync()
    {
        source = new CancellationTokenSource();
        try
        {
            while (isActive && !source.IsCancellationRequested)
            {
                try { sim.Tap(); } catch { /* one bad tap shouldn't kill the loop */ }
                await Task.Delay(TimeSpan.FromSeconds(10), source.Token);

                if (isActive && schedule.ShouldFire(DateTime.Now))
                {
                    schedule.OnFired(DateTime.Now);
                    Toggle(isAuto: true);
                }
            }
        }
        catch (TaskCanceledException) { }
        source.Dispose();
        source = null;
    }

    private void OnSlotClick(Slot slot)
    {
        schedule.Cycle(slot, DateTime.Now);
        RefreshScheduleMenu();
    }

    private void OnCancelSchedule()
    {
        schedule.Cancel();
        RefreshScheduleMenu();
    }

    private void OpenConfigure()
    {
        using var dlg = new ConfigureAutoOffForm(schedule);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            // Dialog calls schedule.Recompute() internally on Save -- we
            // just need to refresh menu labels (Value change updates the
            // displayed text) and the tooltip's FireAt time.
            RefreshScheduleMenu();
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock) MaybeAutoOn();
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) MaybeAutoOn();
    }

    // Re-arm NeverAway iff it was auto-offed (not manually). System events
    // may fire on a non-UI thread -- marshal back to UI for the toggle.
    private void MaybeAutoOn()
    {
        if (isActive || schedule.Cause != OffCause.Auto) return;
        if (InvokeRequired) BeginInvoke(() => Toggle());
        else Toggle();
    }

    private void RefreshScheduleMenu()
    {
        slot1Item.Text = LabelFor(schedule.Slot1);
        slot1Item.CheckState = CheckStateFor(schedule.Slot1);
        slot2Item.Text = LabelFor(schedule.Slot2);
        slot2Item.CheckState = CheckStateFor(schedule.Slot2);
        cancelScheduleItem.Visible = schedule.ActiveSlot is not null;
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        if (!isActive) trayIcon.Text = "NeverAway is off.";
        else if (schedule.FireAt is { } t) trayIcon.Text = $"NeverAway is on. Auto-off at {t:h:mm tt}.";
        else trayIcon.Text = "NeverAway is on.";
    }

    private static string LabelFor(Slot slot)
    {
        var baseLabel = slot.Kind == SlotKind.Duration
            ? FormatDuration(slot.Value)
            : $"Auto-off at {DateTime.Today.Add(slot.Value):h:mm tt}";

        return slot.Mode switch
        {
            SlotMode.Once  => $"{baseLabel} (once)",
            SlotMode.Daily => $"Auto-off daily at {DateTime.Today.Add(slot.Value):h:mm tt}",
            _              => baseLabel,
        };
    }

    private static string FormatDuration(TimeSpan ts)
    {
        var hours = (int)ts.TotalHours;
        var minutes = ts.Minutes;
        if (hours > 0 && minutes > 0) return $"Auto-off in {hours}h {minutes}m";
        if (hours > 0)                return $"Auto-off in {hours} hour{(hours == 1 ? "" : "s")}";
        return $"Auto-off in {minutes} minute{(minutes == 1 ? "" : "s")}";
    }

    private static CheckState CheckStateFor(Slot slot) => slot.Mode switch
    {
        SlotMode.Once  => CheckState.Checked,
        SlotMode.Daily => CheckState.Indeterminate, // visually bolder than a check
        _              => CheckState.Unchecked,
    };
}
