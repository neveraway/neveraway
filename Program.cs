using System;
using System.Windows.Forms;
using System.Drawing;

namespace neveraway
{
    class neverawayApp:Form
    {
        private NotifyIcon trayIcon;
        private bool isActive = true;
        private Icon activeIcon = new Icon(SystemIcons.Error, 40, 40);
        private Icon inactiveIcon = new Icon(SystemIcons.Shield, 40, 40);


        protected override void OnLoad(EventArgs e)
        {
            Visible       = false; // Hide form window.
            ShowInTaskbar = false; // Remove from taskbar.
            Opacity       = 0;     // See through, just in case.
            base.OnLoad(e);
        }

        [STAThread]
        static void Main()
        {
             Application.Run(new neverawayApp());
        }

        public neverawayApp(){
            SetupNotifyIcon();
        }
        private void SetupNotifyIcon(){
            trayIcon = new System.Windows.Forms.NotifyIcon();
            trayIcon.Icon = activeIcon;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => OnDoubleClick(s,e);
            trayIcon.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
            trayIcon.ContextMenuStrip.Items.Add("Do Something?").Click += (s, e) => OnClick(s,e);
            trayIcon.ContextMenuStrip.Items.Add($"Exit").Click += (s, e) => OnExit(s,e);
            trayIcon.ShowBalloonTip(500, "Status", "Started Up!", ToolTipIcon.Info);
        }
        private void OnClick(object sender, EventArgs e)
        {
            MessageBox.Show("You did something!","Congrats!");
        }
        private void OnDoubleClick(object sender, EventArgs e)
        {
            isActive = !isActive;
            trayIcon.Icon = isActive ? activeIcon : inactiveIcon;
        }
        private void OnExit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            Application.Exit();
        }
    }
}
