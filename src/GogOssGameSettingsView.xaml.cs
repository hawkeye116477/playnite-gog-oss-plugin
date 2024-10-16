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
                newGameSettings.StartupArguments = StartupArgumentsTxt.Text.Split().ToList();
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
            var gameSettingsFile = Path.Combine(GogOssLibrary.Instance.GetPluginUserDataPath(), "GamesSettings", $"{GameID}.json");
            if (newGameSettings.GetType().GetProperties().Any(p => p.GetValue(newGameSettings) != null) || File.Exists(gameSettingsFile))
            {
                Helpers.SaveJsonSettingsToFile(newGameSettings, GameID, "GamesSettings");
            }
            Window.GetWindow(this).Close();
        }

        private void SyncSavesBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = playniteAPI.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.GogOssCloudSaveConfirm), ResourceProvider.GetString(LOC.GogOssCloudSaves), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                CloudSyncAction selectedCloudSyncAction = (CloudSyncAction)ManualSyncSavesCBo.SelectedValue;
                if (SelectedSavePathTxt.Text != "")
                {
                    GogOssCloud.SyncGameSaves(Game.Name, GameID, selectedCloudSyncAction, true, true, SelectedSavePathTxt.Text);
                }
                else
                {
                    GogOssCloud.SyncGameSaves(Game.Name, GameID, selectedCloudSyncAction, true);
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
            var saveLocations = GogOssCloud.CalculateGameSavesPath(GameID);
            SelectedSavePathTxt.Text = saveLocations[0].location;
        }

        private void GameSettingsViewUC_Loaded(object sender, RoutedEventArgs e)
        {
            var globalSettings = GogOssLibrary.GetSettings();
            EnableCometSupportChk.IsEnabled = globalSettings.EnableCometSupport;
            if (globalSettings.GamesUpdatePolicy == UpdatePolicy.Never)
            {
                DisableGameUpdateCheckingChk.IsChecked = true;
            }
            AutoSyncSavesChk.IsChecked = globalSettings.SyncGameSaves;
            AutoSyncPlaytimeChk.IsChecked = globalSettings.SyncPlaytime;
            gameSettings = LoadGameSettings(GameID);
            if (gameSettings.EnableCometSupport != null)
            {
                EnableCometSupportChk.IsChecked = gameSettings.EnableCometSupport;
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

            var remoteConfig = GogOssCloud.GetCloudConfig(GameID);
            if (!remoteConfig.content.Windows.cloudStorage.enabled)
            {
                CloudSavesSP.Visibility = Visibility.Collapsed;
                CloudSavesNotSupportedTB.Visibility = Visibility.Visible;
            }

            var cloudSyncActions = new Dictionary<CloudSyncAction, string>
            {
                { CloudSyncAction.Download, ResourceProvider.GetString(LOC.GogOssDownload) },
                { CloudSyncAction.Upload, ResourceProvider.GetString(LOC.GogOssUpload) },
                { CloudSyncAction.ForceDownload, ResourceProvider.GetString(LOC.GogOssForceDownload) },
                { CloudSyncAction.ForceUpload, ResourceProvider.GetString(LOC.GogOssForceUpload) }
            };
            ManualSyncSavesCBo.ItemsSource = cloudSyncActions;
            ManualSyncSavesCBo.SelectedIndex = 0;
        }

        private void ChooseAlternativeExeBtn_Click(object sender, RoutedEventArgs e)
        {
            var file = playniteAPI.Dialogs.SelectFile($"{ResourceProvider.GetString(LOC.GogOss3P_PlayniteExecutableTitle)}|*.exe");
            if (file != "")
            {
                SelectedAlternativeExeTxt.Text = file;
            }
        }
    }
}
