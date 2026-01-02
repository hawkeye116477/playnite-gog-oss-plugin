using CommonPlugin;
using CommonPlugin.Enums;
using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Interaction logic for GogOssGameSettingsView.xaml
    /// </summary>
    public partial class GogOssGameSettingsView : UserControl
    {
        private Game Game => DataContext as Game;
        public string GameID => Game.GameId;
        private IPlayniteAPI playniteAPI = API.Instance;
        public GameSettings gameSettings;
        public GogOssCloud gogOssCloud = new GogOssCloud();

        public GogOssGameSettingsView()
        {
            InitializeComponent();
        }

        public static GameSettings LoadGameSettings(string gameID)
        {
            var gameSettings = new GameSettings();
            var gameSettingsFile = Path.Combine(GogOssLibrary.Instance.GetPluginUserDataPath(), "GamesSettings", $"{gameID}.json");
            if (File.Exists(gameSettingsFile))
            {
                if (Serialization.TryFromJson(FileSystem.ReadFileAsStringSafe(gameSettingsFile), out GameSettings savedGameSettings))
                {
                    if (savedGameSettings != null)
                    {
                        gameSettings = savedGameSettings;
                    }
                }
            }
            return gameSettings;
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            var globalSettings = GogOssLibrary.GetSettings();
            var newGameSettings = new GameSettings();
            if (EnableCometSupportChk.IsChecked != globalSettings.EnableCometSupport)
            {
                newGameSettings.EnableCometSupport = EnableCometSupportChk.IsChecked;
            }
            bool globalDisableUpdates = false;
            if (globalSettings.GamesUpdatePolicy == UpdatePolicy.Never)
            {
                globalDisableUpdates = true;
            }
            if (DisableGameUpdateCheckingChk.IsChecked != globalDisableUpdates)
            {
                newGameSettings.DisableGameVersionCheck = DisableGameUpdateCheckingChk.IsChecked;
            }
            if (StartupArgumentsTxt.Text != "")
            {
                newGameSettings.StartupArguments = StartupArgumentsTxt.Text.SplitOutsideQuotes(' ').ToList();
            }
            if (SelectedAlternativeExeTxt.Text != "")
            {
                newGameSettings.OverrideExe = SelectedAlternativeExeTxt.Text;
            }
            if (AutoSyncSavesChk.IsChecked != globalSettings.SyncGameSaves)
            {
                newGameSettings.AutoSyncSaves = AutoSyncSavesChk.IsChecked;
            }
            if (SelectedSavePathTxt.Text != "")
            {
                newGameSettings.CloudSaveFolder = SelectedSavePathTxt.Text;
            }
            if (AutoSyncPlaytimeChk.IsChecked != globalSettings.SyncPlaytime)
            {
                newGameSettings.AutoSyncPlaytime = AutoSyncPlaytimeChk.IsChecked;
            }
            if (EnableOverlayChk.IsChecked != globalSettings.EnableOverlay)
            {
                newGameSettings.EnableOverlay = EnableOverlayChk.IsChecked;
            }
            var gameSettingsFile = Path.Combine(GogOssLibrary.Instance.GetPluginUserDataPath(), "GamesSettings", $"{GameID}.json");
            if (newGameSettings.GetType().GetProperties().Any(p => p.GetValue(newGameSettings) != null) || File.Exists(gameSettingsFile))
            {
                var commonHelpers = GogOssLibrary.Instance.commonHelpers;
                commonHelpers.SaveJsonSettingsToFile(newGameSettings, "GamesSettings", GameID, true);
            }
            Window.GetWindow(this).Close();
        }

        private void SyncSavesBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonCloudSaveConfirm), LocalizationManager.Instance.GetString(LOC.CommonCloudSaves), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                bool forceCloudSync = (bool)ForceCloudActionChk.IsChecked;
                CloudSyncAction selectedCloudSyncAction = (CloudSyncAction)ManualSyncSavesCBo.SelectedValue;
                if (SelectedSavePathTxt.Text != "")
                {
                    gogOssCloud.SyncGameSaves(Game, selectedCloudSyncAction, forceCloudSync, true, true, SelectedSavePathTxt.Text);
                }
                else
                {
                    gogOssCloud.SyncGameSaves(Game, selectedCloudSyncAction, forceCloudSync, true);
                }
            }
        }

        private void ChooseSavePathBtn_Click(object sender, RoutedEventArgs e)
        {
            var selectedCloudPath = playniteAPI.Dialogs.SelectFolder();
            if (selectedCloudPath != "")
            {
                SelectedSavePathTxt.Text = selectedCloudPath;
            }
        }

        private void CalculatePathBtn_Click(object sender, RoutedEventArgs e)
        {
            var saveLocations = gogOssCloud.CalculateGameSavesPath(Game);
            SelectedSavePathTxt.Text = saveLocations[0].location;
        }

        private void GameSettingsViewUC_Loaded(object sender, RoutedEventArgs e)
        {
            CommonHelpers.SetControlBackground(this);
            var globalSettings = GogOssLibrary.GetSettings();
            EnableCometSupportChk.IsEnabled = Comet.IsInstalled;
            if (globalSettings.GamesUpdatePolicy == UpdatePolicy.Never)
            {
                DisableGameUpdateCheckingChk.IsChecked = true;
            }

            AutoSyncSavesChk.IsChecked = globalSettings.SyncGameSaves;
            AutoSyncPlaytimeChk.IsChecked = globalSettings.SyncPlaytime;
            EnableCometSupportChk.IsChecked = globalSettings.EnableCometSupport;
            EnableOverlayChk.IsChecked = globalSettings.EnableOverlay;
            gameSettings = LoadGameSettings(GameID);
            if (gameSettings.EnableCometSupport != null)
            {
                EnableCometSupportChk.IsChecked = gameSettings.EnableCometSupport;
            }
            if (gameSettings.EnableOverlay != null)
            {
                EnableOverlayChk.IsChecked = gameSettings.EnableOverlay;
            }
            if (gameSettings.DisableGameVersionCheck != null)
            {
                DisableGameUpdateCheckingChk.IsChecked = gameSettings.DisableGameVersionCheck;
            }
            if (gameSettings.StartupArguments != null)
            {
                StartupArgumentsTxt.Text = string.Join(" ", gameSettings.StartupArguments);
            }
            if (gameSettings.OverrideExe != null)
            {
                SelectedAlternativeExeTxt.Text = gameSettings.OverrideExe;
            }
            if (gameSettings.AutoSyncSaves != null)
            {
                AutoSyncSavesChk.IsChecked = gameSettings.AutoSyncSaves;
            }

            if (!gameSettings.CloudSaveFolder.IsNullOrEmpty())
            {
                SelectedSavePathTxt.Text = gameSettings.CloudSaveFolder;
            }
            if (!gameSettings.AutoSyncPlaytime != null)
            {
                AutoSyncPlaytimeChk.IsChecked = gameSettings.AutoSyncPlaytime;
            }
            if (playniteAPI.ApplicationSettings.PlaytimeImportMode == PlaytimeImportMode.Never)
            {
                AutoSyncPlaytimeChk.IsEnabled = false;
            }

            var remoteConfig = gogOssCloud.GetCloudConfig(Game);
            if (!remoteConfig.content.Windows.cloudStorage.enabled)
            {
                CloudSavesSP.Visibility = Visibility.Collapsed;
                CloudSavesNotSupportedTB.Visibility = Visibility.Visible;
            }

            var cloudSyncActions = new Dictionary<CloudSyncAction, string>
            {
                { CloudSyncAction.Download, LocalizationManager.Instance.GetString(LOC.CommonDownload) },
                { CloudSyncAction.Upload, LocalizationManager.Instance.GetString(LOC.CommonUpload) },
            };
            ManualSyncSavesCBo.ItemsSource = cloudSyncActions;
            ManualSyncSavesCBo.SelectedIndex = 0;
        }

        private void ChooseAlternativeExeBtn_Click(object sender, RoutedEventArgs e)
        {
            var file = playniteAPI.Dialogs.SelectFile($"{LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteExecutableTitle)}|*.exe");
            if (file != "")
            {
                SelectedAlternativeExeTxt.Text = file;
            }
        }
    }
}
