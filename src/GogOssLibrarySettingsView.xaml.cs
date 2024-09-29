using GogOssLibraryNS.Models;
using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Services;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Interaction logic for GogOssLibrarySettingsView.xaml
    /// </summary>
    public partial class GogOssLibrarySettingsView : UserControl
    {
        private IPlayniteAPI playniteAPI = API.Instance;
        public GogOssTroubleshootingInformation troubleshootingInformation;
        private ILogger logger = LogManager.GetLogger();

        public GogOssLibrarySettingsView()
        {
            InitializeComponent();
            UpdateAuthStatus();
        }

        private void ChooseGamePathBtn_Click(object sender, RoutedEventArgs e)
        {

        }

        private void ChooseCometBtn_Click(object sender, RoutedEventArgs e)
        {
            var file = playniteAPI.Dialogs.SelectFile($"{ResourceProvider.GetString(LOC.GogOss3P_PlayniteExecutableTitle)}|*.exe");
            if (file != "")
            {
                SelectedCometPathTxt.Text = file;
            }
        }

        private async void CometSettingsUC_Loaded(object sender, RoutedEventArgs e)
        {
            MaxWorkersNI.MaxValue = Helpers.CpuThreadsNumber;
            var downloadCompleteActions = new Dictionary<DownloadCompleteAction, string>
            {
                { DownloadCompleteAction.Nothing, ResourceProvider.GetString(LOC.GogOss3P_PlayniteDoNothing) },
                { DownloadCompleteAction.ShutDown, ResourceProvider.GetString(LOC.GogOss3P_PlayniteMenuShutdownSystem) },
                { DownloadCompleteAction.Reboot, ResourceProvider.GetString(LOC.GogOss3P_PlayniteMenuRestartSystem) },
                { DownloadCompleteAction.Hibernate, ResourceProvider.GetString(LOC.GogOss3P_PlayniteMenuHibernateSystem) },
                { DownloadCompleteAction.Sleep, ResourceProvider.GetString(LOC.GogOss3P_PlayniteMenuSuspendSystem) },
            };
            AfterDownloadCompleteCBo.ItemsSource = downloadCompleteActions;
            troubleshootingInformation = new GogOssTroubleshootingInformation();
            if (Comet.IsInstalled)
            {
                var cometVersion = await Comet.GetCometVersion();
                if (!cometVersion.IsNullOrEmpty())
                {
                    troubleshootingInformation.CometVersion = cometVersion;
                    LauncherVersionTxt.Text = troubleshootingInformation.CometVersion;
                }
                CometBinaryTxt.Text = troubleshootingInformation.CometBinary;
            }
            else
            {
                troubleshootingInformation.CometVersion = "Not%20installed";
                LauncherVersionTxt.Text = ResourceProvider.GetString(LOC.GogOssCometNotInstalled);
                CometBinaryTxt.Text = ResourceProvider.GetString(LOC.GogOssCometNotInstalled);
                CheckForUpdatesBtn.IsEnabled = false;
                OpenLauncherBinaryBtn.IsEnabled = false;
            }
            PlayniteVersionTxt.Text = troubleshootingInformation.PlayniteVersion;
            PluginVersionTxt.Text = troubleshootingInformation.PluginVersion;
            GamesInstallationPathTxt.Text = troubleshootingInformation.GamesInstallationPath;
            LogFilesPathTxt.Text = playniteAPI.Paths.ConfigurationPath;
            ReportBugHyp.NavigateUri = new Uri($"https://github.com/hawkeye116477/playnite-comet-plugin/issues/new?assignees=&labels=bug&projects=&template=bugs.yml&cometV={troubleshootingInformation.PluginVersion}&playniteV={troubleshootingInformation.PlayniteVersion}&launcherV={troubleshootingInformation.CometVersion}");
        }


        private async void CheckForUpdatesBtn_Click(object sender, RoutedEventArgs e)
        {
            var versionInfoContent = await Comet.GetVersionInfoContent();
            if (versionInfoContent.Tag_name != null)
            {
                var newVersion = versionInfoContent.Tag_name.Replace("v", "");
                if (troubleshootingInformation.CometVersion != newVersion)
                {
                    var options = new List<MessageBoxOption>
                    {
                        new MessageBoxOption(ResourceProvider.GetString(LOC.GogOssViewChangelog)),
                        new MessageBoxOption(ResourceProvider.GetString(LOC.GogOss3P_PlayniteOKLabel)),
                    };
                    var result = playniteAPI.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString(LOC.GogOssNewVersionAvailable), "Comet Launcher", newVersion), ResourceProvider.GetString(LOC.GogOss3P_PlayniteUpdaterWindowTitle), MessageBoxImage.Information, options);
                    if (result == options[0])
                    {
                        var changelogURL = $"https://github.com/imLinguin/comet/releases/tag/v{newVersion}";
                        Playnite.Commands.GlobalCommands.NavigateUrl(changelogURL);
                    }
                }
                else
                {
                    playniteAPI.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.GogOssNoUpdatesAvailable));
                }
            }
            else
            {
                playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.GogOss3P_PlayniteUpdateCheckFailMessage), "Comet Launcher");
            }
        }

        private void CopyRawDataBtn_Click(object sender, RoutedEventArgs e)
        {
            var troubleshootingJSON = Serialization.ToJson(troubleshootingInformation, true);
            Clipboard.SetText(troubleshootingJSON);
        }

        private void OpenGamesInstallationPathBtn_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(troubleshootingInformation.GamesInstallationPath))
            {
                ProcessStarter.StartProcess("explorer.exe", troubleshootingInformation.GamesInstallationPath);
            }
            else
            {
                playniteAPI.Dialogs.ShowErrorMessage(LOC.GogOssPathNotExistsError);
            }
        }

        private void OpenLauncherBinaryBtn_Click(object sender, RoutedEventArgs e)
        {
            Comet.StartClient();
        }

        private void OpenLogFilesPathBtn_Click(object sender, RoutedEventArgs e)
        {
            ProcessStarter.StartProcess("explorer.exe", playniteAPI.Paths.ConfigurationPath);
        }

        private async void UpdateAuthStatus()
        {
            if (GogOssLibrary.GetSettings().ConnectAccount)
            {
                LoginBtn.IsEnabled = false;
                AuthStatusTB.Text = ResourceProvider.GetString(LOC.GogOss3P_GOGLoginChecking);
                var clientApi = new GogAccountClient();
                var userLoggedIn = await clientApi.GetIsUserLoggedIn();
                if (userLoggedIn)
                {
                    var accountInfo = await clientApi.GetAccountInfo();
                    AuthStatusTB.Text = ResourceProvider.GetString(LOC.GogOssSignedInAs).Format(accountInfo.username);
                    LoginBtn.Content = ResourceProvider.GetString(LOC.GogOssSignOut);
                    LoginBtn.IsChecked = true;
                }
                else
                {
                    AuthStatusTB.Text = ResourceProvider.GetString(LOC.GogOss3P_GOGNotLoggedIn);
                    LoginBtn.Content = ResourceProvider.GetString(LOC.GogOss3P_GOGAuthenticateLabel);
                    LoginBtn.IsChecked = false;
                }
                LoginBtn.IsEnabled = true;
            }
            else
            {
                AuthStatusTB.Text = ResourceProvider.GetString(LOC.GogOss3P_GOGNotLoggedIn);
                LoginBtn.IsEnabled = true;
            }
        }

        private async void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            using var view = playniteAPI.WebViews.CreateView(new WebViewSettings
            {
                WindowWidth = 580,
                WindowHeight = 700,
            });
            var clientApi = new GogAccountClient(view);
            var userLoggedIn = LoginBtn.IsChecked;
            if (!userLoggedIn == false)
            {
                try
                {
                    await clientApi.Login();
                }
                catch (Exception ex) when (!Debugger.IsAttached)
                {
                    playniteAPI.Dialogs.ShowErrorMessage(playniteAPI.Resources.GetString(LOC.GogOss3P_GOGNotLoggedInError), "");
                    logger.Error(ex, "Failed to authenticate user.");
                }
                UpdateAuthStatus();
            }
            else
            {
                var answer = playniteAPI.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.GogOssSignOutConfirm), LOC.GogOssSignOut, MessageBoxButton.YesNo);
                if (answer == MessageBoxResult.Yes)
                {
                    view.DeleteDomainCookies(".gog.com");
                    File.Delete(GogOss.TokensPath);
                    UpdateAuthStatus();
                }
                else
                {
                    LoginBtn.IsChecked = true;
                }
            }
        }

        private void CheckGOGConnectAccount_Checked(object sender, RoutedEventArgs e)
        {
            UpdateAuthStatus();
        }
    }
}
