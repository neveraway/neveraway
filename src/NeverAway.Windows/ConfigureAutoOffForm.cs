// Modal dialog to configure the two auto-off slots: pick Kind (Duration
// or Absolute) and Value (hours/minutes for Duration, time-of-day for
// Absolute) per slot. Save commits to AutoOffSchedule; if a slot's Kind
// changed, that slot's Mode resets to Off (cleanup -- old Mode may be
// invalid for the new Kind, e.g., Daily on a Duration slot).

using System.Windows.Forms;
using NeverAway.Core;
using static NeverAway.Core.AutoOffSchedule;

namespace NeverAway.Windows;

internal sealed class ConfigureAutoOffForm : Form
{
    private readonly AutoOffSchedule schedule;
    private readonly SlotControls slot1Controls;
    private readonly SlotControls slot2Controls;

    public ConfigureAutoOffForm(AutoOffSchedule schedule)
    {
        this.schedule = schedule;

        Text = "Configure auto-off";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(380, 260);

        slot1Controls = BuildSlotGroup("Slot 1", schedule.Slot1, top: 10);
        slot2Controls = BuildSlotGroup("Slot 2", schedule.Slot2, top: 110);

        var saveBtn = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Left = 200, Top = 220, Width = 80,
        };
        saveBtn.Click += (_, _) => OnSave();
        Controls.Add(saveBtn);

        var cancelBtn = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Left = 290, Top = 220, Width = 80,
        };
        Controls.Add(cancelBtn);

        AcceptButton = saveBtn;
        CancelButton = cancelBtn;
    }

    private SlotControls BuildSlotGroup(string title, Slot slot, int top)
    {
        var group = new GroupBox { Text = title, Left = 10, Top = top, Width = 360, Height = 90 };
        Controls.Add(group);

        var durationRadio = new RadioButton { Text = "Duration", Left = 10, Top = 20, Width = 80, Checked = slot.Kind == SlotKind.Duration };
        var absoluteRadio = new RadioButton { Text = "At time",  Left = 10, Top = 50, Width = 80, Checked = slot.Kind == SlotKind.Absolute };
        group.Controls.Add(durationRadio);
        group.Controls.Add(absoluteRadio);

        var hoursInput = new NumericUpDown { Left = 100, Top = 20, Width = 50, Minimum = 0, Maximum = 23, Value = (int)slot.Value.TotalHours };
        var hoursLabel = new Label { Text = "hours", Left = 155, Top = 22, Width = 40 };
        var minutesInput = new NumericUpDown { Left = 200, Top = 20, Width = 50, Minimum = 0, Maximum = 59, Value = slot.Kind == SlotKind.Duration ? slot.Value.Minutes : 0 };
        var minutesLabel = new Label { Text = "minutes", Left = 255, Top = 22, Width = 50 };
        group.Controls.Add(hoursInput);
        group.Controls.Add(hoursLabel);
        group.Controls.Add(minutesInput);
        group.Controls.Add(minutesLabel);

        var timePicker = new DateTimePicker
        {
            Left = 100, Top = 50, Width = 100,
            Format = DateTimePickerFormat.Time, ShowUpDown = true,
            Value = DateTime.Today.Add(slot.Kind == SlotKind.Absolute ? slot.Value : new TimeSpan(18, 0, 0)),
        };
        group.Controls.Add(timePicker);

        // Enable/disable inputs based on selected radio
        void Refresh()
        {
            hoursInput.Enabled = durationRadio.Checked;
            minutesInput.Enabled = durationRadio.Checked;
            timePicker.Enabled = absoluteRadio.Checked;
        }
        durationRadio.CheckedChanged += (_, _) => Refresh();
        absoluteRadio.CheckedChanged += (_, _) => Refresh();
        Refresh();

        return new SlotControls(slot, durationRadio, hoursInput, minutesInput, timePicker);
    }

    private void OnSave()
    {
        ApplySlot(slot1Controls);
        ApplySlot(slot2Controls);
        // After applying changes: if a Kind changed on the active slot,
        // its Mode was reset to Off (so ActiveSlot is now null) and
        // Recompute clears FireAt. If only Values changed, Recompute
        // refreshes FireAt against the new Value while preserving Mode.
        schedule.Recompute(DateTime.Now);
    }

    private void ApplySlot(SlotControls c)
    {
        var newKind = c.DurationRadio.Checked ? SlotKind.Duration : SlotKind.Absolute;
        var kindChanged = c.Slot.Kind != newKind;

        c.Slot.Kind = newKind;
        c.Slot.Value = newKind == SlotKind.Duration
            ? TimeSpan.FromHours((double)c.HoursInput.Value) + TimeSpan.FromMinutes((double)c.MinutesInput.Value)
            : c.TimePicker.Value.TimeOfDay;

        if (kindChanged) c.Slot.Reset();
    }

    private sealed record SlotControls(
        Slot Slot,
        RadioButton DurationRadio,
        NumericUpDown HoursInput,
        NumericUpDown MinutesInput,
        DateTimePicker TimePicker);
}
