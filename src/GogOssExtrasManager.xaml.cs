using CommonPlugin;
using Playnite.SDK.Models;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using GogOssLibraryNS.Services;
using System.IO;
using GogOssLibraryNS.Models;
using Playnite.SDK.Data;
using Playnite.SDK;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CommonPlugin.Enums;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Logika interakcji dla klasy GogOssExtrasManager.xaml
    /// </summary>
    public partial class GogOssExtrasManager : UserControl
    {
        private Game Game;
        private IPlayniteAPI playniteAPI = API.Instance;
        public Window GogOssExtrasManagerWindow => Window.GetWindow(this);
        public long availableFreeSpace;
        public double downloadSizeNumber;

        public GogOssExtrasManager()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            downloadSizeNumber = 0;
            SelectedExtrasPathTxt.Text = GogOss.ExtrasInstallationPath;
            UpdateSpaceInfo(GogOss.ExtrasInstallationPath);
            Game = DataContext as Game;
            CommonHelpers.SetControlBackground(this);
            await RefreshAll();
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
            double afterInstallSizeNumber = (double)(availableFreeSpace - downloadSizeNumber);
            if (afterInstallSizeNumber < 0)
            {
                afterInstallSizeNumber = 0;
            }
            AfterInstallingTB.Text = CommonHelpers.FormatSize(afterInstallSizeNumber);
        }

        private async Task RefreshAll()
        {
            AvailableExtrasLB.Visibility = Visibility.Collapsed;
            BottomADGrd.Visibility = Visibility.Collapsed;
            ReloadABtn.IsEnabled = false;
            LoadingATB.Visibility = Visibility.Visible;
            NoExtrasATB.Visibility = Visibility.Collapsed;

            var gogExtras = await GogOss.GetExtras(Game.GameId);
            if (gogExtras.Count > 0)
            {
                foreach (var extra in gogExtras)
                {
                    extra.Name = char.ToUpper(extra.Name[0]) + extra.Name[1..];
                }
            }
            if (gogExtras.Count > 0)
            {
                AvailableExtrasLB.ItemsSource = gogExtras;
                AvailableExtrasLB.Visibility = Visibility.Visible;
                BottomADGrd.Visibility = Visibility.Visible;
                DownloadBtn.IsEnabled = true;
            }
            else
            {
                NoExtrasATB.Visibility = Visibility.Visible;
            }
            LoadingATB.Visibility = Visibility.Collapsed;
            ReloadABtn.IsEnabled = true;
        }

        private void AvailableExtrasLB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItems = AvailableExtrasLB.SelectedItems.Cast<Extra>().ToList();
            double fullDownloadSize = 0;
            foreach (var selectedItem in selectedItems)
            {
                var selectedItemSize = Helpers.StringSizeToBytes(selectedItem.Size);
                fullDownloadSize += selectedItemSize;
            }
            downloadSizeNumber = fullDownloadSize;
            var downloadSize = CommonHelpers.FormatSize(fullDownloadSize);
            DownloadSizeTB.Text = downloadSize;
            UpdateAfterInstallingSize();
        }

        private async void ReloadABtn_Click(object sender, RoutedEventArgs e)
        {
            var result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonReloadConfirm), LocalizationManager.Instance.GetString(LOC.CommonReload), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                DownloadBtn.IsEnabled = false;
                DownloadSizeTB.Text = LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteLoadingLabel);
                var dataDir = GogOssLibrary.Instance.GetPluginUserDataPath();
                var cacheDir = Path.Combine(dataDir, "cache", "extras");
                foreach (var file in Directory.GetFiles(cacheDir, "*", SearchOption.AllDirectories))
                {
                    if (file.Contains(Game.GameId))
                    {
                        File.Delete(file);
                    }
                }
                await RefreshAll();
            }
        }

        private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableExtrasLB.SelectedItems.Count > 0)
            {
                var selectedItems = AvailableExtrasLB.SelectedItems.Cast<Extra>().ToList();
                var settings = GogOssLibrary.GetSettings();
                int maxWorkers = settings.MaxWorkers;

                GogOssExtrasManagerWindow.Close();
                GogOssDownloadManagerView downloadManager = GogOssLibrary.GetGogOssDownloadManager();

                var newInstallPath = SelectedExtrasPathTxt.Text;
                if (newInstallPath == "")
                {
                    newInstallPath = GogOss.ExtrasInstallationPath;
                }
                var playniteDirectoryVariable = ExpandableVariables.PlayniteDirectory.ToString();
                if (newInstallPath.Contains(playniteDirectoryVariable))
                {
                    newInstallPath = newInstallPath.Replace(playniteDirectoryVariable, playniteAPI.Paths.ApplicationPath);
                }

                var tasks = new List<DownloadManagerData.Download>();
                var gogDownloadApi = new GogDownloadApi();
                var gameInstallData = new DownloadManagerData.Download
                {
                    gameID = Game.GameId,
                    name = $"{Game.Name.RemoveTrademarks()}",
                };

                var gameManifest = await gogDownloadApi.GetGameMetaManifest(gameInstallData);
                foreach (var selectedItem in selectedItems)
                {
                    var downloadTaskId = $"{Game.GameId}_{Regex.Match(selectedItem.ManualUrl, @"\d+$").Value}";
                    var downloadTask = new DownloadManagerData.Download
                    {
                        gameID = downloadTaskId,
                        name = $"{Game.Name.RemoveTrademarks()} - {selectedItem.Name.RemoveTrademarks()}",
                        downloadSizeNumber = Helpers.StringSizeToBytes(selectedItem.Size),
                        downloadItemType = Enums.DownloadItemType.Extra,
                        fullInstallPath = Path.Combine(newInstallPath, $"{gameManifest.installDirectory}_Extras")
                    };
                    downloadTask.downloadProperties = new DownloadProperties()
                    {
                        maxWorkers = maxWorkers,
                        installPath = newInstallPath
                    };
                    var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == downloadTaskId);
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
                        tasks.Add(downloadTask);
                    }
                }
                if (tasks.Count > 0)
                {
                    await downloadManager.EnqueueMultipleJobs(tasks);
                }
            }
        }

        private void ChooseExtrasPathBtn_Click(object sender, RoutedEventArgs e)
        {
            var path = playniteAPI.Dialogs.SelectFolder();
            if (path != "")
            {
                SelectedExtrasPathTxt.Text = path;
                UpdateSpaceInfo(path);
            }
        }

        private void SelectAllExtrasBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableExtrasLB.Items.Count > 0)
            {
                if (AvailableExtrasLB.Items.Count == AvailableExtrasLB.SelectedItems.Count)
                {
                    AvailableExtrasLB.UnselectAll();
                }
                else
                {
                    AvailableExtrasLB.SelectAll();
                }
            }
        }
    }
}
