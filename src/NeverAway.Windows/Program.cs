// WinForms tray app — preserves the v2 UX. Double-click the tray icon
// or use the context menu to toggle on/off; "Exit" stops cleanly.

using System.Drawing;
using System.Windows.Forms;
using NeverAway.Core;

namespace NeverAway.Windows;

internal class TrayApp : Form
{
    private readonly NotifyIcon trayIcon;
    private readonly IInputSimulator sim = InputSimulator.ForCurrentOs();
    private readonly Icon activeIcon = new(SystemIcons.Error, 40, 40);
    private readonly Icon inactiveIcon = new(SystemIcons.Shield, 40, 40);
    private CancellationTokenSource? source;
    private bool isActive;

    [STAThread]
    private static void Main() => Application.Run(new TrayApp());

    public TrayApp()
    {
        trayIcon = new NotifyIcon
        {
            Icon = activeIcon,
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        trayIcon.DoubleClick += (_, _) => Toggle();
        trayIcon.ContextMenuStrip.Items.Add("Never Away?").Click += (_, _) => Toggle();
        trayIcon.ContextMenuStrip.Items.Add("Exit").Click       += (_, _) => Exit();
        Toggle();
    }

    protected override void OnLoad(EventArgs e)
    {
        Visible = false;
        ShowInTaskbar = false;
        Opacity = 0;
        base.OnLoad(e);
    }

    private void Toggle()
    {
        isActive = !isActive;
        ((ToolStripMenuItem)trayIcon.ContextMenuStrip!.Items[0]!).Checked = isActive;
        trayIcon.Icon = isActive ? activeIcon : inactiveIcon;
        trayIcon.Text = isActive ? "NeverAway is on." : "NeverAway is off.";
        if (isActive) _ = MaintainAsync();
        else source?.Cancel();
    }

    private void Exit()
    {
        source?.Cancel();
        isActive = false;
        trayIcon.Visible = false;
        Application.Exit();
    }

    // Tap every 10s while active. Mirrors NeverAway.Console's loop.
    private async Task MaintainAsync()
    {
        source = new CancellationTokenSource();
        try
        {
            while (isActive && !source.IsCancellationRequested)
            {
                try { sim.Tap(); } catch { /* one bad tap shouldn't kill the loop */ }
                await Task.Delay(TimeSpan.FromSeconds(10), source.Token);
            }
        }
        catch (TaskCanceledException) { }
        source.Dispose();
        source = null;
    }
}
