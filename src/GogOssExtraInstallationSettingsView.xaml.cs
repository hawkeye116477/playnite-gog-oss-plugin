using CommonPlugin;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Interaction logic for GogOssExtraInstallationSettingsView.xaml
    /// </summary>
    public partial class GogOssExtraInstallationSettingsView : UserControl
    {
        public GogDownloadApi gogDownloadApi = new();
        public GogOssExtraInstallationSettingsView()
        {
            InitializeComponent();
        }

        private DownloadManagerData.Download ChosenGame
        {
            get => DataContext as DownloadManagerData.Download;
            set { }
        }

        public GogGameMetaManifest manifest;
        public GogBuildsData buildsManifest;
        private IPlayniteAPI playniteAPI = API.Instance;
        private bool uncheckedByUser = true;
        private bool checkedByUser = true;

        private async void GogOssExtraInstallationSettingsUC_Loaded(object sender, RoutedEventArgs e)
        {
            CommonHelpers.SetControlBackground(this);
            manifest = await gogDownloadApi.GetGameMetaManifest(ChosenGame);
            buildsManifest = await gogDownloadApi.GetProductBuilds(ChosenGame);
            var betaChannels = new Dictionary<string, string>();
            if (buildsManifest.available_branches.Count > 1)
            {
                foreach (var branch in buildsManifest.available_branches)
                {
                    if (branch == null)
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
                    BetaChannelCBo.SelectedValue = ChosenGame.downloadProperties.betaChannel;
                    BetaChannelSP.Visibility = Visibility.Visible;
                }
            }
            await RefreshVersions();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).Close();
        }

        private async Task RefreshVersions()
        {
            VersionSP.Visibility = Visibility.Collapsed;
            var builds = buildsManifest.items;
            var gameVersions = new Dictionary<string, string>();
            if (builds.Count > 0)
            {
                var chosenBranch = ChosenGame.downloadProperties.betaChannel;
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
                var selectedVersion = ChosenGame.downloadProperties.buildId;
                if (selectedVersion.IsNullOrEmpty() || !gameVersions.ContainsKey(selectedVersion))
                {
                    selectedVersion = gameVersions.FirstOrDefault().Key;
                }
                GameVersionCBo.SelectedItem = gameVersions.First(i => i.Key == selectedVersion);
                manifest = await gogDownloadApi.GetGameMetaManifest(ChosenGame);
                if (gameVersions.Count > 1)
                {
                    VersionSP.Visibility = Visibility.Visible;
                }
            }
            await SetGameVersion();
        }

        private void RefreshLanguages()
        {
            var languages = manifest.languages;
            var currentPlayniteLanguage = playniteAPI.ApplicationSettings.Language.Replace("_", "-");
            var selectedLanguage = "";
            if (languages.Count > 1)
            {
                var gameLanguages = new Dictionary<string, string>();
                foreach (var language in languages)
                {
                    gameLanguages.Add(language, new CultureInfo(language).NativeName);
                }
                gameLanguages = gameLanguages.OrderBy(x => x.Value).ToDictionary(x => x.Key, x => x.Value);
                if (!ChosenGame.downloadProperties.language.IsNullOrEmpty() && gameLanguages.ContainsKey(ChosenGame.downloadProperties.language))
                {
                    selectedLanguage = ChosenGame.downloadProperties.language;
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
                GameLanguageCBo.ItemsSource = gameLanguages;
                GameLanguageCBo.SelectedValue = selectedLanguage;
                LanguageSP.Visibility = Visibility.Visible;
            }
            else
            {
                LanguageSP.Visibility = Visibility.Collapsed;
                ChosenGame.downloadProperties.language = "";
            }
        }

        public async Task SetGameVersion()
        {
            KeyValuePair<string, string> selectedVersion = (KeyValuePair<string, string>)GameVersionCBo.SelectedItem;
            ChosenGame.downloadProperties.buildId = selectedVersion.Key;
            ChosenGame.downloadProperties.version = selectedVersion.Value.Split('—')[0].Trim();
            manifest = await gogDownloadApi.GetGameMetaManifest(ChosenGame);
            RefreshLanguages();
            if (manifest.dlcs.Count > 0)
            {
                var settings = GogOssLibrary.GetSettings();
                ExtraContentLB.ItemsSource = manifest.dlcs;
                ExtraContentBrd.Visibility = Visibility.Visible;
                if (ChosenGame.downloadProperties.extraContent.Count > 0)
                {
                    foreach (var selectedDlc in ChosenGame.downloadProperties.extraContent)
                    {
                        var selectedDlcItem = manifest.dlcs.FirstOrDefault(d => d.Key == selectedDlc);
                        if (selectedDlcItem.Key != null)
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

        private async void GameVersionCBo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GameVersionCBo.IsDropDownOpen)
            {
                await SetGameVersion();
            }
        }

        private async void BetaChannelCBo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BetaChannelCBo.IsDropDownOpen)
            {
                if (BetaChannelCBo.SelectedValue != null)
                {
                    ChosenGame.downloadProperties.betaChannel = BetaChannelCBo.SelectedValue.ToString();
                    await RefreshVersions();
                }
            }
        }

        private void ExtraContentLB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedDlcs = ExtraContentLB.SelectedItems.Cast<GogDownloadGameInfo.Dlc>();
            ChosenGame.downloadProperties.extraContent = new List<string>();
            foreach (var selectedDlc in selectedDlcs)
            {
                ChosenGame.downloadProperties.extraContent.Add(selectedDlc.id);
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
    }
}
