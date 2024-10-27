using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
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
        public string installCommand;
        public double downloadSizeNumber;
        public double installSizeNumber;
        public long availableFreeSpace;
        public bool uncheckedByUser = true;
        private bool checkedByUser = true;
        public DownloadManagerData.Download singleGameInstallData;
        public GogGameMetaManifest manifest;
        public GogBuildsData builds;
        private static readonly HttpClient httpClient = new HttpClient();

        public GogOssGameInstallerView()
        {
            InitializeComponent();
            SetControlStyles();
        }

        private void SetControlStyles()
        {
            var baseStyleName = "BaseTextBlockStyle";
            if (playniteAPI.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
            {
                baseStyleName = "TextBlockBaseStyle";
                Resources.Add(typeof(Button), new Style(typeof(Button), null));
            }

            if (ResourceProvider.GetResource(baseStyleName) is Style baseStyle && baseStyle.TargetType == typeof(TextBlock))
            {
                var implicitStyle = new Style(typeof(TextBlock), baseStyle);
                Resources.Add(typeof(TextBlock), implicitStyle);
            }
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
                SpaceTB.Text = Helpers.FormatSize(availableFreeSpace);
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
            AfterInstallingTB.Text = Helpers.FormatSize(afterInstallSizeNumber);
        }

        public async Task StartTask(DownloadAction downloadAction)
        {
            var settings = GogOssLibrary.GetSettings();
            var installPath = SelectedGamePathTxt.Text;
            if (installPath == "")
            {
                installPath = Gogdl.GamesInstallationPath;
            }
            var playniteDirectoryVariable = ExpandableVariables.PlayniteDirectory.ToString();
            if (installPath.Contains(playniteDirectoryVariable))
            {
                installPath = installPath.Replace(playniteDirectoryVariable, playniteAPI.Paths.ApplicationPath);
            }

            var redistInstallPath = Gogdl.DependenciesInstallationPath;
            if (!Helpers.IsDirectoryWritable(installPath))
            {
                return;
            }
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
                    if (downloadAction == DownloadAction.Repair)
                    {
                        var installedInfo = GogOss.GetInstalledInfo(installData.gameID);
                        installPath = installedInfo.install_path;
                        installData.fullInstallPath = installPath;
                    }
                    var downloadProperties = GetDownloadProperties(installData, downloadAction, installPath);
                    if (installData.downloadItemType == DownloadItemType.Dependency)
                    {
                        downloadProperties = GetDownloadProperties(installData, downloadAction, redistInstallPath);
                    }
                    installData.downloadProperties = downloadProperties;
                    downloadTasks.Add(installData);
                }
            }
            if (downloadItemsAlreadyAdded.Count > 0)
            {
                if (downloadItemsAlreadyAdded.Count == 1)
                {
                    playniteAPI.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString(LOC.GogOssDownloadAlreadyExists), downloadItemsAlreadyAdded[0]), "", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    string downloadItemsAlreadyAddedComnined = string.Join(", ", downloadItemsAlreadyAdded.Select(item => item.ToString()));
                    playniteAPI.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString(LOC.GogOssDownloadAlreadyExistsOther), downloadItemsAlreadyAddedComnined), "", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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

        public DownloadManagerData.DownloadProperties GetDownloadProperties(DownloadManagerData.Download installData, DownloadAction downloadAction, string installPath = "")
        {
            var settings = GogOssLibrary.GetSettings();
            int maxWorkers = settings.MaxWorkers;
            if (MaxWorkersNI.Value != "")
            {
                maxWorkers = int.Parse(MaxWorkersNI.Value);
            }
            installData.downloadProperties.downloadAction = downloadAction;
            if (installPath != "")
            {
                installData.downloadProperties.installPath = installPath;
            }
            installData.downloadProperties.maxWorkers = maxWorkers;
            return installData.downloadProperties;
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
            DownloadSizeTB.Text = Helpers.FormatSize(downloadSizeNumber);
            InstallSizeTB.Text = Helpers.FormatSize(installSizeNumber);
        }

        private async void GogOssGameInstallerUC_Loaded(object sender, RoutedEventArgs e)
        {
            //httpClient.DefaultRequestHeaders.Clear();
            //httpClient.DefaultRequestHeaders.Add("User-Agent", GogOss.UserAgent);
            //var gogAccountClient = new GogAccountClient();
            //if (await gogAccountClient.GetIsUserLoggedIn())
            //{
            //    var tokens = gogAccountClient.LoadTokens();
            //    httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + tokens.access_token);
            //    var response = await httpClient.GetAsync($"https://content-system.gog.com/products/{MultiInstallData.First().gameID}/secure_link?generation=2&_version=2&path=");
            //    if (response.IsSuccessStatusCode)
            //    {
            //        var content = await response.Content.ReadAsStringAsync();
            //        logger.Debug("heh");
            //        logger.Debug(content);
            //    }
            //    else
            //    {
            //        var content = await response.Content.ReadAsStringAsync();
            //        logger.Debug("fail");
            //        logger.Debug(content);
            //    }
            //}

            if (MultiInstallData.First().downloadProperties.downloadAction == DownloadAction.Repair)
            {
                FolderDP.Visibility = Visibility.Collapsed;
                InstallBtn.Visibility = Visibility.Collapsed;
                RepairBtn.Visibility = Visibility.Visible;
                AfterInstallingSP.Visibility = Visibility.Collapsed;
            }
            var settings = GogOssLibrary.GetSettings();
            var installPath = Gogdl.GamesInstallationPath;
            var playniteDirectoryVariable = ExpandableVariables.PlayniteDirectory.ToString();
            if (installPath.Contains(playniteDirectoryVariable))
            {
                installPath = installPath.Replace(playniteDirectoryVariable, playniteAPI.Paths.ApplicationPath);
            }
            SelectedGamePathTxt.Text = installPath;
            UpdateSpaceInfo(installPath);
            var cacheInfoPath = GogOssLibrary.Instance.GetCachePath("infocache");
            if (!Directory.Exists(cacheInfoPath))
            {
                Directory.CreateDirectory(cacheInfoPath);
            }
            MaxWorkersNI.MaxValue = Helpers.CpuThreadsNumber;
            MaxWorkersNI.Value = settings.MaxWorkers.ToString();
            var downloadItemsAlreadyAdded = new List<string>();
            downloadSizeNumber = 0;
            installSizeNumber = 0;

            GogOssDownloadManagerView downloadManager = GogOssLibrary.GetGogOssDownloadManager();

            //var redistTask = new DownloadManagerData.Download
            //{
            //    gameID = "gog-redist",
            //    name = "GOG Common Redistributables",
            //    downloadItemType = DownloadItemType.Dependency,
            //    fullInstallPath = Gogdl.DependenciesInstallationPath
            //};
            //var requiredDepends = Gogdl.GetRequiredDepends();
            //redistTask.depends = new List<string>
            //{
            //    "ISI"
            //};
            bool gamesListShouldBeDisplayed = false;
            var redistInstallPath = Gogdl.DependenciesInstallationPath;

            var installedAppList = GogOssLibrary.GetInstalledAppList();

            foreach (var installData in MultiInstallData.ToList())
            {
                builds = await GogOss.GetGameBuilds(installData.gameID);
                if (builds.errorDisplayed || builds.items.Count == 0)
                {
                    gamesListShouldBeDisplayed = true;
                    MultiInstallData.Remove(installData);
                    continue;
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
                string selectedBetaChannel = installData.downloadProperties.betaChannel;
                if (selectedBetaChannel == "disabled")
                {
                    selectedBetaChannel = "";
                }
                if (installData.downloadProperties.buildId.IsNullOrEmpty())
                {
                    var selectedBuild = builds.items.FirstOrDefault(i => i.branch == selectedBetaChannel);
                    installData.downloadProperties.buildId = selectedBuild.build_id;
                    installData.downloadProperties.version = selectedBuild.version_name;
                }
                manifest = await GogOss.GetGameMetaManifest(installData);
                //if (manifest.dependencies.Count > 0)
                //{
                //    installData.depends = manifest.dependencies;
                //    foreach (var depend in manifest.dependencies)
                //    {
                //        redistTask.depends.AddMissing(depend);
                //    }
                //}
                RefreshLanguages(installData);

                if (manifest.dlcs.Count > 1 && settings.DownloadAllDlcs && installData.downloadProperties.extraContent.Count == 0)
                {
                    foreach (var dlc in manifest.dlcs)
                    {
                        installData.downloadProperties.extraContent.Add(dlc.Key);
                    }
                }
                var gameSize = await Gogdl.CalculateGameSize(installData);
                installData.fullInstallPath = Path.Combine(installPath, manifest.installDirectory);
                if (installData.downloadItemType == DownloadItemType.Dependency)
                {
                    installData.fullInstallPath = Path.Combine(redistInstallPath, "__redist", installData.gameID);
                }
                installData.downloadSizeNumber = gameSize.download_size;
                installData.installSizeNumber = gameSize.disk_size;
                var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == installData.gameID);
                if (wantedItem != null)
                {
                    downloadItemsAlreadyAdded.Add(installData.name);
                    MultiInstallData.Remove(installData);
                }
            }

            if (MultiInstallData.Count == 1)
            {
                var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == MultiInstallData[0].gameID);
                manifest = await GogOss.GetGameMetaManifest(MultiInstallData[0]);
                if (!manifest.errorDisplayed)
                {
                    singleGameInstallData = MultiInstallData[0];
                    var betaChannels = new Dictionary<string, string>();
                    builds = await GogOss.GetGameBuilds(singleGameInstallData.gameID);
                    if (builds.errorDisplayed || builds.items.Count == 0)
                    {
                        gamesListShouldBeDisplayed = false;
                        MultiInstallData.Remove(singleGameInstallData);
                        return;
                    }
                    if (builds.available_branches.Count > 1)
                    {
                        foreach (var branch in builds.available_branches)
                        {
                            if (branch == "")
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

            var downloadedDepends = Gogdl.GetDownloadedDepends();
            //bool requiredDependsDownloaded = true;
            //if (redistTask.depends.Count > 0 && requiredDepends.Count > 0)
            //{
            //    foreach (var requiredDepend in requiredDepends)
            //    {
            //        if (!redistTask.depends.Contains(requiredDepend))
            //        {
            //            redistTask.depends.Add(requiredDepend);
            //        }
            //        var dependDownloaded = downloadedDepends.FirstOrDefault(d => d == requiredDepend);
            //        if (dependDownloaded == null)
            //        {
            //            requiredDependsDownloaded = false;
            //        }
            //    }
            //}

            //if (redistTask.depends.Count != requiredDepends.Count || !requiredDependsDownloaded)
            //{
            //    redistTask.downloadSizeNumber = 0;
            //    redistTask.installSizeNumber = 0;
            //    foreach (var depend in redistTask.depends.ToList())
            //    {
            //        if (!downloadedDepends.Contains(depend))
            //        {
            //            var dependInstallData = new DownloadManagerData.Download
            //            {
            //                gameID = depend,
            //                downloadItemType = DownloadItemType.Dependency
            //            };
            //            dependInstallData.downloadProperties.version = "2";
            //            var dependInfo = await GogOss.GetGameMetaManifest(dependInstallData);
            //            {
            //                if (dependInfo.executable.path.IsNullOrEmpty())
            //                {
            //                    redistTask.depends.Remove(depend);
            //                    continue;
            //                }
            //            }
            //            var dependSize = await Gogdl.CalculateGameSize(dependInstallData);
            //            redistTask.downloadSizeNumber += dependSize.download_size;
            //            redistTask.installSizeNumber += dependSize.disk_size;
            //        }
            //    }
            //    if (redistTask.downloadSizeNumber != 0)
            //    {
            //        var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == "gog-redist");
            //        if (wantedItem == null)
            //        {
            //            MultiInstallData.Insert(0, redistTask);
            //        }
            //        else if (wantedItem.status != Enums.DownloadStatus.Running)
            //        {
            //            downloadManager.downloadManagerData.downloads.Remove(wantedItem);
            //            MultiInstallData.Insert(0, redistTask);
            //        }
            //    }
            //}

            CalculateTotalSize();
            if (downloadItemsAlreadyAdded.Count > 0)
            {
                if (downloadItemsAlreadyAdded.Count == 1)
                {
                    playniteAPI.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString(LOC.GogOssDownloadAlreadyExists), downloadItemsAlreadyAdded[0]), "", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    string downloadItemsAlreadyAddedComnined = string.Join(", ", downloadItemsAlreadyAdded.Select(item => item.ToString()));
                    playniteAPI.Dialogs.ShowMessage(string.Format(ResourceProvider.GetString(LOC.GogOssDownloadAlreadyExistsOther), downloadItemsAlreadyAddedComnined), "", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            var games = MultiInstallData.Where(i => i.downloadItemType == DownloadItemType.Game).ToList();
            GamesLB.ItemsSource = games;
            if ((games.Count > 1 && singleGameInstallData == null) || gamesListShouldBeDisplayed)
            {
                GamesBrd.Visibility = Visibility.Visible;
            }

            if (games.Count <= 0)
            {
                InstallerWindow.Close();
                return;
            }
            if (downloadSizeNumber != 0 && installSizeNumber != 0)
            {
                InstallBtn.IsEnabled = true;
            }
            else if (games.First().downloadProperties.downloadAction != DownloadAction.Repair)
            {
                InstallerWindow.Close();
            }
            if (settings.UnattendedInstall && (games.First().downloadProperties.downloadAction == DownloadAction.Install))
            {
                await StartTask(DownloadAction.Install);
            }
        }

        private Dictionary<string, string> RefreshLanguages(DownloadManagerData.Download installData)
        {
            var currentPlayniteLanguage = playniteAPI.ApplicationSettings.Language.Replace("_", "-");
            var languages = manifest.languages;
            var selectedLanguage = "";
            var gameLanguages = new Dictionary<string, string>();
            if (languages.Count > 1)
            {
                foreach (var language in languages)
                {
                    gameLanguages.Add(language, new CultureInfo(language).NativeName);
                }
                gameLanguages = gameLanguages.OrderBy(x => x.Value).ToDictionary(x => x.Key, x => x.Value);
                if (!installData.downloadProperties.language.IsNullOrEmpty() && gameLanguages.ContainsKey(installData.downloadProperties.language))
                {
                    selectedLanguage = installData.downloadProperties.language;
                }
                else
                {
                    if (gameLanguages.ContainsKey(currentPlayniteLanguage))
                    {
                        selectedLanguage = currentPlayniteLanguage;
                    }
                    else
                    {
                        currentPlayniteLanguage = currentPlayniteLanguage.Substring(0, currentPlayniteLanguage.IndexOf("-"));
                        if (gameLanguages.ContainsKey(currentPlayniteLanguage))
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
                    var gameSize = await Gogdl.CalculateGameSize(installData);
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
                var gameSize = await Gogdl.CalculateGameSize(singleGameInstallData);
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
            manifest = await GogOss.GetGameMetaManifest(singleGameInstallData);
            var gameLanguages = RefreshLanguages(singleGameInstallData);
            if (gameLanguages.Count > 1)
            {
                GameLanguageCBo.ItemsSource = gameLanguages;
                GameLanguageCBo.SelectedValue = singleGameInstallData.downloadProperties.language;
                LanguageSP.Visibility = Visibility.Visible;
            }
            if (manifest.dlcs.Count > 0)
            {
                logger.Debug("DLC");
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
                if (settings.DownloadAllDlcs)
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
                        gameVersions.Add(build.build_id, versionName);
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
            await SetGameVersion();
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
                var gameSize = await Gogdl.CalculateGameSize(singleGameInstallData);
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
                var gameSize = await Gogdl.CalculateGameSize(installData);
                installData.downloadSizeNumber = gameSize.download_size;
                installData.installSizeNumber = gameSize.disk_size;
                CalculateTotalSize();
                InstallBtn.IsEnabled = true;
            }
        }

        private async void GameExtraSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
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
                //manifest = await GogOss.GetGameMetaManifest(selectedGame);
                var gameSize = await Gogdl.CalculateGameSize(selectedGame);
                selectedGame.downloadSizeNumber = gameSize.download_size;
                selectedGame.installSizeNumber = gameSize.disk_size;
                CalculateTotalSize();
                InstallBtn.IsEnabled = true;
            }
        }
    }
}
