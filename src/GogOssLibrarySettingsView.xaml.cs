using CommonPlugin;
using CommonPlugin.Enums;
using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Linguini.Shared.Types.Bundle;
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
            var file = playniteAPI.Dialogs.SelectFile($"{LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteExecutableTitle)}|*.exe");
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
                MigrateRevertGogBtn.IsEnabled = true;
            }

            if (GalaxyOverlay.IsInstalled)
            {
                OverlayInstallBtn.Visibility = Visibility.Collapsed;
            }

            var downloadCompleteActions = new Dictionary<DownloadCompleteAction, string>
            {
                { DownloadCompleteAction.Nothing, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteDoNothing) },
                { DownloadCompleteAction.ShutDown, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteMenuShutdownSystem) },
                { DownloadCompleteAction.Reboot, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteMenuRestartSystem) },
                { DownloadCompleteAction.Hibernate, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteMenuHibernateSystem) },
                { DownloadCompleteAction.Sleep, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteMenuSuspendSystem) },
            };
            AfterDownloadCompleteCBo.ItemsSource = downloadCompleteActions;

            var updatePolicyOptions = new Dictionary<UpdatePolicy, string>
            {
                { UpdatePolicy.PlayniteLaunch, LocalizationManager.Instance.GetString(LOC.CommonCheckUpdatesEveryPlayniteStartup) },
                { UpdatePolicy.Day, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteOptionOnceADay) },
                { UpdatePolicy.Week, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteOptionOnceAWeek) },
                { UpdatePolicy.Month, LocalizationManager.Instance.GetString(LOC.CommonOnceAMonth) },
                { UpdatePolicy.ThreeMonths, LocalizationManager.Instance.GetString(LOC.CommonOnceEvery3Months) },
                { UpdatePolicy.SixMonths, LocalizationManager.Instance.GetString(LOC.CommonOnceEvery6Months) },
                { UpdatePolicy.Never, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteOptionOnlyManually) }
            };
            GamesUpdatesCBo.ItemsSource = updatePolicyOptions;

            var launcherUpdatePolicyOptions = new Dictionary<UpdatePolicy, string>
            {
                { UpdatePolicy.PlayniteLaunch, LocalizationManager.Instance.GetString(LOC.CommonCheckUpdatesEveryPlayniteStartup) },
                { UpdatePolicy.Day, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteOptionOnceADay) },
                { UpdatePolicy.Week, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteOptionOnceAWeek) },
                { UpdatePolicy.Month, LocalizationManager.Instance.GetString(LOC.CommonOnceAMonth) },
                { UpdatePolicy.ThreeMonths, LocalizationManager.Instance.GetString(LOC.CommonOnceEvery3Months) },
                { UpdatePolicy.SixMonths, LocalizationManager.Instance.GetString(LOC.CommonOnceEvery6Months) },
                { UpdatePolicy.Never, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteOptionOnlyManually) }
            };
            LauncherUpdatesCBo.ItemsSource = launcherUpdatePolicyOptions;

            var autoClearOptions = new Dictionary<ClearCacheTime, string>
            {
                { ClearCacheTime.Day, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteOptionOnceADay) },
                { ClearCacheTime.Week, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteOptionOnceAWeek) },
                { ClearCacheTime.Month, LocalizationManager.Instance.GetString(LOC.CommonOnceAMonth) },
                { ClearCacheTime.ThreeMonths, LocalizationManager.Instance.GetString(LOC.CommonOnceEvery3Months) },
                { ClearCacheTime.SixMonths, LocalizationManager.Instance.GetString(LOC.CommonOnceEvery6Months) },
                { ClearCacheTime.Never, LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteSettingsPlaytimeImportModeNever) }
            };
            AutoClearCacheCBo.ItemsSource = autoClearOptions;
            AutoRemoveCompletedDownloadsCBo.ItemsSource = autoClearOptions;

            var preferredCdnActions = PreferredCdn.GetCdnDict();
            PreferredCdnCBo.ItemsSource = preferredCdnActions;

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
                var cometFluentArgs = new Dictionary<string, IFluentType> { ["launcherName"] = (FluentString)"Comet" };
                CometVersionTxt.Text = LocalizationManager.Instance.GetString(LOC.CommonLauncherNotInstalled, cometFluentArgs);
                CometBinaryTxt.Text = LocalizationManager.Instance.GetString(LOC.CommonLauncherNotInstalled, cometFluentArgs);
                CheckForCometUpdatesBtn.IsEnabled = false;
                OpenCometBinaryBtn.IsEnabled = false;
            }

            if (!Xdelta.IsInstalled)
            {
                var xdeltaFluentArgs = new Dictionary<string, IFluentType> { ["launcherName"] = (FluentString)"Xdelta" };
                XdeltaBinaryTxt.Text = LocalizationManager.Instance.GetString(LOC.CommonLauncherNotInstalled, xdeltaFluentArgs);
            }
            else
            {
                XdeltaBinaryTxt.Text = troubleshootingInformation.XdeltaBinary;
            }

            troubleshootingInformation.GogdlVersion = "Not%20needed";
            PlayniteVersionTxt.Text = GogOssTroubleshootingInformation.PlayniteVersion;
            PluginVersionTxt.Text = troubleshootingInformation.PluginVersion;
            GamesInstallationPathTxt.Text = troubleshootingInformation.GamesInstallationPath;
            LogFilesPathTxt.Text = playniteAPI.Paths.ConfigurationPath;
            ReportBugHyp.NavigateUri = new Uri($"https://github.com/hawkeye116477/playnite-gog-oss-plugin/issues/new?assignees=&labels=bug&projects=&template=bugs.yml&pluginV={troubleshootingInformation.PluginVersion}&playniteV={GogOssTroubleshootingInformation.PlayniteVersion}&cometV={troubleshootingInformation.CometVersion}&gogdlV={troubleshootingInformation.GogdlVersion}");

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
                        new MessageBoxOption(LocalizationManager.Instance.GetString(LOC.CommonViewChangelog)),
                        new MessageBoxOption(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteOkLabel)),
                    };
                    var result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonNewVersionAvailable, new Dictionary<string, IFluentType> { ["appName"] = (FluentString)"Comet", ["appVersion"] = (FluentString)newVersion }), LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUpdaterWindowTitle), MessageBoxImage.Information, options);
                    if (result == options[0])
                    {
                        var changelogURL = $"https://github.com/imLinguin/comet/releases/tag/v{newVersion}";
                        Playnite.Commands.GlobalCommands.NavigateUrl(changelogURL);
                    }
                }
                else
                {
                    playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonNoUpdatesAvailable));
                }
            }
            else
            {
                playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUpdateCheckFailMessage), "Comet");
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
                playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.CommonPathNotExistsError));
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
                AuthStatusTB.Text = LocalizationManager.Instance.GetString(LOC.ThirdPartyGogLoginChecking);
                using (var view = playniteAPI.WebViews.CreateOffscreenView())
                {
                    var clientApi = new GogAccountClient();
                    var userLoggedIn = await clientApi.GetIsUserLoggedIn();
                    if (userLoggedIn)
                    {
                        var accountInfo = await clientApi.GetAccountInfo();
                        AuthStatusTB.Text = LocalizationManager.Instance.GetString(LOC.CommonSignedInAs, new Dictionary<string, IFluentType> { ["userName"] = (FluentString)accountInfo.username });
                        LoginBtn.Content = LocalizationManager.Instance.GetString(LOC.CommonSignOut);
                        LoginBtn.IsChecked = true;
                    }
                    else
                    {
                        AuthStatusTB.Text = LocalizationManager.Instance.GetString(LOC.ThirdPartyGogNotLoggedIn);
                        LoginBtn.Content = LocalizationManager.Instance.GetString(LOC.ThirdPartyGogAuthenticateLabel);
                        LoginBtn.IsChecked = false;
                    }
                    LoginBtn.IsEnabled = true;
                }
            }
            else
            {
                AuthStatusTB.Text = LocalizationManager.Instance.GetString(LOC.ThirdPartyGogNotLoggedIn);
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
                    playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.ThirdPartyGogNotLoggedInError), "");
                    logger.Error(ex, "Failed to authenticate user.");
                }
                UpdateAuthStatus();
            }
            else
            {
                var answer = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonSignOutConfirm), LocalizationManager.Instance.GetString(LOC.CommonSignOut), MessageBoxButton.YesNo);
                if (answer == MessageBoxResult.Yes)
                {
                    view.DeleteDomainCookies(".gog.com");
                    if (File.Exists(GogOss.TokensPath))
                    {
                        File.Delete(GogOss.TokensPath);
                    }
                    if (File.Exists(GogOss.EncryptedTokensPath))
                    {
                        File.Delete(GogOss.EncryptedTokensPath);
                    }
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
            var result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonClearCacheConfirm), LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteSettingsClearCacheTitle), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                GogOss.ClearCache();
            }
        }

        private void SyncGameSavesChk_Click(object sender, RoutedEventArgs e)
        {
            if (SyncGameSavesChk.IsChecked == true)
            {
                playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonSyncGameSavesWarn), "", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void MigrateGogBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonMigrationConfirm), LocalizationManager.Instance.GetString(LOC.CommonMigrateGamesOriginal), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                return;
            }
            GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(LocalizationManager.Instance.GetString(LOC.CommonMigratingGamesOriginal), false) { IsIndeterminate = false };
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
                            playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonMigrationCompleted), LocalizationManager.Instance.GetString(LOC.CommonMigrateGamesOriginal), MessageBoxButton.OK, MessageBoxImage.Information);
                            logger.Info("Successfully migrated " + migratedGames.Count + " game(s) from GOG to GOG OSS.");
                        }
                        if (migratedGames.Count == 0 && notImportedGames.Count == 0)
                        {
                            playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.CommonMigrationNoGames));
                        }
                    }
                    else
                    {
                        a.ProgressMaxValue = 1;
                        a.CurrentProgressValue = 1;
                        playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.CommonMigrationNoGames));
                    }
                }
            }, globalProgressOptions);
        }

        private void ChooseXdeltaBtn_Click(object sender, RoutedEventArgs e)
        {
            var file = playniteAPI.Dialogs.SelectFile($"{LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteExecutableTitle)}|xdelta3*.exe");
            if (file != "")
            {
                SelectedXdeltaPathTxt.Text = file;
            }
        }

        private void OpenXdeltaBinaryBtn_Click(object sender, RoutedEventArgs e)
        {
            Xdelta.StartClient();
        }

        private void MigrateRevertGogBtn_Click(object sender, RoutedEventArgs e)
        {
            var commonFluentArgs = new Dictionary<string, IFluentType>
            {
                { "pluginShortName", (FluentString)"GOG" },
                { "originalPluginShortName", (FluentString)"GOG OSS" },
            };

            var result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonMigrationConfirm, commonFluentArgs), LocalizationManager.Instance.GetString(LOC.CommonRevertMigrateGames), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                return;
            }
            GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(LocalizationManager.Instance.GetString(LOC.CommonRevertMigratingGames), false) { IsIndeterminate = false };
            playniteAPI.Dialogs.ActivateGlobalProgress((a) =>
            {
                using (playniteAPI.Database.BufferedUpdate())
                {
                    var gamesToMigrate = playniteAPI.Database.Games.Where(i => i.PluginId == GogOssLibrary.Instance.Id).ToList();
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
                                game.PluginId = Guid.Parse("AEBE8B7C-6DC3-4A66-AF31-E7375C6B5E9E");
                                playniteAPI.Database.Games.Update(game);
                                migratedGames.Add(game.GameId);
                                a.CurrentProgressValue = iterator;
                            }
                        }
                        a.CurrentProgressValue = gamesToMigrate.Count() + 1;
                        if (migratedGames.Count > 0)
                        {
                            playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonMigrationCompleted), LocalizationManager.Instance.GetString(LOC.CommonRevertMigrateGames), MessageBoxButton.OK, MessageBoxImage.Information);
                            logger.Info("Successfully migrated " + migratedGames.Count + " game(s) from GOG OSS to GOG.");
                        }
                        if (migratedGames.Count == 0 && notImportedGames.Count == 0)
                        {
                            playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.CommonMigrationNoGames));
                        }
                    }
                    else
                    {
                        a.ProgressMaxValue = 1;
                        a.CurrentProgressValue = 1;
                        playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.CommonMigrationNoGames));
                    }
                }
            }, globalProgressOptions);
        }

        private void OverlayInstallBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!Comet.IsInstalled)
            {
                var playniteAPI = API.Instance;
                var options = new List<MessageBoxOption>
                {
                    new MessageBoxOption(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteInstallGame)),
                    new MessageBoxOption(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteOkLabel)),
                };
                var cometFluentArgs = new Dictionary<string, IFluentType> { ["launcherName"] = (FluentString)"Comet" };
                var result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonLauncherNotInstalled, cometFluentArgs), "GOG OSS library integration", MessageBoxImage.Information, options);
                if (result == options[0])
                {
                    Playnite.Commands.GlobalCommands.NavigateUrl("https://github.com/hawkeye116477/playnite-gog-oss-plugin/wiki/Installation-of-needed-tools#comet-needed-for-leaderboards-multiplayer-and-achievements");
                }
            }

            var window = playniteAPI.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = false
            });
            var overlayFluentArgs = new Dictionary<string, IFluentType> { ["overlayName"] = (FluentString)"Galaxy" };
            var overlayFullName = LocalizationManager.Instance.GetString(LOC.CommonOverlay, overlayFluentArgs);
            window.Title = overlayFullName;
            var installProperties = new DownloadProperties { downloadAction = DownloadAction.Install, os = "windows" };
            var installData = new DownloadManagerData.Download { name = overlayFullName, gameID = "galaxy-overlay", downloadProperties = installProperties, downloadItemType = DownloadItemType.Overlay };
            var installDataList = new List<DownloadManagerData.Download>
            {
                installData
            };
            window.DataContext = installDataList;
            window.Content = new GogOssGameInstallerView();
            window.Owner = playniteAPI.Dialogs.GetCurrentAppWindow();
            window.SizeToContent = SizeToContent.Height;
            window.Width = 600;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ShowDialog();
        }
    }
}
