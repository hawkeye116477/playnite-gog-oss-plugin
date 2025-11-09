using CommonPlugin;
using CommonPlugin.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Linguini.Shared.Types.Bundle;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Interaction logic for GogOssDlcManager.xaml
    /// </summary>
    public partial class GogOssDlcManager : UserControl
    {
        private Game Game;
        private string GameId;
        public Window DlcManagerWindow => Window.GetWindow(this);
        public ObservableCollection<KeyValuePair<string, Game>> installedDLCs;
        public ObservableCollection<KeyValuePair<string, Game>> notInstalledDLCs;
        public long availableFreeSpace;
        public DownloadManagerData.Download downloadTask;
        public GogDownloadApi gogDownloadApi = new();

        public GogOssDlcManager()
        {
            InitializeComponent();
        }

        private async void InstallBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableDlcsLB.SelectedItems.Count > 0)
            {
                var settings = GogOssLibrary.GetSettings();
                int maxWorkers = settings.MaxWorkers;
                if (MaxWorkersNI.Value != "")
                {
                    maxWorkers = int.Parse(MaxWorkersNI.Value);
                }
                DlcManagerWindow.Close();
                GogOssDownloadManagerView downloadManager = GogOssLibrary.GetGogOssDownloadManager();

                var tasks = new List<DownloadManagerData.Download>();
                downloadTask = new DownloadManagerData.Download
                {
                    gameID = Game.GameId,
                    name = Game.Name,
                };
                var installedGameInfo = GogOss.GetInstalledInfo(GameId);
                downloadTask.fullInstallPath = installedGameInfo.install_path;
                downloadTask.downloadProperties = new DownloadProperties()
                {
                    buildId = installedGameInfo.build_id,
                    extraContent = installedGameInfo.installed_DLCs,
                    language = installedGameInfo.language,
                    version = installedGameInfo.version,
                    maxWorkers = maxWorkers,
                };
                var manifest = await gogDownloadApi.GetGameMetaManifest(downloadTask);
                downloadTask.downloadProperties.installPath = Path.Combine(installedGameInfo.install_path.Replace(manifest.installDirectory, ""));

                foreach (var selectedOption in AvailableDlcsLB.SelectedItems.Cast<KeyValuePair<string, Game>>())
                {
                    downloadTask.downloadProperties.extraContent.Add(selectedOption.Key);
                }

                var dlcsSize = await CalculateDlcsSize();
                downloadTask.downloadSizeNumber = dlcsSize.download_size;
                downloadTask.installSizeNumber = dlcsSize.disk_size;

                tasks.Add(downloadTask);
                if (tasks.Count > 0)
                {
                    var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == Game.GameId);
                    if (wantedItem != null)
                    {
                        if (wantedItem.status != DownloadStatus.Running)
                        {
                            downloadManager.downloadManagerData.downloads.Remove(wantedItem);
                            downloadManager.downloadsChanged = true;
                            wantedItem = null;
                        }
                    }
                    if (wantedItem == null)
                    {
                        await downloadManager.EnqueueMultipleJobs(tasks);
                    }
                }
            }
        }

        private async Task<GogGameMetaManifest.SizeType> CalculateDlcsSize()
        {
            var installedGameInfo = GogOss.GetInstalledInfo(GameId);
            var manifest = await gogDownloadApi.GetGameMetaManifest(GameId, installedGameInfo);
            var size = new GogGameMetaManifest.SizeType
            {
                download_size = 0,
                disk_size = 0
            };
            var selectedLanguage = installedGameInfo.language;
            if (manifest.size.Count == 2)
            {
                selectedLanguage = manifest.size.ElementAt(1).Key.ToString();
            }
            var selectedDlcs = AvailableDlcsLB.SelectedItems.Cast<KeyValuePair<string, Game>>().ToDictionary(i => i.Key, i => i.Value);
            if (selectedDlcs.Count() > 0)
            {
                foreach (var dlc in manifest.dlcs.OrderBy(obj => obj.Value.title))
                {
                    if (selectedDlcs.ContainsKey(dlc.Key))
                    {
                        if (dlc.Value.size.ContainsKey("*"))
                        {
                            size.download_size += dlc.Value.size["*"].download_size;
                            size.disk_size += dlc.Value.size["*"].disk_size;
                        }
                        if (dlc.Value.size.ContainsKey(selectedLanguage))
                        {
                            size.download_size += dlc.Value.size[selectedLanguage].download_size;
                            size.disk_size += dlc.Value.size[selectedLanguage].disk_size;
                        }
                    }
                }
            }
            return size;
        }

        private void SelectAllAvDlcsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableDlcsLB.Items.Count > 0)
            {
                if (AvailableDlcsLB.Items.Count == AvailableDlcsLB.SelectedItems.Count)
                {
                    AvailableDlcsLB.UnselectAll();
                }
                else
                {
                    AvailableDlcsLB.SelectAll();
                }
            }
        }

        private async void AvailableDlcsLB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Game.InstallDirectory.IsNullOrEmpty())
            {
                return;
            }
            if (AvailableDlcsLB.SelectedIndex == -1)
            {
                InstallBtn.IsEnabled = false;
            }
            else
            {
                InstallBtn.IsEnabled = true;
            }
            var dlcsSize = await CalculateDlcsSize();
            var downloadSize = CommonHelpers.FormatSize(dlcsSize.download_size);
            DownloadSizeTB.Text = downloadSize;
            var installSize = CommonHelpers.FormatSize(dlcsSize.disk_size);
            InstallSizeTB.Text = installSize;
            double afterInstallSizeNumber = (double)(availableFreeSpace - dlcsSize.disk_size);
            AfterInstallingTB.Text = CommonHelpers.FormatSize(afterInstallSizeNumber);
        }

        private void UninstallBtn_Click(object sender, RoutedEventArgs e)
        {
            var playniteAPI = API.Instance;
            if (InstalledDlcsLB.SelectedItems.Count > 0)
            {
                MessageBoxResult result;
                if (InstalledDlcsLB.SelectedItems.Count == 1)
                {
                    var selectedDLC = (KeyValuePair<string, Game>)InstalledDlcsLB.SelectedItems[0];
                    result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonUninstallGameConfirm, new Dictionary<string, IFluentType> { ["gameTitle"] = (FluentString)selectedDLC.Value.Name }),
                                                             LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUninstallGame),
                                                             MessageBoxButton.YesNo,
                                                             MessageBoxImage.Question);
                }
                else
                {
                    result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonUninstallSelectedDlcs),
                                                             LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUninstallGame),
                                                             MessageBoxButton.YesNo,
                                                             MessageBoxImage.Question);
                }

                if (result == MessageBoxResult.Yes)
                {
                    var globalProgressOptions = new GlobalProgressOptions($"{LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUninstalling)}... ", false);

                    var selectedKeys = new HashSet<string>(InstalledDlcsLB.SelectedItems
                        .OfType<KeyValuePair<string, Game>>()
                        .Select(s => s.Key));

                    playniteAPI.Dialogs.ActivateGlobalProgress(async (a) =>
                    {
                        a.IsIndeterminate = true;
                        var installedInfo = GogOss.GetInstalledInfo(GameId);
                        var bigDepot = await GogOss.GetInstalledBigDepot(installedInfo, GameId);
                        if (bigDepot != null)
                        {
                            var options = new ParallelOptions
                            {
                                MaxDegreeOfParallelism = CommonHelpers.CpuThreadsNumber - 1
                            };

                            var entries = bigDepot.items.Select(i => (i.product_id, i.path))
                                                        .Concat(bigDepot.files.Select(f => (f.product_id, f.path)))
                                                        .Where(e => selectedKeys.Contains(e.product_id))
                                                        .ToList();
                            Parallel.ForEach(entries, options, e =>
                            {
                                var pathToRemove = Path.Combine(installedInfo.install_path, e.path.TrimStart('/', '\\'));
                                if (File.Exists(pathToRemove))
                                {
                                    File.Delete(pathToRemove);
                                }
                            });
                            foreach (var selectedKey in selectedKeys)
                            {
                                installedInfo.installed_DLCs.Remove(selectedKey);
                            }
                            var installedAppList = GogOssLibrary.GetInstalledAppList();
                            if (installedAppList.ContainsKey(GameId))
                            {
                                installedAppList.Remove(GameId);
                            }
                            installedAppList.Add(GameId, installedInfo);
                            GogOssLibrary.Instance.installedAppListModified = true;
                            GogOss.AddToHeroicInstalledList(installedInfo, GameId);     
                        }
                    }, globalProgressOptions);
                    foreach (var selectedKey in selectedKeys)
                    {
                        var selectedDlcKvp = installedDLCs.FirstOrDefault(kvp => kvp.Key == selectedKey);
                        installedDLCs.Remove(selectedDlcKvp);
                        notInstalledDLCs.Add(selectedDlcKvp);
                    }
                    if (AvailableDlcsLB.Items.Count > 0)
                    {
                        BottomADGrd.Visibility = Visibility.Visible;
                        TopADSP.Visibility = Visibility.Visible;
                        NoAvailableDlcsTB.Visibility = Visibility.Collapsed;
                    }
                    if (InstalledDlcsLB.Items.Count == 0)
                    {
                        InstalledDlcsSP.Visibility = Visibility.Collapsed;
                        NoInstalledDlcsTB.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private void SelectAllInDlcsBtn_Click_1(object sender, RoutedEventArgs e)
        {
            if (InstalledDlcsLB.Items.Count > 0)
            {
                if (InstalledDlcsLB.Items.Count == InstalledDlcsLB.SelectedItems.Count)
                {
                    InstalledDlcsLB.UnselectAll();
                }
                else
                {
                    InstalledDlcsLB.SelectAll();
                }
            }
        }

        private void InstalledDlcsLB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (InstalledDlcsLB.SelectedIndex == -1)
            {
                UninstallBtn.IsEnabled = false;
            }
            else
            {
                UninstallBtn.IsEnabled = true;
            }
        }

        private async void GogOssDlcManagerUC_Loaded(object sender, RoutedEventArgs e)
        {
            Game = DataContext as Game;
            GameId = Game.GameId;
            var playniteAPI = API.Instance;
            if (playniteAPI.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
            {
                CloseWindowTab.Visibility = Visibility.Visible;
            }
            CommonHelpers.SetControlBackground(this);
            BottomADGrd.Visibility = Visibility.Collapsed;
            TopADSP.Visibility = Visibility.Collapsed;
            InstalledDlcsSP.Visibility = Visibility.Collapsed;
            LoadingATB.Visibility = Visibility.Visible;
            LoadingITB.Visibility = Visibility.Visible;
            var installedGameInfo = GogOss.GetInstalledInfo(GameId);

            var gameInfo = await gogDownloadApi.GetGameMetaManifest(GameId, installedGameInfo);
            var ownedDlcs = gameInfo.dlcs;
            if (ownedDlcs.Count > 0)
            {
                var installedDlcsIds = installedGameInfo.installed_DLCs;
                installedDLCs = new ObservableCollection<KeyValuePair<string, Game>>();
                notInstalledDLCs = new ObservableCollection<KeyValuePair<string, Game>>();
                foreach (var ownedDlc in ownedDlcs.OrderBy(obj => obj.Value.title))
                {
                    if (!ownedDlc.Key.IsNullOrEmpty())
                    {
                        var dlcData = new Game
                        {
                            Name = ownedDlc.Value.title.RemoveTrademarks(),
                            GameId = ownedDlc.Key
                        };

                        if (installedDlcsIds.Count > 0 && installedDlcsIds.Contains(ownedDlc.Key))
                        {
                            installedDLCs.Add(new KeyValuePair<string, Game>(ownedDlc.Key, dlcData));
                        }
                        else
                        {
                            notInstalledDLCs.Add(new KeyValuePair<string, Game>(ownedDlc.Key, dlcData));
                        }
                    }
                }
                InstalledDlcsLB.ItemsSource = installedDLCs;
                AvailableDlcsLB.ItemsSource = notInstalledDLCs;
                if (!Game.InstallDirectory.IsNullOrEmpty())
                {
                    DriveInfo dDrive = new DriveInfo(Path.GetFullPath(Game.InstallDirectory));
                    if (dDrive.IsReady)
                    {
                        availableFreeSpace = dDrive.AvailableFreeSpace;
                        SpaceTB.Text = CommonHelpers.FormatSize(availableFreeSpace);
                        AfterInstallingTB.Text = CommonHelpers.FormatSize(availableFreeSpace);
                    }
                }
                var settings = GogOssLibrary.GetSettings();
                MaxWorkersNI.Value = settings.MaxWorkers.ToString();
                if (InstalledDlcsLB.Items.Count == 0)
                {
                    InstalledDlcsSP.Visibility = Visibility.Collapsed;
                    NoInstalledDlcsTB.Visibility = Visibility.Visible;
                }
                if (AvailableDlcsLB.Items.Count == 0)
                {
                    BottomADGrd.Visibility = Visibility.Collapsed;
                    TopADSP.Visibility = Visibility.Collapsed;
                    NoAvailableDlcsTB.Visibility = Visibility.Visible;
                }
            }
            else
            {
                NoAvailableDlcsTB.Visibility = Visibility.Visible;
                BottomADGrd.Visibility = Visibility.Collapsed;
                TopADSP.Visibility = Visibility.Collapsed;
                InstalledDlcsTbI.Visibility = Visibility.Collapsed;
            }
            LoadingATB.Visibility = Visibility.Collapsed;
            LoadingITB.Visibility = Visibility.Collapsed;
            if (InstalledDlcsLB.Items.Count > 0)
            {
                InstalledDlcsSP.Visibility = Visibility.Visible;
            }
            if (AvailableDlcsLB.Items.Count > 0)
            {
                BottomADGrd.Visibility = Visibility.Visible;
                TopADSP.Visibility = Visibility.Visible;
            }
            if (Game.InstallDirectory.IsNullOrEmpty())
            {
                AvailableDlcsActionSP.Visibility = Visibility.Collapsed;
                BottomADGrd.Visibility = Visibility.Collapsed;
                AvailableDlcsAOBrd.Visibility = Visibility.Collapsed;
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl tabControl)
            {
                if (tabControl.SelectedItem == CloseWindowTab)
                {
                    Window.GetWindow(this).Close();
                }
            }

        }
    }
}
