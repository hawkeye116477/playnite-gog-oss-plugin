using CommonPlugin;
using CommonPlugin.Enums;
using Playnite.SDK;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Interaction logic for GogOssDownloadCompleteActionView.xaml
    /// </summary>
    public partial class GogOssDownloadCompleteActionView : UserControl
    {
        private DownloadCompleteAction downloadCompleteAction = GogOssLibrary.GetSettings().DoActionAfterDownloadComplete;
        private DispatcherTimer timer;
        private int time = 60;

        public GogOssDownloadCompleteActionView()
        {
            InitializeComponent();
        }

        private void GogOssDownloadCompleteActionUC_Loaded(object sender, RoutedEventArgs e)
        {
            CommonHelpers.SetControlBackground(this);
            switch (downloadCompleteAction)
            {
                case DownloadCompleteAction.ShutDown:
                    ActionBtn.Content = ResourceProvider.GetString(LOC.GogOss3P_PlayniteMenuShutdownSystem);
                    CountdownTB.Text = ResourceProvider.GetString(LOC.GogOssSystemShutdownCountdown);
                    break;
                case DownloadCompleteAction.Reboot:
                    ActionBtn.Content = ResourceProvider.GetString(LOC.GogOss3P_PlayniteMenuRestartSystem);
                    CountdownTB.Text = ResourceProvider.GetString(LOC.GogOssSystemRestartCountdown);
                    break;
                case DownloadCompleteAction.Hibernate:
                    ActionBtn.Content = ResourceProvider.GetString(LOC.GogOss3P_PlayniteMenuHibernateSystem);
                    CountdownTB.Text = ResourceProvider.GetString(LOC.GogOssSystemHibernateCountdown);
                    break;
                case DownloadCompleteAction.Sleep:
                    ActionBtn.Content = ResourceProvider.GetString(LOC.GogOss3P_PlayniteMenuSuspendSystem);
                    CountdownTB.Text = ResourceProvider.GetString(LOC.GogOssSystemSuspendCountdown);
                    break;
            }
            CountdownPB.Maximum = time;
            CountdownSecondsTB.Text = $"{time} s";
            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (time > 0)
            {
                time--;
                CountdownPB.Value += 1;
                CountdownSecondsTB.Text = $"{time} s";
            }
            else
            {
                CountdownPB.Value = CountdownPB.Maximum;
                timer.Stop();
                StartDownloadCompleteAction();
            }
        }

        public void StartDownloadCompleteAction()
        {
            switch (downloadCompleteAction)
            {
                case DownloadCompleteAction.ShutDown:
                    Process.Start("shutdown", "/s /t 0");
                    break;
                case DownloadCompleteAction.Reboot:
                    Process.Start("shutdown", "/r /t 0");
                    break;
                case DownloadCompleteAction.Hibernate:
                    Playnite.Native.Powrprof.SetSuspendState(true, true, false);
                    break;
                case DownloadCompleteAction.Sleep:
                    Playnite.Native.Powrprof.SetSuspendState(false, true, false);
                    break;
                default:
                    break;
            }
        }

        private void ActionBtn_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).Close();
            timer.Stop();
            StartDownloadCompleteAction();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            timer.Stop();
            Window.GetWindow(this).Close();
        }
    }
}
