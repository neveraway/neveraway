using System;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;



namespace neveraway
{
    class neverawayApp : Form
    {
        private NotifyIcon trayIcon;
        private bool isActive = false;
        private Icon activeIcon = new Icon(SystemIcons.Error, 40, 40);
        private Icon inactiveIcon = new Icon(SystemIcons.Shield, 40, 40);
        private CancellationTokenSource source;
        // [DllImport("user32.dll")]
        // private static extern int SetKeyboardState(byte[] keyState);

        // private const Byte[]

        protected override void OnLoad(EventArgs e)
        {
            Visible = false; // Hide form window.
            ShowInTaskbar = false; // Remove from taskbar.
            Opacity = 0;     // See through, just in case.
            base.OnLoad(e);
        }

        [STAThread]
        static void Main()
        {
            Application.Run(new neverawayApp());
        }

        public neverawayApp()
        {
            trayIcon = new System.Windows.Forms.NotifyIcon();
            trayIcon.Icon = activeIcon;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => OnClick(s, e);
            trayIcon.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
            trayIcon.ContextMenuStrip.Items.Add("Never Away?").Click += (s, e) => OnClick(s, e);
            trayIcon.ContextMenuStrip.Items.Add($"Exit").Click += (s, e) => OnExit(s, e);
            ToggleStatus();
            MaintainStatus();
        }
        private void OnClick(object sender, EventArgs e)
        {
            ToggleStatus();
        }
        private void OnExit(object sender, EventArgs e)
        {
            source?.Cancel();
            isActive = false;
            trayIcon.Visible = false;
            Application.Exit();
        }
        private void ToggleStatus()
        {
            ((ToolStripMenuItem)trayIcon.ContextMenuStrip.Items[0]).Checked = isActive = !isActive;
            trayIcon.Icon = isActive ? activeIcon : inactiveIcon;
            trayIcon.Text = isActive ? "NeverAway is on." : "NeverAway is off.";
            if (isActive)
            {
                MaintainStatus();
            }
            else
            {
                source?.Cancel();
            }
        }
        public async Task MaintainStatus()
        {
            source = new CancellationTokenSource();
            while (isActive)
            {
                Keyboard.KeyUp((byte)Keys.F24);
                await Task.Delay(10 * 1000);
                if (source.Token.IsCancellationRequested)
                {
                    break;
                }
            }
            source.Dispose();
        }

    }
    //ref https://stackoverflow.com/questions/16342599/c-sharp-hold-key-in-a-game-application
    public class Keyboard
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern void keybd_event(byte bVk, byte bScan, int dwFlags, int dwExtraInfo);
        const int KEY_UP_EVENT = 0x0002; //Key up flag

        public static void KeyUp(byte key)
        {
            keybd_event(key, 0, KEY_UP_EVENT, 0);
        }

    }

}
