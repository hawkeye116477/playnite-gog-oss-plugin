using CommonPlugin;
using CommonPlugin.Enums;
using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Services;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            MaxWorkersNI.MaxValue = CommonHelpers.CpuThreadsNumber;
        }

        private void ChooseGamePathBtn_Click(object sender, RoutedEventArgs e)
        {
            var path = playniteAPI.Dialogs.SelectFolder();
            if (path != "")
            {
                SelectedGamePathTxt.Text = path;
            }
        }

        private void ChooseCometBtn_Click(object sender, RoutedEventArgs e)
        {
            var file = playniteAPI.Dialogs.SelectFile($"{ResourceProvider.GetString(LOC.GogOss3P_PlayniteExecutableTitle)}|*.exe");
            if (file != "")
            {
                SelectedCometPathTxt.Text = file;
            }
        }

        private async void GogOssSettingsUC_Loaded(object sender, RoutedEventArgs e)
        {
            var installedAddons = playniteAPI.Addons.Addons;
            if (installedAddons.Contains("GogLibrary_Builtin"))
            {
                MigrateGogBtn.IsEnabled = true;
            }

            var downloadCompleteActions = new Dictionary<DownloadCompleteAction, string>
            {
                { DownloadCompleteAction.Nothing, ResourceProvider.GetString(LOC.GogOss3P_PlayniteDoNothing) },
                { DownloadCompleteAction.ShutDown, ResourceProvider.GetString(LOC.GogOss3P_PlayniteMenuShutdownSystem) },
                { DownloadCompleteAction.Reboot, ResourceProvider.GetString(LOC.GogOss3P_PlayniteMenuRestartSystem) },
                { DownloadCompleteAction.Hibernate, ResourceProvider.GetString(LOC.GogOss3P_PlayniteMenuHibernateSystem) },
                { DownloadCompleteAction.Sleep, ResourceProvider.GetString(LOC.GogOss3P_PlayniteMenuSuspendSystem) },
            };
            AfterDownloadCompleteCBo.ItemsSource = downloadCompleteActions;

            var updatePolicyOptions = new Dictionary<UpdatePolicy, string>
            {
                { UpdatePolicy.PlayniteLaunch, ResourceProvider.GetString(LOC.GogOssCheckUpdatesEveryPlayniteStartup) },
                { UpdatePolicy.Day, ResourceProvider.GetString(LOC.GogOss3P_PlayniteOptionOnceADay) },
                { UpdatePolicy.Week, ResourceProvider.GetString(LOC.GogOss3P_PlayniteOptionOnceAWeek) },
                { UpdatePolicy.Month, ResourceProvider.GetString(LOC.GogOssOnceAMonth) },
                { UpdatePolicy.ThreeMonths, ResourceProvider.GetString(LOC.GogOssOnceEvery3Months) },
                { UpdatePolicy.SixMonths, ResourceProvider.GetString(LOC.GogOssOnceEvery6Months) },
                { UpdatePolicy.Never, ResourceProvider.GetString(LOC.GogOss3P_PlayniteOptionOnlyManually) }
            };
            GamesUpdatesCBo.ItemsSource = updatePolicyOptions;

            var launcherUpdatePolicyOptions = new Dictionary<UpdatePolicy, string>
            {
                { UpdatePolicy.PlayniteLaunch, ResourceProvider.GetString(LOC.GogOssCheckUpdatesEveryPlayniteStartup) },
                { UpdatePolicy.Day, ResourceProvider.GetString(LOC.GogOss3P_PlayniteOptionOnceADay) },
                { UpdatePolicy.Week, ResourceProvider.GetString(LOC.GogOss3P_PlayniteOptionOnceAWeek) },
                { UpdatePolicy.Month, ResourceProvider.GetString(LOC.GogOssOnceAMonth) },
                { UpdatePolicy.ThreeMonths, ResourceProvider.GetString(LOC.GogOssOnceEvery3Months) },
                { UpdatePolicy.SixMonths, ResourceProvider.GetString(LOC.GogOssOnceEvery6Months) },
                { UpdatePolicy.Never, ResourceProvider.GetString(LOC.GogOss3P_PlayniteOptionOnlyManually) }
            };
            LauncherUpdatesCBo.ItemsSource = launcherUpdatePolicyOptions;

            var autoClearOptions = new Dictionary<ClearCacheTime, string>
            {
                { ClearCacheTime.Day, ResourceProvider.GetString(LOC.GogOss3P_PlayniteOptionOnceADay) },
                { ClearCacheTime.Week, ResourceProvider.GetString(LOC.GogOss3P_PlayniteOptionOnceAWeek) },
                { ClearCacheTime.Month, ResourceProvider.GetString(LOC.GogOssOnceAMonth) },
                { ClearCacheTime.ThreeMonths, ResourceProvider.GetString(LOC.GogOssOnceEvery3Months) },
                { ClearCacheTime.SixMonths, ResourceProvider.GetString(LOC.GogOssOnceEvery6Months) },
                { ClearCacheTime.Never, ResourceProvider.GetString(LOC.GogOss3P_PlayniteSettingsPlaytimeImportModeNever) }
            };
            AutoClearCacheCBo.ItemsSource = autoClearOptions;

            troubleshootingInformation = new GogOssTroubleshootingInformation();
            if (Comet.IsInstalled)
            {
                var cometVersion = await Comet.GetCometVersion();
                if (!cometVersion.IsNullOrEmpty())
                {
                    troubleshootingInformation.CometVersion = cometVersion;
                    CometVersionTxt.Text = troubleshootingInformation.CometVersion;
                }
                CometBinaryTxt.Text = troubleshootingInformation.CometBinary;
            }
            else
            {
                troubleshootingInformation.CometVersion = "Not%20installed";
                CometVersionTxt.Text = ResourceProvider.GetString(LOC.GogOssLauncherNotInstalled).Replace("{AppName}", "Comet");
                CometBinaryTxt.Text = ResourceProvider.GetString(LOC.GogOssLauncherNotInstalled).Replace("{AppName}", "Comet");
                CheckForCometUpdatesBtn.IsEnabled = false;
                OpenCometBinaryBtn.IsEnabled = false;
            }
            if (Gogdl.IsInstalled)
            {
                var gogDlVersion = await Gogdl.GetVersion();
                if (!gogDlVersion.IsNullOrEmpty())
                {
                    troubleshootingInformation.GogdlVersion = gogDlVersion;
                    GogdlVersionTxt.Text = troubleshootingInformation.GogdlVersion;
                }
                GogdlBinaryTxt.Text = troubleshootingInformation.GogdlBinary;
            }
            else
            {
                troubleshootingInformation.GogdlVersion = "Not%20installed";
                GogdlVersionTxt.Text = ResourceProvider.GetString(LOC.GogOssLauncherNotInstalled).Replace("{AppName}", "Gogdl");
                GogdlBinaryTxt.Text = ResourceProvider.GetString(LOC.GogOssLauncherNotInstalled).Replace("{AppName}", "Gogdl");
                CheckForGogdlUpdatesBtn.IsEnabled = false;
                OpenGogdlBinaryBtn.IsEnabled = false;
            }
            PlayniteVersionTxt.Text = troubleshootingInformation.PlayniteVersion;
            PluginVersionTxt.Text = troubleshootingInformation.PluginVersion;
            GamesInstallationPathTxt.Text = troubleshootingInformation.GamesInstallationPath;
            LogFilesPathTxt.Text = playniteAPI.Paths.ConfigurationPath;
            ReportBugHyp.NavigateUri = new Uri($"https://github.com/hawkeye116477/playnite-gog-oss-plugin/issues/new?assignees=&labels=bug&projects=&template=bugs.yml&pluginV={troubleshootingInformation.PluginVersion}&playniteV={troubleshootingInformation.PlayniteVersion}&cometV={troubleshootingInformation.CometVersion}&gogdlV={troubleshootingInformation.GogdlVersion}");

            if (playniteAPI.ApplicationSettings.PlaytimeImportMode == PlaytimeImportMode.Never)
            {
                SyncPlaytimeChk.IsEnabled = false;
            }
        }


        private async void CheckForCometUpdatesBtn_Click(object sender, RoutedEventArgs e)
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
                    var result = playniteAPI.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString(LOC.GogOssNewVersionAvailable), "Comet", newVersion), ResourceProvider.GetString(LOC.GogOss3P_PlayniteUpdaterWindowTitle), MessageBoxImage.Information, options);
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
                playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.GogOss3P_PlayniteUpdateCheckFailMessage), "Comet");
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

        private void OpenCometBinaryBtn_Click(object sender, RoutedEventArgs e)
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

        private void GOGConnectAccountChk_Checked(object sender, RoutedEventArgs e)
        {
            UpdateAuthStatus();
        }

        private async void CheckForGogdlUpdatesBtn_Click(object sender, RoutedEventArgs e)
        {
            var versionInfoContent = await Gogdl.GetVersionInfoContent();
            if (versionInfoContent.Tag_name != null)
            {
                var newVersion = versionInfoContent.Tag_name.Replace("v", "");
                if (troubleshootingInformation.GogdlVersion != newVersion)
                {
                    var options = new List<MessageBoxOption>
                    {
                        new MessageBoxOption(ResourceProvider.GetString(LOC.GogOssViewChangelog)),
                        new MessageBoxOption(ResourceProvider.GetString(LOC.GogOss3P_PlayniteOKLabel)),
                    };
                    var result = playniteAPI.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString(LOC.GogOssNewVersionAvailable), "Gogdl", newVersion), ResourceProvider.GetString(LOC.GogOss3P_PlayniteUpdaterWindowTitle), MessageBoxImage.Information, options);
                    if (result == options[0])
                    {
                        var changelogURL = $"https://github.com/Heroic-Games-Launcher/heroic-gogdl/releases/tag/v{newVersion}";
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
                playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.GogOss3P_PlayniteUpdateCheckFailMessage), "Gogdl");
            }
        }

        private void OpenGogdlBinaryBtn_Click(object sender, RoutedEventArgs e)
        {
            ProcessStarter.StartProcess("cmd", $"/k {troubleshootingInformation.GogdlBinary} -h", Path.GetDirectoryName(troubleshootingInformation.GogdlBinary));
        }

        private void GamesUpdatesCBo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedValue = (KeyValuePair<UpdatePolicy, string>)GamesUpdatesCBo.SelectedItem;
            if (selectedValue.Key == UpdatePolicy.Never)
            {
                AutoUpdateGamesChk.IsEnabled = false;
            }
            else
            {
                AutoUpdateGamesChk.IsEnabled = true;
            }

        }

        private void ClearCacheBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = playniteAPI.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.GogOssClearCacheConfirm), ResourceProvider.GetString(LOC.GogOss3P_PlayniteSettingsClearCacheTitle), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                GogOss.ClearCache();
            }
        }

        private void SyncGameSavesChk_Click(object sender, RoutedEventArgs e)
        {
            if (SyncGameSavesChk.IsChecked == true)
            {
                playniteAPI.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.GogOssSyncGameSavesWarn), "", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MigrateGogBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = playniteAPI.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.GogOssMigrationConfirm), ResourceProvider.GetString(LOC.GogOssMigrateGamesGog), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                return;
            }
            GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(ResourceProvider.GetString(LOC.GogOssMigratingGamesGog), false) { IsIndeterminate = false };
            playniteAPI.Dialogs.ActivateGlobalProgress((a) =>
            {
                using (playniteAPI.Database.BufferedUpdate())
                {
                    var gamesToMigrate = playniteAPI.Database.Games.Where(i => i.PluginId == Guid.Parse("AEBE8B7C-6DC3-4A66-AF31-E7375C6B5E9E")).ToList();
                    var migratedGames = new List<string>();
                    var notImportedGames = new List<string>();
                    if (gamesToMigrate.Count > 0)
                    {
                        var iterator = 0;
                        a.ProgressMaxValue = gamesToMigrate.Count() + 1;
                        a.CurrentProgressValue = 0;
                        foreach (var game in gamesToMigrate.ToList())
                        {
                            iterator++;
                            var alreadyExists = playniteAPI.Database.Games.FirstOrDefault(i => i.GameId == game.GameId && i.PluginId == GogOssLibrary.Instance.Id);
                            if (alreadyExists == null)
                            {
                                game.PluginId = GogOssLibrary.Instance.Id;
                                playniteAPI.Database.Games.Update(game);
                                migratedGames.Add(game.GameId);
                                a.CurrentProgressValue = iterator;
                            }
                        }
                        a.CurrentProgressValue = gamesToMigrate.Count() + 1;
                        if (migratedGames.Count > 0)
                        {
                            playniteAPI.Dialogs.ShowMessage(LOC.GogOssMigrationCompleted, LOC.GogOssMigrateGamesGog, MessageBoxButton.OK, MessageBoxImage.Information);
                            logger.Info("Successfully migrated " + migratedGames.Count + " game(s) from GOG to GOG OSS.");
                        }
                        if (migratedGames.Count == 0 && notImportedGames.Count == 0)
                        {
                            playniteAPI.Dialogs.ShowErrorMessage(LOC.GogOssMigrationNoGames);
                        }
                    }
                    else
                    {
                        a.ProgressMaxValue = 1;
                        a.CurrentProgressValue = 1;
                        playniteAPI.Dialogs.ShowErrorMessage(LOC.GogOssMigrationNoGames);
                    }
                }
            }, globalProgressOptions);
        }

        private void ChooseGogdlBtn_Click(object sender, RoutedEventArgs e)
        {
            var file = playniteAPI.Dialogs.SelectFile($"{ResourceProvider.GetString(LOC.GogOss3P_PlayniteExecutableTitle)}|*.exe");
            if (file != "")
            {
                SelectedGogdlPathTxt.Text = file;
            }
        }
    }
}
