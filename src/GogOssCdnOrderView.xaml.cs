using CommonPlugin;
using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Interaction logic for GogOssCdnOrderView.xaml
    /// </summary>
    public partial class GogOssCdnOrderView : UserControl
    {
        public List<string> CdnOrder { get; private set; } = new List<string>();
        private IPlayniteAPI playniteApi = API.Instance;

        public GogOssCdnOrderView()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            var games = playniteApi.Database.Games.Where(i => i.PluginId == GogOssLibrary.Instance.Id).OrderBy(g => g.Name).ToList();
            GameCBo.ItemsSource = games;
            CdnSP.Visibility = Visibility.Collapsed;
            var globalSettings = GogOssLibrary.GetSettings();
            if (globalSettings.CdnOrder?.Count > 0)
            {
                CdnLB.ItemsSource = globalSettings.CdnOrder;
                CdnSP.Visibility = Visibility.Visible;
                ClearBtn.IsEnabled = true;
            }
        }

        private void GameCBo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedValue = GameCBo.SelectedItem;
            if (selectedValue != null)
            {
                ShowAllBtn.IsEnabled = true;
            }
            else
            {
                ShowAllBtn.IsEnabled = false;
            }
        }

        private void ShowAllBtn_Click(object sender, RoutedEventArgs e)
        {
            GlobalProgressOptions metadataProgressOptions = new(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteProgressMetadata), false);
            var playniteAPI = API.Instance;
            playniteAPI.Dialogs.ActivateGlobalProgress((a) =>
            {
                _ = (Application.Current.Dispatcher?.BeginInvoke((Action)async delegate
                {
                    CdnSP.Visibility = Visibility.Collapsed;
                    var taskData = new DownloadManagerData.Download();
                    var selectedGame = GameCBo.SelectedItem as Playnite.SDK.Models.Game;
                    taskData.gameID = selectedGame.GameId;
                    taskData.downloadItemType = DownloadItemType.Game;
                    GogDownloadApi gogDownloadApi = new();
                    var cdns = await gogDownloadApi.GetSecureLinks(taskData);

                    var finalCdns = new ObservableCollection<string>();
                    foreach (var cdn in cdns.Distinct())
                    {
                        finalCdns.Add(cdn.endpoint_name);
                    }
                    CdnLB.ItemsSource = finalCdns;
                    CdnSP.Visibility = Visibility.Visible;
                    ClearBtn.IsEnabled = true;
                }));
            }, metadataProgressOptions);

        }

        private void MoveDownBtn_Click(object sender, RoutedEventArgs e)
        {
            var cdnItems = (ObservableCollection<string>)CdnLB.ItemsSource;
            int selectedIndex = CdnLB.SelectedIndex;
            if (selectedIndex < cdnItems.Count - 1 & selectedIndex != -1)
            {
                cdnItems.Move(selectedIndex, selectedIndex + 1);
                CdnLB.SelectedIndex = selectedIndex + 1;
            }
        }

        private void MoveUpBtn_Click(object sender, RoutedEventArgs e)
        {
            var cdnItems = (ObservableCollection<string>)CdnLB.ItemsSource;
            int selectedIndex = CdnLB.SelectedIndex;
            if (selectedIndex > 0)
            {
                cdnItems.Move(selectedIndex, selectedIndex - 1);
                CdnLB.SelectedIndex = selectedIndex - 1;
            }
        }

        private void ApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            CdnOrder = new List<string>();
            var cdnItems = (ObservableCollection<string>)CdnLB.ItemsSource;
            if (cdnItems.Count > 0)
            {
                foreach (var cdnItem in cdnItems)
                {
                    CdnOrder.Add(cdnItem);
                }
            }
            var thisWindow = Window.GetWindow(this);
            thisWindow.DialogResult = true;
            Window.GetWindow(this).Close();
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            CdnLB.ItemsSource = new ObservableCollection<string>();
        }

        private void CdnLB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int selectedIndex = CdnLB.SelectedIndex;
            if (selectedIndex >= 0)
            {
                MoveUpBtn.IsEnabled = true;
                MoveDownBtn.IsEnabled = true;
            }
            else
            {
                MoveUpBtn.IsEnabled = false;
                MoveDownBtn.IsEnabled = false;
            }
        }
    }
}
