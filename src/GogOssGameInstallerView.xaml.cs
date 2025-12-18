using CommonPlugin;
using CommonPlugin.Enums;
using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Linguini.Shared.Types.Bundle;
using Playnite.SDK;
using Playnite.SDK.Data;
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
    /// Interaction logic for GogOssGameInstallerView.xaml
    /// </summary>
    public partial class GogOssGameInstallerView : UserControl
    {
        private ILogger logger = LogManager.GetLogger();
        private IPlayniteAPI playniteAPI = API.Instance;
        public double downloadSizeNumber;
        public double installSizeNumber;
        public long availableFreeSpace;
        private GogGameMetaManifest manifest;
        public bool uncheckedByUser = true;
        private bool checkedByUser = true;
        public DownloadManagerData.Download singleGameInstallData;
        public GogBuildsData builds;

        public GogOssGameInstallerView()
        {
            InitializeComponent();
        }

        public Window InstallerWindow => Window.GetWindow(this);

        private List<DownloadManagerData.Download> MultiInstallData
        {
            get => (List<DownloadManagerData.Download>)DataContext;
            set { }
        }

        private void ChooseGamePathBtn_Click(object sender, RoutedEventArgs e)
        {
            var path = playniteAPI.Dialogs.SelectFolder();
            if (path != "")
            {
                SelectedGamePathTxt.Text = path;
                UpdateSpaceInfo(path);
            }
        }

        private void UpdateSpaceInfo(string path)
        {
            DriveInfo dDrive = new DriveInfo(path);
            if (dDrive.IsReady)
            {
                availableFreeSpace = dDrive.AvailableFreeSpace;
                SpaceTB.Text = CommonHelpers.FormatSize(availableFreeSpace);
            }
            UpdateAfterInstallingSize();
        }

        private void UpdateAfterInstallingSize()
        {
            double afterInstallSizeNumber = (double)(availableFreeSpace - installSizeNumber);
            if (afterInstallSizeNumber < 0)
            {
                afterInstallSizeNumber = 0;
            }
            AfterInstallingTB.Text = CommonHelpers.FormatSize(afterInstallSizeNumber);
        }

        public async Task StartTask(DownloadAction downloadAction)
        {
            var gogDownloadApi = new GogDownloadApi();
            var settings = GogOssLibrary.GetSettings();
            var installPath = SelectedGamePathTxt.Text;
            if (installPath == "")
            {
                installPath = GogOss.GamesInstallationPath;
            }
            var playniteDirectoryVariable = ExpandableVariables.PlayniteDirectory.ToString();
            if (installPath.Contains(playniteDirectoryVariable))
            {
                installPath = installPath.Replace(playniteDirectoryVariable, playniteAPI.Paths.ApplicationPath);
            }

            var redistInstallPath = GogOss.DependenciesInstallationPath;
            InstallerWindow.Close();
            GogOssDownloadManagerView downloadManager = GogOssLibrary.GetGogOssDownloadManager();
            var downloadTasks = new List<DownloadManagerData.Download>();
            var downloadItemsAlreadyAdded = new List<string>();
            foreach (var installData in MultiInstallData)
            {
                var gameId = installData.gameID;
                var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == gameId);
                if (wantedItem == null)
                {
                    if (installData.downloadProperties.installPath.IsNullOrEmpty())
                    {
                        if (installData.downloadItemType == DownloadItemType.Dependency)
                        {
                            installData.downloadProperties.installPath = redistInstallPath;
                        }
                        else
                        {
                            installData.downloadProperties.installPath = installPath;
                        }
                    }
                    if (installData.downloadItemType == DownloadItemType.Dependency)
                    {
                        installData.fullInstallPath = Path.Combine(GogOss.DependenciesInstallationPath, "__redist", gameId);
                    }
                    else if (installData.downloadItemType == DownloadItemType.Game)
                    {
                        manifest = await gogDownloadApi.GetGameMetaManifest(installData);
                        installData.fullInstallPath = Path.Combine(installPath, manifest.installDirectory);
                    }
                    else if (installData.downloadItemType == DownloadItemType.Overlay)
                    {
                        installData.fullInstallPath = Path.Combine(installPath, ".galaxy-overlay");
                    }
                    if (!CommonHelpers.IsDirectoryWritable(installPath, LOC.CommonPermissionError))
                    {
                        continue;
                    }
                    var downloadProperties = GetDownloadProperties(installData, downloadAction);
                    installData.downloadProperties = downloadProperties;
                    downloadTasks.Add(installData);
                }
            }
            if (downloadItemsAlreadyAdded.Count > 0)
            {
                string downloadItemsAlreadyAddedCombined = downloadItemsAlreadyAdded[0];
                if (downloadItemsAlreadyAdded.Count > 1)
                {
                    downloadItemsAlreadyAddedCombined = string.Join(", ", downloadItemsAlreadyAdded.Select(item => item.ToString()));
                }
                playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonDownloadAlreadyExists, new Dictionary<string, IFluentType> { ["appName"] = (FluentString)downloadItemsAlreadyAddedCombined, ["count"] = (FluentNumber)downloadItemsAlreadyAdded.Count }), "", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            if (downloadTasks.Count > 0)
            {
                await downloadManager.EnqueueMultipleJobs(downloadTasks);
            }
        }

        private async void InstallBtn_Click(object sender, RoutedEventArgs e)
        {
            await StartTask(DownloadAction.Install);
        }

        private async void RepairBtn_Click(object sender, RoutedEventArgs e)
        {
            foreach (var installData in MultiInstallData)
            {
                installData.downloadSizeNumber = 0;
                installData.installSizeNumber = 0;
            }
            await StartTask(DownloadAction.Repair);
        }

        public DownloadProperties GetDownloadProperties(DownloadManagerData.Download installData, DownloadAction downloadAction)
        {
            var settings = GogOssLibrary.GetSettings();
            int maxWorkers = settings.MaxWorkers;
            if (MaxWorkersNI.Value != "")
            {
                maxWorkers = int.Parse(MaxWorkersNI.Value);
            }
            DownloadProperties newDownloadProperties = new();
            newDownloadProperties = Serialization.GetClone(installData.downloadProperties);
            newDownloadProperties.downloadAction = downloadAction;
            newDownloadProperties.maxWorkers = maxWorkers;
            return newDownloadProperties;
        }

        private void CalculateTotalSize()
        {
            downloadSizeNumber = 0;
            installSizeNumber = 0;
            foreach (var installData in MultiInstallData)
            {
                downloadSizeNumber += installData.downloadSizeNumber;
                installSizeNumber += installData.installSizeNumber;
            }
            UpdateAfterInstallingSize();
            DownloadSizeTB.Text = CommonHelpers.FormatSize(downloadSizeNumber);
            InstallSizeTB.Text = CommonHelpers.FormatSize(installSizeNumber);
        }

        private async void GogOssGameInstallerUC_Loaded(object sender, RoutedEventArgs e)
        {
            CommonHelpers.SetControlBackground(this);
            if (MultiInstallData.First().downloadProperties.downloadAction == DownloadAction.Repair)
            {
                FolderDP.Visibility = Visibility.Collapsed;
                InstallBtn.Visibility = Visibility.Collapsed;
                RepairBtn.Visibility = Visibility.Visible;
                AfterInstallingSP.Visibility = Visibility.Collapsed;
            }
            await RefreshAll();
            var settings = GogOssLibrary.GetSettings();
            var games = MultiInstallData.Where(i => i.downloadItemType == DownloadItemType.Game);
            if (settings.UnattendedInstall && (games.First().downloadProperties.downloadAction == DownloadAction.Install))
            {
                await StartTask(DownloadAction.Install);
            }
        }

        public async Task RefreshAll()
        {
            InstallBtn.IsEnabled = false;
            ReloadBtn.IsEnabled = false;
            var settings = GogOssLibrary.GetSettings();
            var installPath = GogOss.GamesInstallationPath;
            var playniteDirectoryVariable = ExpandableVariables.PlayniteDirectory.ToString();
            if (installPath.Contains(playniteDirectoryVariable))
            {
                installPath = installPath.Replace(playniteDirectoryVariable, playniteAPI.Paths.ApplicationPath);
            }
            SelectedGamePathTxt.Text = installPath;
            UpdateSpaceInfo(installPath);
            MaxWorkersNI.MaxValue = GogOss.MaxMaxWorkers;
            MaxWorkersNI.Value = settings.MaxWorkers.ToString();
            var downloadItemsAlreadyAdded = new List<string>();
            downloadSizeNumber = 0;
            installSizeNumber = 0;

            GogOssDownloadManagerView downloadManager = GogOssLibrary.GetGogOssDownloadManager();

            var depends = new List<string>();
            if (MultiInstallData.Count > 0 && MultiInstallData[0].downloadItemType != DownloadItemType.Overlay)
            {
                depends.Add("ISI");
            }

            bool gamesListShouldBeDisplayed = false;
            var redistInstallPath = GogOss.DependenciesInstallationPath;

            var installedAppList = GogOssLibrary.GetInstalledAppList();

            var gogDownloadApi = new GogDownloadApi();

            if (MultiInstallData.Count > 0 && MultiInstallData[0].downloadItemType != DownloadItemType.Overlay)
            {
                foreach (var installData in MultiInstallData.ToList())
                {
                    if (installData.downloadItemType == DownloadItemType.Game)
                    {
                        builds = await gogDownloadApi.GetProductBuilds(installData.gameID);
                        if (builds.errorDisplayed || builds.installable == false)
                        {
                            if (builds.installable == false)
                            {
                                playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.GogOssGameNotInstallable, new Dictionary<string, IFluentType> { ["gameTitle"] = (FluentString)installData.name, ["url"] = (FluentString)"https://gog.com/account " }));
                            }
                            gamesListShouldBeDisplayed = true;
                            MultiInstallData.Remove(installData);
                            continue;
                        }
                    }

                    if (installedAppList.ContainsKey(installData.gameID))
                    {
                        var installedGame = installedAppList[installData.gameID];
                        if (installData.downloadProperties.downloadAction == DownloadAction.Repair)
                        {
                            installData.downloadProperties.version = installedGame.version;
                            installData.downloadProperties.buildId = installedGame.build_id;
                        }
                        installData.downloadProperties.language = installedGame.language;
                        installData.downloadProperties.extraContent = installedGame.installed_DLCs;
                    }
                    manifest = await gogDownloadApi.GetGameMetaManifest(installData);
                    if (manifest.dependencies.Count > 0)
                    {
                        installData.depends = manifest.dependencies;
                        if (manifest.version == 1)
                        {
                            foreach (var dependv1 in manifest.depots)
                            {
                                if (!dependv1.redist.IsNullOrEmpty() && dependv1.targetDir.IsNullOrEmpty())
                                {
                                    installData.depends.Add(dependv1.redist);
                                }
                            }
                        }
                        foreach (var depend in manifest.dependencies)
                        {
                            depends.AddMissing(depend);
                        }
                    }
                    RefreshLanguages(installData);
                    if (installData.downloadProperties.buildId.IsNullOrEmpty())
                    {
                        installData.downloadProperties.buildId = manifest.buildId;
                        installData.downloadProperties.version = manifest.versionName;
                    }
                    if (manifest.dlcs.Count > 1 && settings.DownloadAllDlcs && installData.downloadProperties.extraContent.Count == 0)
                    {
                        foreach (var dlc in manifest.dlcs)
                        {
                            installData.downloadProperties.extraContent.Add(dlc.Key);
                        }
                    }
                    var gameSize = await GogOss.CalculateGameSize(installData);
                    installData.downloadSizeNumber = gameSize.download_size;
                    installData.installSizeNumber = gameSize.disk_size;
                    var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == installData.gameID);
                    if (wantedItem != null)
                    {
                        if (wantedItem.status == DownloadStatus.Completed)
                        {
                            downloadManager.downloadManagerData.downloads.Remove(wantedItem);
                        }
                        else
                        {
                            downloadItemsAlreadyAdded.Add(installData.name);
                            MultiInstallData.Remove(installData);
                        }
                    }
                }
            }

            if (MultiInstallData.Count == 1)
            {
                if (MultiInstallData[0].downloadItemType == DownloadItemType.Game)
                {
                    builds = await gogDownloadApi.GetProductBuilds(MultiInstallData[0].gameID);
                    if (!builds.errorDisplayed)
                    {
                        singleGameInstallData = MultiInstallData[0];
                        var betaChannels = new Dictionary<string, string>();
                        if (builds.available_branches.Count > 1)
                        {
                            foreach (var branch in builds.available_branches)
                            {
                                if (branch == "")
                                {
                                    betaChannels.Add("disabled", LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteDisabledTitle));
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
                                if (!singleGameInstallData.downloadProperties.betaChannel.IsNullOrEmpty() && builds.available_branches.Contains(singleGameInstallData.downloadProperties.betaChannel))
                                {
                                    selectedBetaChannel = singleGameInstallData.downloadProperties.betaChannel;
                                }
                                BetaChannelCBo.SelectedValue = selectedBetaChannel;
                                BetaChannelSP.Visibility = Visibility.Visible;
                            }
                        }
                        await RefreshVersions();
                    }
                    else
                    {
                        MultiInstallData.Remove(MultiInstallData[0]);
                    }
                }
                if (MultiInstallData[0].downloadItemType == DownloadItemType.Overlay)
                {
                    var overlayInstallData = MultiInstallData[0];
                    var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == overlayInstallData.gameID);
                    if (wantedItem != null)
                    {
                        if (wantedItem.status == DownloadStatus.Completed)
                        {
                            downloadManager.downloadManagerData.downloads.Remove(wantedItem);
                        }
                        else
                        {
                            downloadItemsAlreadyAdded.Add(overlayInstallData.name);
                            MultiInstallData.Remove(overlayInstallData);
                        }
                    }
                    else
                    {
                        var overlayManifest = await GogOss.GetOverlayManifest();
                        if (overlayManifest != null && overlayManifest.files.Count > 0)
                        {
                            foreach (var file in overlayManifest.files)
                            {
                                overlayInstallData.downloadSizeNumber += file.size;
                                overlayInstallData.installSizeNumber += file.size;
                            }
                        }
                        else
                        {
                            MultiInstallData.Remove(overlayInstallData);
                        }
                    }
                }
            }

            if (depends.Count > 0)
            {
                foreach (var depend in depends.ToList())
                {
                    var dependManifest = await GogDownloadApi.GetRedistInfo(depend);
                    var dependInstallData = new DownloadManagerData.Download
                    {
                        gameID = depend,
                        downloadItemType = DownloadItemType.Dependency,
                        name = dependManifest.readableName
                    };
                    var dependDownloadPath = Path.Combine(GogOss.DependenciesInstallationPath, "__redist", depend);
                    if (Directory.Exists(dependDownloadPath))
                    {
                        var dependExePath = Path.Combine(GogOss.DependenciesInstallationPath, dependManifest.executable.path);
                        if (File.Exists(dependExePath))
                        {
                            depends.Remove(depend);
                            continue;
                        }
                    }
                    var dependInfo = await gogDownloadApi.GetGameMetaManifest(dependInstallData);

                    if (dependInfo.executable.path.IsNullOrEmpty())
                    {
                        depends.Remove(depend);
                        continue;
                    }
                    var dependSize = await GogOss.CalculateGameSize(dependInstallData);
                    dependInstallData.downloadSizeNumber = dependSize.download_size;
                    dependInstallData.installSizeNumber = dependSize.disk_size;
                    if (dependInstallData.downloadSizeNumber != 0)
                    {
                        var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == depend);
                        if (wantedItem == null)
                        {
                            MultiInstallData.Insert(0, dependInstallData);
                        }
                    }
                }
            }

            CalculateTotalSize();
            if (downloadItemsAlreadyAdded.Count > 0)
            {
                string downloadItemsAlreadyAddedCombined = downloadItemsAlreadyAdded[0];
                if (downloadItemsAlreadyAdded.Count == 1)
                {
                    downloadItemsAlreadyAddedCombined = string.Join(", ", downloadItemsAlreadyAdded.Select(item => item.ToString()));
                }
                playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonDownloadAlreadyExists, new Dictionary<string, IFluentType> { ["appName"] = (FluentString)downloadItemsAlreadyAddedCombined, ["count"] = (FluentNumber)downloadItemsAlreadyAdded.Count }), "", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            var apps = MultiInstallData.Where(i => i.downloadItemType == DownloadItemType.Game || i.downloadItemType == DownloadItemType.Overlay).ToList();
            GamesLB.ItemsSource = apps;
            if ((apps.Count > 1 && singleGameInstallData == null) || gamesListShouldBeDisplayed)
            {
                GamesBrd.Visibility = Visibility.Visible;
            }

            var clientApi = new GogAccountClient();
            var userLoggedIn = await clientApi.GetIsUserLoggedIn();
            if (apps.Count <= 0 || !userLoggedIn)
            {
                if (!userLoggedIn)
                {
                    playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteGameInstallError, new Dictionary<string, IFluentType> { ["var0"] = (FluentString)LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteLoginRequired) }));
                }
                if (apps.Count <= 0)
                {
                    InstallerWindow.Close();
                }
            }
            if (downloadSizeNumber != 0 && installSizeNumber != 0)
            {
                InstallBtn.IsEnabled = true;
            }
            ReloadBtn.IsEnabled = true;
        }


        private Dictionary<string, string> RefreshLanguages(DownloadManagerData.Download installData)
        {
            var currentPlayniteLanguage = playniteAPI.ApplicationSettings.Language.Replace("_", "-");
            var currentPlayniteLanguageNativeName = new CultureInfo(currentPlayniteLanguage).NativeName;
            var languages = manifest.languages;
            var selectedLanguage = "";
            var gameLanguages = new Dictionary<string, string>();
            if (languages.Count > 1)
            {
                foreach (var language in languages)
                {
                    var nativeLanguageName = language;
                    if (manifest.version > 1)
                    {
                        try
                        {
                            nativeLanguageName = new CultureInfo(language).NativeName;
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, $"Unrecognized language: {language}");
                        }
                    }
                    gameLanguages.Add(language, nativeLanguageName);
                }
                gameLanguages = gameLanguages.OrderBy(x => x.Value).ToDictionary(x => x.Key, x => x.Value);
                if (!installData.downloadProperties.language.IsNullOrEmpty() && gameLanguages.ContainsKey(installData.downloadProperties.language))
                {
                    selectedLanguage = installData.downloadProperties.language;
                }
                else
                {
                    if (gameLanguages.ContainsKey(currentPlayniteLanguage) || gameLanguages.ContainsKey(currentPlayniteLanguageNativeName))
                    {
                        selectedLanguage = currentPlayniteLanguage;
                    }
                    else
                    {
                        currentPlayniteLanguage = currentPlayniteLanguage.Substring(0, currentPlayniteLanguage.IndexOf("-"));
                        if (gameLanguages.ContainsKey(currentPlayniteLanguage) || gameLanguages.ContainsKey(currentPlayniteLanguageNativeName))
                        {
                            selectedLanguage = currentPlayniteLanguage;
                        }
                    }
                }
                installData.downloadProperties.language = selectedLanguage;
            }
            return gameLanguages;
        }

        private async void GameLanguageCBo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (singleGameInstallData != null && GameLanguageCBo.IsDropDownOpen)
            {
                DownloadManagerData.Download installData = singleGameInstallData;
                if (GameLanguageCBo.SelectedValue != null)
                {
                    InstallBtn.IsEnabled = false;
                    installData.downloadProperties.language = GameLanguageCBo.SelectedValue.ToString();
                    var gameSize = await GogOss.CalculateGameSize(installData);
                    installData.downloadSizeNumber = gameSize.download_size;
                    installData.installSizeNumber = gameSize.disk_size;
                    CalculateTotalSize();
                    InstallBtn.IsEnabled = true;
                }
            }
        }

        private async void GameVersionCBo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (singleGameInstallData != null && GameVersionCBo.IsDropDownOpen)
            {
                InstallBtn.IsEnabled = false;
                await SetGameVersion();
                var gameSize = await GogOss.CalculateGameSize(singleGameInstallData);
                singleGameInstallData.downloadSizeNumber = gameSize.download_size;
                singleGameInstallData.installSizeNumber = gameSize.disk_size;
                CalculateTotalSize();
                InstallBtn.IsEnabled = true;
            }
        }

        private async Task SetGameVersion()
        {
            KeyValuePair<string, string> selectedVersion = (KeyValuePair<string, string>)GameVersionCBo.SelectedItem;
            singleGameInstallData.downloadProperties.buildId = selectedVersion.Key;
            singleGameInstallData.downloadProperties.version = selectedVersion.Value.Split('—')[0].Trim();

            var gogDownloadApi = new GogDownloadApi();
            manifest = await gogDownloadApi.GetGameMetaManifest(singleGameInstallData);
            var gameLanguages = RefreshLanguages(singleGameInstallData);
            if (gameLanguages.Count > 1)
            {
                GameLanguageCBo.ItemsSource = gameLanguages;
                GameLanguageCBo.SelectedValue = singleGameInstallData.downloadProperties.language;
                LanguageSP.Visibility = Visibility.Visible;
            }
            if (manifest.dlcs.Count > 0)
            {
                var settings = GogOssLibrary.GetSettings();
                ExtraContentLB.ItemsSource = manifest.dlcs;
                ExtraContentBrd.Visibility = Visibility.Visible;
                if (singleGameInstallData.downloadProperties.extraContent.Count > 0)
                {
                    foreach (var selectedDlc in singleGameInstallData.downloadProperties.extraContent)
                    {
                        var selectedDlcItem = manifest.dlcs[selectedDlc];
                        if (selectedDlcItem != null)
                        {
                            ExtraContentLB.SelectedItems.Add(selectedDlcItem);
                        }
                    }
                }
                if (settings.DownloadAllDlcs && singleGameInstallData.downloadProperties.extraContent.Count == 0)
                {
                    ExtraContentLB.SelectAll();
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

        private async Task RefreshVersions()
        {
            VersionSP.Visibility = Visibility.Collapsed;
            var gameVersions = new Dictionary<string, string>();
            if (builds.items.Count > 0)
            {
                var chosenBranch = singleGameInstallData.downloadProperties.betaChannel;
                if (chosenBranch == "disabled")
                {
                    chosenBranch = "";
                }
                foreach (var build in builds.items)
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
                        var buildId = build.legacy_build_id;
                        if (buildId.IsNullOrEmpty())
                        {
                            buildId = build.build_id;
                        }
                        gameVersions.Add(buildId, versionName);
                    }
                }
                GameVersionCBo.ItemsSource = gameVersions;
                var selectedVersion = singleGameInstallData.downloadProperties.buildId;
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
            if (builds.items.Count > 0)
            {
                await SetGameVersion();
            }
        }

        private async void BetaChannelCBo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (singleGameInstallData != null && BetaChannelCBo.IsDropDownOpen)
            {
                InstallBtn.IsEnabled = false;
                if (BetaChannelCBo.SelectedValue != null)
                {
                    singleGameInstallData.downloadProperties.betaChannel = BetaChannelCBo.SelectedValue.ToString();
                }
                await RefreshVersions();
                var gameSize = await GogOss.CalculateGameSize(singleGameInstallData);
                singleGameInstallData.downloadSizeNumber = gameSize.download_size;
                singleGameInstallData.installSizeNumber = gameSize.disk_size;
                CalculateTotalSize();
                InstallBtn.IsEnabled = true;
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).Close();
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

        private async void ExtraContentLB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (singleGameInstallData != null)
            {
                InstallBtn.IsEnabled = false;
                DownloadManagerData.Download installData = singleGameInstallData;
                var selectedDlcs = ExtraContentLB.SelectedItems.Cast<KeyValuePair<string, GogGameMetaManifest.Dlc>>();
                installData.downloadProperties.extraContent = new List<string>();
                foreach (var selectedDlc in selectedDlcs)
                {
                    installData.downloadProperties.extraContent.Add(selectedDlc.Key);
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
                var gameSize = await GogOss.CalculateGameSize(installData);
                installData.downloadSizeNumber = gameSize.download_size;
                installData.installSizeNumber = gameSize.disk_size;
                CalculateTotalSize();
                InstallBtn.IsEnabled = true;
            }
        }

        private async void GameExtraSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            var gogDownloadApi = new GogDownloadApi();
            var selectedGame = ((Button)sender).DataContext as DownloadManagerData.Download;
            var playniteAPI = API.Instance;
            Window window = playniteAPI.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = false,
            });
            window.DataContext = selectedGame;
            window.Content = new GogOssExtraInstallationSettingsView();
            window.Owner = InstallerWindow;
            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.MinWidth = 600;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Title = selectedGame.name;
            var result = window.ShowDialog();
            if (result == false)
            {
                InstallBtn.IsEnabled = false;
                manifest = await gogDownloadApi.GetGameMetaManifest(selectedGame);
                var gameSize = await GogOss.CalculateGameSize(selectedGame);
                selectedGame.downloadSizeNumber = gameSize.download_size;
                selectedGame.installSizeNumber = gameSize.disk_size;
                CalculateTotalSize();
                InstallBtn.IsEnabled = true;
            }
        }

        private async void ReloadBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonReloadConfirm), LocalizationManager.Instance.GetString(LOC.CommonReload), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                InstallBtn.IsEnabled = false;
                DownloadSizeTB.Text = LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteLoadingLabel);
                var dataDir = GogOssLibrary.Instance.GetPluginUserDataPath();
                var cacheDir = Path.Combine(dataDir, "cache");
                foreach (var file in Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories))
                {
                    foreach (var installData in MultiInstallData)
                    {
                        if (file.Contains(installData.gameID))
                        {
                            File.Delete(file);
                        }
                    }
                }
                await RefreshAll();
            }

        }
    }
}
