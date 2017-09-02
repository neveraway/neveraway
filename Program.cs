using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using WindowsInput;
using System.Linq;
using System.ComponentModel;
using s = NeverAway.Properties.Settings;

namespace NeverAway
{
    public class NeverAwayApp : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenu trayMenu;
        private Icon NormalAwayIcon;
        private Icon NeverAwayIcon;
        private bool _NeverAway;
        private BackgroundWorker bw;
        private int ThreadSleep;
        private int ShowBalloonTipTimeout;
        private int TimesToPressKey;
        private string KeyToPress;

        [STAThread]
        public static void Main()
        {
            Application.Run(new NeverAwayApp());
        }

        public NeverAwayApp()
        {
            try
            {
                NormalAwayIcon = new Icon(SystemIcons.Shield, 40, 40);
                NeverAwayIcon = new Icon(SystemIcons.Hand, 40, 40);
                _NeverAway = false;
                bw = null;

                ThreadSleep = s.Default.TimeToWaitBetweenKeyPressesInMS;
                ShowBalloonTipTimeout = s.Default.ShowBalloonTipTimeout;
                TimesToPressKey = s.Default.TimesToPressKey;
                KeyToPress = s.Default.KeyToPress;

                //Build the tray menu
                InitializeTrayMenu();
            }
            catch (Exception ex)
            {
                this.Dispose();
                MessageBox.Show(Strings.InitializationError);
                throw (ex);
            }
        }
        private void InitializeTrayMenu()
        {
            //create the tray menu
            trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add(Strings.trayMenuNeverAway, OnAway);
            trayMenu.MenuItems.Add(Strings.trayMenuExit, OnExit);

            //add menu to tray icon and show it.
            trayIcon = new NotifyIcon();
            trayIcon.ContextMenu = trayMenu;
            trayIcon.Visible = true;

            //Set the normal away messages and icons
            UpdateTrayStatus();
        }

        private void InitializeBackgroundWorker()
        {
            bw = new BackgroundWorker();
            bw.WorkerSupportsCancellation = true;
            bw.DoWork += new DoWorkEventHandler(bw_DoWork);
        }

        private void CleanupBackgroundWorker()
        {
            if (bw != null)
            {
                bw.CancelAsync();
                bw.Dispose();
                bw = null;
            }
        }
        protected override void OnLoad(EventArgs e)
        {
            Visible       = false; // Hide form window.
            ShowInTaskbar = false; // Remove from taskbar.
            base.OnLoad(e);
        }

        private void OnExit(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            Application.Exit();
        }

        private void OnAway(object sender, EventArgs e)
        {

            _NeverAway = !_NeverAway;
            UpdateTrayStatus();

            CleanupBackgroundWorker();
            if (_NeverAway)
            {
                InitializeBackgroundWorker();
                bw.RunWorkerAsync();
            }

        }

        private void UpdateTrayStatus()
        {
            trayIcon.Text = _NeverAway ? Strings.neverAwayText : Strings.normalAwayText;
            trayIcon.ContextMenu.MenuItems[0].Checked = _NeverAway;
            trayIcon.Icon = _NeverAway ? NeverAwayIcon : NormalAwayIcon;
        }

        private void bw_DoWork(object sender, DoWorkEventArgs e)
        {
            int i = 0;
            while (true)
            {
                if ((((BackgroundWorker)sender).CancellationPending))
                {
                    e.Cancel = true;
                    break;
                }
                else
                {
                    i++;
                    string statusmessage;
                    try
                    {
                        statusmessage = String.Format(Strings.statusMessage, DateTime.Now.ToLocalTime().ToString(), KeyToPress, i.ToString());
                        for (int c = 0; c < TimesToPressKey; c++)
                            InputSimulator.SimulateKeyPress(
                                (VirtualKeyCode)System.Enum.Parse(typeof(VirtualKeyCode), KeyToPress)
                                );
                    }
                    catch (Exception Ex)
                    {
                        statusmessage = String.Format(Strings.errorMessage, DateTime.Now.ToLocalTime().ToString(), Ex.Message, Ex.StackTrace);
                        _NeverAway = false;
                        UpdateTrayStatus();
                        bw.CancelAsync();//probably a better place to put this, but it seems to work ok.
                    }
                    trayIcon.ShowBalloonTip(ShowBalloonTipTimeout, Strings.tipTitle, statusmessage, ToolTipIcon.Info);
                    System.Threading.Thread.Sleep(ThreadSleep);
                }
            }
        }
       
        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                // Dispose of our other resources
                NormalAwayIcon.Dispose();
                NeverAwayIcon.Dispose();
                trayIcon.Dispose();
                CleanupBackgroundWorker();
            }

            base.Dispose(isDisposing);
        }
    }
}