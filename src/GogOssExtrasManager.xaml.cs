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
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Playnite.Common;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Logika interakcji dla klasy GogOssExtrasManager.xaml
    /// </summary>
    public partial class GogOssExtrasManager : UserControl
    {
        private Game Game;
        private IPlayniteAPI playniteAPI = API.Instance;

        public GogOssExtrasManager()
        {
            InitializeComponent();
        }

        private async void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            Game = DataContext as Game;
            CommonHelpers.SetControlBackground(this);
            await RefreshAll();
        }

        private async Task RefreshAll()
        {
            AvailableExtrasLB.Visibility = Visibility.Collapsed;
            BottomADGrd.Visibility = Visibility.Collapsed;
            ReloadABtn.IsEnabled = false;
            LoadingATB.Visibility = Visibility.Visible;
            NoExtrasATB.Visibility = Visibility.Collapsed;

            var dataDir = GogOssLibrary.Instance.GetPluginUserDataPath();
            LibraryGameDetailsResponse gameDetailsInfo = new();
            var extrasDir = Path.Combine(dataDir, "cache", "extras");
            var extrasFilePath = Path.Combine(extrasDir, $"{Game.GameId}.json");
            Directory.CreateDirectory(extrasDir);
            bool correctJson = false;
            if (File.Exists(extrasFilePath))
            {
                var extrasFileContent = File.ReadAllText(extrasFilePath);
                if (extrasFileContent != null)
                {
                    if (Serialization.TryFromJson(extrasFileContent, out LibraryGameDetailsResponse newGameDetailsInfo))
                    {
                        gameDetailsInfo = newGameDetailsInfo;
                        correctJson = true;
                    }
                }
            }
            if (!correctJson)
            {
                var gogApi = new GogAccountClient();
                gameDetailsInfo = await gogApi.GetOwnedGameDetails(Game.GameId);
                File.WriteAllText(extrasFilePath, Serialization.ToJson(gameDetailsInfo));
            }

            var gogExtras = gameDetailsInfo.Extras;
            if (gogExtras.Count > 0)
            {
                var dlcs = gameDetailsInfo.Dlcs;
                if (dlcs.Count > 0)
                {
                    foreach (var dlc in dlcs)
                    {
                        gogExtras.AddRange(dlc.Extras);
                    }
                }
                foreach (var extra in gogExtras)
                {
                    extra.Name = char.ToUpper(extra.Name[0]) + extra.Name[1..];
                }
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
            var downloadSize = CommonHelpers.FormatSize(fullDownloadSize);
            DownloadSizeTB.Text = downloadSize;
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

        private void DownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableExtrasLB.SelectedItems.Count > 0)
            {
                var selectedItems = AvailableExtrasLB.SelectedItems.Cast<Extra>().ToList();
                foreach (var selectedItem in selectedItems)
                {
                    ProcessStarter.StartUrl($"https://www.gog.com{selectedItem.ManualUrl}");
                }
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).Close();
        }
    }
}
