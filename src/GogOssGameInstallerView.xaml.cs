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
        private GogDownloadGameInfo manifest;
        public bool uncheckedByUser = true;
        private bool checkedByUser = true;
        public DownloadManagerData.Download singleGameInstallData;
        public bool editDownloadPropertiesMode = false;

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

        public async Task Install()
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
                    var downloadProperties = GetDownloadProperties(installData, DownloadAction.Install, installPath);
                    if (installData.downloadItemType == DownloadItemType.Dependency)
                    {
                        downloadProperties = GetDownloadProperties(installData, DownloadAction.Install, redistInstallPath);
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
            await Install();
        }

        private void RepairBtn_Click(object sender, RoutedEventArgs e)
        {

        }

        public DownloadProperties GetDownloadProperties(DownloadManagerData.Download installData, DownloadAction downloadAction, string installPath = "")
        {
            var settings = GogOssLibrary.GetSettings();
            int maxWorkers = settings.MaxWorkers;
            if (MaxWorkersNI.Value != "")
            {
                maxWorkers = int.Parse(MaxWorkersNI.Value);
            }
            installData.downloadProperties.downloadAction = downloadAction;
            installData.downloadProperties.installPath = installPath;
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

        private GogDownloadGameInfo.SizeType CalculateGameSize(DownloadManagerData.Download installData)
        {
            var size = new GogDownloadGameInfo.SizeType
            {
                download_size = 0,
                disk_size = 0
            };
            if (manifest.size.ContainsKey("*"))
            {
                size.download_size += manifest.size["*"].download_size;
                size.disk_size += manifest.size["*"].disk_size;
            }
            var selectedLanguage = installData.downloadProperties.language;
            if (manifest.size.Count == 2)
            {
                selectedLanguage = manifest.size.ElementAt(1).Key.ToString();
            }
            if (manifest.size.ContainsKey(selectedLanguage))
            {
                size.download_size += manifest.size[selectedLanguage].download_size;
                size.disk_size += manifest.size[selectedLanguage].disk_size;
            }
            var selectedDlcs = installData.downloadProperties.extraContent;
            if (selectedDlcs.Count() > 0)
            {
                foreach (var dlc in manifest.dlcs)
                {
                    if (selectedDlcs.Contains(dlc.id))
                    {
                        if (dlc.size.ContainsKey("*"))
                        {
                            size.download_size += dlc.size["*"].download_size;
                            size.disk_size += dlc.size["*"].disk_size;
                        }
                        if (dlc.size.ContainsKey(selectedLanguage))
                        {
                            size.download_size += dlc.size[selectedLanguage].download_size;
                            size.disk_size += dlc.size[selectedLanguage].disk_size;
                        }
                    }
                }
            }
            return size;
        }

        private async void GogOssGameInstallerUC_Loaded(object sender, RoutedEventArgs e)
        {
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
            if (MultiInstallData.Count == 1)
            {
                var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == MultiInstallData[0].gameID);
                if (MultiInstallData[0].editDownloadPropertiesMode != null)
                {
                    editDownloadPropertiesMode = (bool)MultiInstallData[0].editDownloadPropertiesMode;
                }
                if (editDownloadPropertiesMode)
                {
                    GameVersionCBo.IsEnabled = false;
                    BetaChannelCBo.IsEnabled = false;
                    ExtraContentLB.IsEnabled = false;
                    AllOrNothingChk.IsEnabled = false;
                    var downloadActionOptions = new Dictionary<DownloadAction, string>
                    {
                        { DownloadAction.Install, ResourceProvider.GetString(LOC.GogOss3P_PlayniteInstallGame) },
                        { DownloadAction.Repair, ResourceProvider.GetString(LOC.GogOssRepair) },
                        { DownloadAction.Update, ResourceProvider.GetString(LOC.GogOss3P_PlayniteUpdaterInstallUpdate) }
                    };
                    TaskCBo.SelectedValue = wantedItem.downloadProperties.downloadAction;
                    TaskCBo.ItemsSource = downloadActionOptions;
                    TaskSP.Visibility = Visibility.Visible;
                }
                if (wantedItem != null && editDownloadPropertiesMode != true)
                {
                    downloadItemsAlreadyAdded.Add(MultiInstallData[0].name);
                    MultiInstallData.Remove(MultiInstallData[0]);
                }
            }

            if (MultiInstallData.Count == 1)
            {
                if (editDownloadPropertiesMode)
                {
                    InstallBtn.Visibility = Visibility.Collapsed;
                    RepairBtn.Visibility = Visibility.Collapsed;
                    SaveBtn.Visibility = Visibility.Visible;
                }
                manifest = await Gogdl.GetGameInfo(MultiInstallData[0]);
                if (!manifest.errorDisplayed)
                {
                    singleGameInstallData = MultiInstallData[0];
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
                            if (!singleGameInstallData.downloadProperties.betaChannel.IsNullOrEmpty() && manifest.available_branches.Contains(singleGameInstallData.downloadProperties.betaChannel))
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

            var installedAppList = GogOssLibrary.Instance.installedAppList;
            if (!installedAppList.ContainsKey("ISI"))
            {
                var isiTask = new DownloadManagerData.Download
                {
                    gameID = "ISI",
                    name = "Install Script Interpreter",
                    downloadItemType = DownloadItemType.Dependency,
                };
                var ISIalreadyAdded = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == isiTask.gameID);
                if (ISIalreadyAdded == null)
                {
                    MultiInstallData.Insert(0, isiTask);
                }
            }
            bool gamesListShouldBeDisplayed = false;
            var redistInstallPath = Gogdl.DependenciesInstallationPath;
            foreach (var installData in MultiInstallData.ToList())
            {
                manifest = await Gogdl.GetGameInfo(installData);
                if (manifest.errorDisplayed)
                {
                    gamesListShouldBeDisplayed = true;
                    MultiInstallData.Remove(installData);
                    continue;
                }
                RefreshLanguages(installData);
                if (installData.downloadProperties.buildId.IsNullOrEmpty())
                {
                    installData.downloadProperties.buildId = manifest.buildId;
                    installData.downloadProperties.version = manifest.versionName;
                }
                if (manifest.dlcs.Count > 1 && settings.DownloadAllDlcs)
                {
                    foreach (var dlc in manifest.dlcs)
                    {
                        installData.downloadProperties.extraContent.Add(dlc.id);
                    }
                }
                var gameSize = CalculateGameSize(installData);
                installData.fullInstallPath = Path.Combine(installPath, manifest.folder_name);
                if (installData.downloadItemType == DownloadItemType.Dependency)
                {
                    installData.fullInstallPath = Path.Combine(redistInstallPath, "__redist", installData.gameID);
                }
                installData.downloadSizeNumber = gameSize.download_size;
                installData.installSizeNumber = gameSize.disk_size;
                var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == installData.gameID);
                if (wantedItem != null && editDownloadPropertiesMode != true)
                {
                    downloadItemsAlreadyAdded.Add(installData.name);
                    MultiInstallData.Remove(installData);
                }
            }
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
            if (MultiInstallData.Count > 1 || gamesListShouldBeDisplayed)
            {
                GamesBrd.Visibility = Visibility.Visible;
            }

            if (MultiInstallData.Count <= 0)
            {
                InstallerWindow.Close();
                return;
            }
            if (downloadSizeNumber != 0 && installSizeNumber != 0)
            {
                InstallBtn.IsEnabled = true;
            }
            else if (MultiInstallData.First().downloadProperties.downloadAction != DownloadAction.Repair)
            {
                InstallerWindow.Close();
            }
            if (settings.UnattendedInstall && (MultiInstallData.First().downloadProperties.downloadAction == DownloadAction.Install) &&
                !editDownloadPropertiesMode)
            {
                await Install();
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

        private void GameLanguageCBo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (singleGameInstallData != null && GameLanguageCBo.IsDropDownOpen)
            {
                DownloadManagerData.Download installData = singleGameInstallData;
                if (GameLanguageCBo.SelectedValue != null)
                {
                    InstallBtn.IsEnabled = false;
                    installData.downloadProperties.language = GameLanguageCBo.SelectedValue.ToString();
                    var gameSize = CalculateGameSize(installData);
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
                var gameSize = CalculateGameSize(singleGameInstallData);
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
            manifest = await Gogdl.GetGameInfo(singleGameInstallData);
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
                        var selectedDlcItem = manifest.dlcs.FirstOrDefault(d => d.id == selectedDlc);
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
            var builds = manifest.builds.items;
            var gameVersions = new Dictionary<string, string>();
            if (builds.Count > 0)
            {
                var chosenBranch = singleGameInstallData.downloadProperties.betaChannel;
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
                var gameSize = CalculateGameSize(singleGameInstallData);
                singleGameInstallData.downloadSizeNumber = gameSize.download_size;
                singleGameInstallData.installSizeNumber = gameSize.disk_size;
                CalculateTotalSize();
                InstallBtn.IsEnabled = true;
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            if (editDownloadPropertiesMode)
            {
                DownloadManagerData.Download installData = singleGameInstallData;
                installData.editDownloadPropertiesMode = null;
            }
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

        private void ExtraContentLB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (singleGameInstallData != null)
            {
                InstallBtn.IsEnabled = false;
                DownloadManagerData.Download installData = singleGameInstallData;
                var selectedDlcs = ExtraContentLB.SelectedItems.Cast<GogDownloadGameInfo.Dlc>();
                installData.downloadProperties.extraContent = new List<string>();
                foreach (var selectedDlc in selectedDlcs)
                {
                    installData.downloadProperties.extraContent.Add(selectedDlc.id);
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
                var gameSize = CalculateGameSize(installData);
                installData.downloadSizeNumber = gameSize.download_size;
                installData.installSizeNumber = gameSize.disk_size;
                CalculateTotalSize();
                InstallBtn.IsEnabled = true;
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            var downloadManager = GogOssLibrary.GetGogOssDownloadManager();
            var previouslySelected = downloadManager.DownloadsDG.SelectedIndex;
            var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == MultiInstallData[0].gameID);
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
            wantedItem.downloadProperties.betaChannel = MultiInstallData[0].downloadProperties.betaChannel;
            wantedItem.downloadProperties.buildId = MultiInstallData[0].downloadProperties.buildId;
            wantedItem.downloadProperties.version = MultiInstallData[0].downloadProperties.version;
            wantedItem.downloadProperties.language = MultiInstallData[0].downloadProperties.language;
            wantedItem.downloadProperties.extraContent = MultiInstallData[0].downloadProperties.extraContent;
            wantedItem.downloadSizeNumber = MultiInstallData[0].downloadSizeNumber;
            wantedItem.installSizeNumber = MultiInstallData[0].installSizeNumber;

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
                manifest = await Gogdl.GetGameInfo(selectedGame);
                var gameSize = CalculateGameSize(selectedGame);
                selectedGame.downloadSizeNumber = gameSize.download_size;
                selectedGame.installSizeNumber = gameSize.disk_size;
                CalculateTotalSize();
                InstallBtn.IsEnabled = true;
            }
        }
    }
}
