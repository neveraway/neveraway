using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NeverAway;

public class NeverAwayApp : Form
{
    private readonly Icon _activeIcon = new(SystemIcons.Error, 40, 40);
    private readonly Icon _inactiveIcon = new(SystemIcons.Shield, 40, 40);
    private readonly NotifyIcon _trayIcon;
    private CancellationTokenSource _cancellationTokenSource;

    public NeverAwayApp()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = _activeIcon,
            Visible = true
        };

        _trayIcon.DoubleClick += OnClick;
        _trayIcon.ContextMenuStrip = new ContextMenuStrip();
        _trayIcon.ContextMenuStrip.Items.Add("Never Away?").Click += OnClick;
        _trayIcon.ContextMenuStrip.Items.Add($"Exit").Click += OnExit;

        ToggleStatus();
        _ = MaintainStatus();
    }

    private bool isActive { get; set; } = false;

    public async Task MaintainStatus()
    {
        _cancellationTokenSource = new CancellationTokenSource();
        while (isActive)
        {
            Keyboard.KeyUp((byte)Keys.F24);
            await Task.Delay(10 * 1000);
            if (_cancellationTokenSource.Token.IsCancellationRequested)
            {
                break;
            }
        }
        _cancellationTokenSource.Dispose();
    }

    protected override void OnLoad(EventArgs e)
    {
        Visible = false; // Hide form window.
        ShowInTaskbar = false; // Remove from taskbar.
        Opacity = 0;     // See through, just in case.
        base.OnLoad(e);
    }

    private void OnClick(object sender, EventArgs e)
    {
        ToggleStatus();
    }

    private void OnExit(object sender, EventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        isActive = false;
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private void ToggleStatus()
    {
        ((ToolStripMenuItem)_trayIcon.ContextMenuStrip.Items[0]).Checked = isActive = !isActive;

        _trayIcon.Icon = isActive ? _activeIcon : _inactiveIcon;
        _trayIcon.Text = isActive ? "NeverAway is on." : "NeverAway is off.";

        if (isActive)
        {
            _ = MaintainStatus();
        }
        else
        {
            _cancellationTokenSource?.Cancel();
        }
    }
}
