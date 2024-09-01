using System;
using System.Collections.Generic;
using Playnite.SDK;
using CometLibraryNS.Services;

namespace CometLibraryNS
{
    public class CometLibrarySettings
    {
        public bool ImportInstalledGames { get; set; } = true;
        public bool ConnectAccount { get; set; } = false;
        public bool ImportUninstalledGames { get; set; } = false;
        public bool StartGamesUsingComet { get; set; } = false;
        public bool UseAutomaticGameInstalls { get; set; } = false;
        public bool UseVerticalCovers { get; set; } = true;
        public string Locale { get; set; } = "en";
        public string GamesInstallationPath { get; set; } = "";
        public string SelectedLauncherPath { get; set; } = "";
    }
    public class CometLibrarySettingsViewModel : PluginSettingsViewModel<CometLibrarySettings, CometLibrary>
    {
        public bool IsUserLoggedIn
        {
            get
            {
                using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                {
                    var api = new GogAccountClient(view);
                    return api.GetIsUserLoggedIn();
                }
            }
        }

        public RelayCommand<object> LoginCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                Login();
            });
        }

        public CometLibrarySettingsViewModel(CometLibrary library, IPlayniteAPI api) : base(library, api)
        {
            Settings = LoadSavedSettings() ?? new CometLibrarySettings();
        }

        private void Login()
        {
            try
            {
                using (var view = PlayniteApi.WebViews.CreateView(500, 500))
                using (var backgroundView = PlayniteApi.WebViews.CreateOffscreenView())
                {
                    var api = new GogAccountClient(view);
                    api.Login(backgroundView);
                }

                OnPropertyChanged(nameof(IsUserLoggedIn));
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to authenticate user.");
            }
        }

        public Dictionary<string, string> Languages { get; } = new Dictionary<string, string>
        {
            {"en", "English" },
            {"de", "Deutsch" },
            {"fr", "Français" },
            {"pl", "Polski" },
            {"ru", "Pусский" },
            {"zh", "中文(简体)" },
        };
    }
}
