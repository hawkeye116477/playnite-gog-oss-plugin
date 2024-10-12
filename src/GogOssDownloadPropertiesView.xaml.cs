using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Interaction logic for GogOssDownloadPropertiesView.xaml
    /// </summary>
    public partial class GogOssDownloadPropertiesView : UserControl
    {
        private DownloadManagerData.Download SelectedDownload => (DownloadManagerData.Download)DataContext;
        public DownloadManagerData.Rootobject downloadManagerData;
        public bool uncheckedByUser = true;
        private bool checkedByUser = true;
        private IPlayniteAPI playniteAPI = API.Instance;
        public Installed gameInfo;
        public string selectedBetaChannel;

        public GogOssDownloadPropertiesView()
        {
            InitializeComponent();
            LoadSavedData();
        }

        private DownloadManagerData.Rootobject LoadSavedData()
        {
            var downloadManager = GogOssLibrary.GetGogOssDownloadManager();
            downloadManagerData = downloadManager.downloadManagerData;
            return downloadManagerData;
        }

        private async void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            var downloadManager = GogOssLibrary.GetGogOssDownloadManager();
            var previouslySelected = downloadManager.DownloadsDG.SelectedIndex;
            var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == SelectedDownload.gameID);
            var installPath = SelectedGamePathTxt.Text;
            var playniteDirectoryVariable = ExpandableVariables.PlayniteDirectory.ToString();
            if (installPath.Contains(playniteDirectoryVariable))
            {
                installPath = installPath.Replace(playniteDirectoryVariable, playniteAPI.Paths.ApplicationPath);
            }
            if (!Helpers.IsDirectoryWritable(installPath))
            {
                return;
            }
            wantedItem.downloadProperties.installPath = installPath;
            wantedItem.downloadProperties.downloadAction = (DownloadAction)TaskCBo.SelectedValue;
            wantedItem.downloadProperties.maxWorkers = int.Parse(MaxWorkersNI.Value);

            if (wantedItem.status == DownloadStatus.Canceled)
            {
                wantedItem.downloadProperties.betaChannel = selectedBetaChannel;
                wantedItem.downloadProperties.buildId = gameInfo.build_id;
                wantedItem.downloadProperties.version = gameInfo.version;
                wantedItem.downloadProperties.language = gameInfo.language;
                wantedItem.downloadProperties.extraContent = gameInfo.installed_DLCs;
                var gameSize = await Gogdl.CalculateGameSize(SelectedDownload.gameID, gameInfo);
                wantedItem.downloadSizeNumber = gameSize.download_size;
                wantedItem.installSizeNumber = gameSize.disk_size;
            }

            for (int i = 0; i < downloadManager.downloadManagerData.downloads.Count; i++)
            {
                if (downloadManager.downloadManagerData.downloads[i].gameID == wantedItem.gameID)
                {
                    downloadManager.downloadManagerData.downloads[i] = wantedItem;
                    break;
                }
            }

            downloadManager.DownloadsDG.SelectedIndex = previouslySelected;
            downloadManager.downloadsChanged = true;
            Window.GetWindow(this).Close();
        }

        private async void ExtraContentLB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveBtn.IsEnabled = false;
            var selectedDlcs = ExtraContentLB.SelectedItems.Cast<GogDownloadGameInfo.Dlc>();
            gameInfo.installed_DLCs = new List<string>();
            foreach (var selectedDlc in selectedDlcs)
            {
                gameInfo.installed_DLCs.Add(selectedDlc.id);
            }
            if (AllOrNothingChk.IsChecked == true && selectedDlcs.Count() != ExtraContentLB.Items.Count)
            {
                uncheckedByUser = false;
                AllOrNothingChk.IsChecked = false;
                uncheckedByUser = true;
            }
            if (AllOrNothingChk.IsChecked == false && selectedDlcs.Count() == ExtraContentLB.Items.Count)
            {
                checkedByUser = false;
                AllOrNothingChk.IsChecked = true;
                checkedByUser = true;
            }
            await UpdateSizeInfo();
            SaveBtn.IsEnabled = true;
        }

        private void AllOrNothingChk_Checked(object sender, RoutedEventArgs e)
        {
            if (checkedByUser)
            {
                ExtraContentLB.SelectAll();
            }
        }

        private void AllOrNothingChk_Unchecked(object sender, RoutedEventArgs e)
        {
            if (uncheckedByUser)
            {
                ExtraContentLB.SelectedItems.Clear();
            }
        }

        private async void ChooseGamePathBtn_Click(object sender, RoutedEventArgs e)
        {
            var path = playniteAPI.Dialogs.SelectFolder();
            if (path != "")
            {
                SelectedGamePathTxt.Text = path;
                await UpdateSizeInfo();
            }
        }

        private async void GogOssDownloadPropertiesUC_Loaded(object sender, RoutedEventArgs e)
        {
            MaxWorkersNI.MaxValue = Helpers.CpuThreadsNumber;
            var wantedItem = SelectedDownload;
            if (wantedItem.downloadProperties != null)
            {
                SelectedGamePathTxt.Text = wantedItem.downloadProperties.installPath;
                MaxWorkersNI.Value = wantedItem.downloadProperties.maxWorkers.ToString();
                TaskCBo.SelectedValue = wantedItem.downloadProperties.downloadAction;
            }
            var downloadActionOptions = new Dictionary<DownloadAction, string>
            {
                { DownloadAction.Install, ResourceProvider.GetString(LOC.GogOss3P_PlayniteInstallGame) },
                { DownloadAction.Repair, ResourceProvider.GetString(LOC.GogOssRepair) },
                { DownloadAction.Update, ResourceProvider.GetString(LOC.GogOss3P_PlayniteUpdaterInstallUpdate) }
            };
            TaskCBo.ItemsSource = downloadActionOptions;
            var manifest = await Gogdl.GetGameInfo(wantedItem);
            var betaChannels = new Dictionary<string, string>();
            if (manifest.available_branches.Count > 1)
            {
                foreach (var branch in manifest.available_branches)
                {
                    if (branch == null)
                    {
                        betaChannels.Add("disabled", ResourceProvider.GetString(LOC.GogOss3P_PlayniteDisabledTitle));
                    }
                    else
                    {
                        betaChannels.Add(branch, branch);
                    }
                }
                if (betaChannels.Count > 0)
                {
                    BetaChannelCBo.ItemsSource = betaChannels;
                    var selectedBetaChannel = "disabled";
                    if (!wantedItem.downloadProperties.betaChannel.IsNullOrEmpty() && manifest.available_branches.Contains(wantedItem.downloadProperties.betaChannel))
                    {
                        selectedBetaChannel = wantedItem.downloadProperties.betaChannel;
                    }
                    BetaChannelCBo.SelectedValue = selectedBetaChannel;
                    BetaChannelSP.Visibility = Visibility.Visible;
                }
            }
            gameInfo = new Installed()
            {
                platform = SelectedDownload.downloadProperties.os,
                title = SelectedDownload.name,
                build_id = SelectedDownload.downloadProperties.buildId,
                version = SelectedDownload.downloadProperties.version,
                language = SelectedDownload.downloadProperties.language,
                installed_DLCs = SelectedDownload.downloadProperties.extraContent,
            };
            selectedBetaChannel = SelectedDownload.downloadProperties.betaChannel;
            await RefreshVersions();

            if (wantedItem.status != DownloadStatus.Canceled)
            {
                GameLanguageCBo.IsEnabled = false;
                GameVersionCBo.IsEnabled = false;
                BetaChannelCBo.IsEnabled = false;
                ExtraContentLB.IsEnabled = false;
            }

            if (wantedItem.status == DownloadStatus.Completed)
            {
                SelectedGamePathTxt.IsEnabled = false;
                ChooseGamePathBtn.IsEnabled = false;
                TaskCBo.IsEnabled = false;
                MaxWorkersNI.IsEnabled = false;
                SaveBtn.IsEnabled = false;
            }
        }

        private async Task RefreshVersions()
        {
            VersionSP.Visibility = Visibility.Collapsed;
            var manifest = await Gogdl.GetGameInfo(SelectedDownload.gameID, gameInfo);
            var builds = manifest.builds.items;
            var gameVersions = new Dictionary<string, string>();
            if (builds.Count > 0)
            {
                var chosenBranch = selectedBetaChannel;
                if (chosenBranch == "disabled")
                {
                    chosenBranch = null;
                }
                foreach (var build in builds)
                {
                    if (build.branch == chosenBranch)
                    {
                        DateTimeFormatInfo formatInfo = CultureInfo.CurrentCulture.DateTimeFormat;
                        var versionNameFirstPart = $"{build.version_name} — ";
                        if (build.version_name == "")
                        {
                            versionNameFirstPart = "";
                        }
                        var versionName = $"{versionNameFirstPart}{build.date_published.ToLocalTime().ToString("d", formatInfo)}";
                        gameVersions.Add(build.build_id, versionName);
                    }
                }
                GameVersionCBo.ItemsSource = gameVersions;
                var selectedVersion = SelectedDownload.downloadProperties.buildId;
                if (selectedVersion.IsNullOrEmpty() || !gameVersions.ContainsKey(selectedVersion))
                {
                    selectedVersion = gameVersions.FirstOrDefault().Key;
                }
                GameVersionCBo.SelectedItem = gameVersions.First(i => i.Key == selectedVersion);
                if (gameVersions.Count > 1)
                {
                    VersionSP.Visibility = Visibility.Visible;
                }
            }
            await SetGameVersion();
        }

        private async Task SetGameVersion()
        {
            KeyValuePair<string, string> selectedVersion = (KeyValuePair<string, string>)GameVersionCBo.SelectedItem;
            var selectedBuildId = selectedVersion.Key;
            var selectedVersionName = selectedVersion.Value.Split('—')[0].Trim();
            gameInfo.build_id = selectedBuildId;
            gameInfo.version = selectedVersionName;
            var manifest = await Gogdl.GetGameInfo(SelectedDownload.gameID, gameInfo);
            var gameLanguages = await RefreshLanguages();
            if (gameLanguages.Count > 1)
            {
                var currentPlayniteLanguage = playniteAPI.ApplicationSettings.Language.Replace("_", "-");
                GameLanguageCBo.ItemsSource = gameLanguages;
                var newSelectedLanguage = "";
                if (gameLanguages.ContainsKey(gameInfo.language))
                {
                    newSelectedLanguage = gameInfo.language;
                }
                else
                {
                    if (gameLanguages.ContainsKey(currentPlayniteLanguage))
                    {
                        newSelectedLanguage = currentPlayniteLanguage;
                    }
                    else
                    {
                        currentPlayniteLanguage = currentPlayniteLanguage.Substring(0, currentPlayniteLanguage.IndexOf("-"));
                        if (gameLanguages.ContainsKey(currentPlayniteLanguage))
                        {
                            newSelectedLanguage = currentPlayniteLanguage;
                        }
                    }
                }
                GameLanguageCBo.SelectedValue = newSelectedLanguage;
                LanguageSP.Visibility = Visibility.Visible;
            }
            if (manifest.dlcs.Count > 0)
            {
                var settings = GogOssLibrary.GetSettings();
                ExtraContentLB.ItemsSource = manifest.dlcs;
                ExtraContentTbI.Visibility = Visibility.Visible;
                if (gameInfo.installed_DLCs.Count > 0)
                {
                    foreach (var selectedDlc in gameInfo.installed_DLCs)
                    {
                        var selectedDlcItem = manifest.dlcs.FirstOrDefault(d => d.id == selectedDlc);
                        if (selectedDlcItem != null)
                        {
                            ExtraContentLB.SelectedItems.Add(selectedDlcItem);
                        }
                    }
                }
                if (manifest.dlcs.Count > 1)
                {
                    AllOrNothingChk.Visibility = Visibility.Visible;
                }
                else
                {
                    AllOrNothingChk.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async Task<Dictionary<string, string>> RefreshLanguages()
        {
            var manifest = await Gogdl.GetGameInfo(SelectedDownload.gameID, gameInfo);
            var languages = manifest.languages;
            var gameLanguages = new Dictionary<string, string>();
            if (languages.Count > 1)
            {
                foreach (var language in languages)
                {
                    gameLanguages.Add(language, new CultureInfo(language).NativeName);
                }
                gameLanguages = gameLanguages.OrderBy(x => x.Value).ToDictionary(x => x.Key, x => x.Value);
            }
            return gameLanguages;
        }

        private void UpdateSpaceInfo(string path, double installSizeNumber)
        {
            DriveInfo dDrive = new DriveInfo(path);
            if (dDrive.IsReady)
            {
                long availableFreeSpace = dDrive.AvailableFreeSpace;
                SpaceTB.Text = Helpers.FormatSize(availableFreeSpace);
                UpdateAfterInstallingSize(availableFreeSpace, installSizeNumber);
            }
        }

        private void UpdateAfterInstallingSize(long availableFreeSpace, double installSizeNumber)
        {
            double afterInstallSizeNumber = (double)(availableFreeSpace - installSizeNumber);
            if (afterInstallSizeNumber < 0)
            {
                afterInstallSizeNumber = 0;
            }
            AfterInstallingTB.Text = Helpers.FormatSize(afterInstallSizeNumber);
        }

        private async Task UpdateSizeInfo()
        {
            var gameSize = await Gogdl.CalculateGameSize(SelectedDownload.gameID, gameInfo);
            DownloadSizeTB.Text = Helpers.FormatSize(gameSize.download_size);
            InstallSizeTB.Text = Helpers.FormatSize(gameSize.disk_size);
            UpdateSpaceInfo(SelectedDownload.downloadProperties.installPath, gameSize.disk_size);
        }

        private async void GameLanguageCBo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GameLanguageCBo.IsDropDownOpen)
            {
                if (GameLanguageCBo.SelectedValue != null)
                {
                    SaveBtn.IsEnabled = false;
                    gameInfo.language = GameLanguageCBo.SelectedValue.ToString();
                    await UpdateSizeInfo();
                    SaveBtn.IsEnabled = true;
                }
            }
        }

        private async void GameVersionCBo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GameVersionCBo.IsDropDownOpen)
            {
                SaveBtn.IsEnabled = false;
                await SetGameVersion();
                await UpdateSizeInfo();
                SaveBtn.IsEnabled = true;
            }
        }

        private async void BetaChannelCBo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BetaChannelCBo.IsDropDownOpen)
            {
                SaveBtn.IsEnabled = false;
                if (BetaChannelCBo.SelectedValue != null)
                {
                    selectedBetaChannel = BetaChannelCBo.SelectedValue.ToString();
                }
                await RefreshVersions();
                await UpdateSizeInfo();
                SaveBtn.IsEnabled = true;
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).Close();
        }
    }
}
