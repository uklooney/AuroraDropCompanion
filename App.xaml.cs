using System;
using System.Windows;
//using System.Windows.Forms;

namespace AuroraDropCompanion
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {

        System.Windows.Forms.NotifyIcon notifyIcon = new System.Windows.Forms.NotifyIcon();

        protected override void OnStartup(StartupEventArgs e)
        {
            //notifyIcon.Icon = new System.Drawing.Icon("Resources/icon.ico");
            //notifyIcon.Text = "Show Status";
            //notifyIcon.Visible = true;

            //notifyIcon.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
            //notifyIcon.ContextMenuStrip.Items.Add(new System.Windows.Forms.ToolStripLabel("Case LED Matrix"));
            //notifyIcon.ContextMenuStrip.Items.Add("-");

            //notifyIcon.ContextMenuStrip.Items.Add("Audio Analyser", Image.FromFile("Resources/icon.ico"), OnStatusClicked);
            //notifyIcon.ContextMenuStrip.Items.Add("Demo Mode", Image.FromFile("Resources/icon.ico"), OnStatusClicked);
            //notifyIcon.ContextMenuStrip.Items.Add("-");
            //notifyIcon.ContextMenuStrip.Items.Add("Status", Image.FromFile("Resources/icon.ico"), OnStatusClicked);

            base.OnStartup(e);
        }

        private void OnStatusClicked(object sender, EventArgs e)
        {
            MessageBox.Show("Application is running", "Status", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            notifyIcon.Dispose();
        }
    }


    static class SavedVar
    {
        /*
                public static double WindowTop = 0;
                public static double WindowLeft = 0;
                public static double WindowWidth = 400;
                public static double WindowHeight = 40;
                public static int BarWidth = 5;
                public static int BarSpacing = 1;
                public static int BarHeight = 1;
                public static int BarPeaks = 1;
                public static int PeakHoldTime = 5;
                public static int PeakFallOffSpeed = 20;
                public static double Opacity = 0.8;
                public static double InputGain = 1.5;
                public static int Equalizer = 0;
                public static double Eq1 = 0.0;
                public static double Eq2 = 0.0;
                public static double Eq3 = 0.0;
                public static double Eq4 = 0.0;
                public static double Eq5 = 0.0;
                public static double Eq6 = 0.0;
                public static int ColourScheme = 10;
                public static int Style = 1;
                public static int Channels = 1;
                public static int ForceTopMost = 1;
                public static int LoadStartup = 0;

                public static bool settingsOpen = false;
                public static bool dirty = false;
                public static String info = "";
        */
        public static double frameCount = 0.0;
        public static int bufferSize = 0;

    }

}
