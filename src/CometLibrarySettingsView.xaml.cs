using CometLibrary.Models;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CometLibraryNS
{
    /// <summary>
    /// Interaction logic for CometLibrarySettingsView.xaml
    /// </summary>
    public partial class CometLibrarySettingsView : UserControl
    {
        private IPlayniteAPI playniteAPI = API.Instance;
        public CometTroubleshootingInformation troubleshootingInformation;

        public CometLibrarySettingsView()
        {
            InitializeComponent();
        }

        private void ChooseGamePathBtn_Click(object sender, RoutedEventArgs e)
        {

        }

        private void ChooseLauncherBtn_Click(object sender, RoutedEventArgs e)
        {
            var file = playniteAPI.Dialogs.SelectFile($"{ResourceProvider.GetString(LOC.Comet3P_PlayniteExecutableTitle)}|*.exe");
            if (file != "")
            {
                SelectedLauncherPathTxt.Text = file;
            }
        }

        private async void CometSettingsUC_Loaded(object sender, RoutedEventArgs e)
        {
            troubleshootingInformation = new CometTroubleshootingInformation();
            if (Comet.IsInstalled)
            {
                var launcherVersion = await Comet.GetLauncherVersion();
                if (!launcherVersion.IsNullOrEmpty())
                {
                    troubleshootingInformation.LauncherVersion = launcherVersion;
                    LauncherVersionTxt.Text = troubleshootingInformation.LauncherVersion;
                }
                LauncherBinaryTxt.Text = troubleshootingInformation.LauncherBinary;
            }
            else
            {
                troubleshootingInformation.LauncherVersion = "Not%20installed";
                LauncherVersionTxt.Text = ResourceProvider.GetString(LOC.CometLauncherNotInstalled);
                LauncherBinaryTxt.Text = ResourceProvider.GetString(LOC.CometLauncherNotInstalled);
                CheckForUpdatesBtn.IsEnabled = false;
                OpenLauncherBinaryBtn.IsEnabled = false;
            }
            PlayniteVersionTxt.Text = troubleshootingInformation.PlayniteVersion;
            PluginVersionTxt.Text = troubleshootingInformation.PluginVersion;
            GamesInstallationPathTxt.Text = troubleshootingInformation.GamesInstallationPath;
            LogFilesPathTxt.Text = playniteAPI.Paths.ConfigurationPath;
            ReportBugHyp.NavigateUri = new Uri($"https://github.com/hawkeye116477/playnite-comet-plugin/issues/new?assignees=&labels=bug&projects=&template=bugs.yml&cometV={troubleshootingInformation.PluginVersion}&playniteV={troubleshootingInformation.PlayniteVersion}&launcherV={troubleshootingInformation.LauncherVersion}");
        }


        private async void CheckForUpdatesBtn_Click(object sender, RoutedEventArgs e)
        {
            var versionInfoContent = await Comet.GetVersionInfoContent();
            if (versionInfoContent.Tag_name != null)
            {
                var newVersion = versionInfoContent.Tag_name.Replace("v", "");
                if (troubleshootingInformation.LauncherVersion != newVersion)
                {
                    var options = new List<MessageBoxOption>
                    {
                        new MessageBoxOption(ResourceProvider.GetString(LOC.CometViewChangelog)),
                        new MessageBoxOption(ResourceProvider.GetString(LOC.Comet3P_PlayniteOKLabel)),
                    };
                    var result = playniteAPI.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString(LOC.CometNewVersionAvailable), "Comet Launcher", newVersion), ResourceProvider.GetString(LOC.Comet3P_PlayniteUpdaterWindowTitle), MessageBoxImage.Information, options);
                    if (result == options[0])
                    {
                        var changelogURL = $"https://github.com/imLinguin/comet/releases/tag/v{newVersion}";
                        Playnite.Commands.GlobalCommands.NavigateUrl(changelogURL);
                    }
                }
                else
                {
                    playniteAPI.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.CometNoUpdatesAvailable));
                }
            }
            else
            {
                playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.Comet3P_PlayniteUpdateCheckFailMessage), "Comet Launcher");
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
                playniteAPI.Dialogs.ShowErrorMessage(LOC.CometPathNotExistsError);
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
    }
}
