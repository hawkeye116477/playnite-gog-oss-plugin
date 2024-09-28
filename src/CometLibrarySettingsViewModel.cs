using System;
using System.Collections.Generic;
using Playnite.SDK;
using CometLibraryNS.Services;
using CometLibraryNS.Enums;

namespace CometLibraryNS
{
    public class CometLibrarySettings
    {
        public bool ImportInstalledGames { get; set; } = true;
        public bool ConnectAccount { get; set; } = false;
        public bool ImportUninstalledGames { get; set; } = false;
        public bool StartGamesUsingComet { get; set; } = false;
        public bool UseVerticalCovers { get; set; } = true;
        public string Locale { get; set; } = "en";
        public string GamesInstallationPath { get; set; } = "";
        public string SelectedCometPath { get; set; } = "";
        public string SelectedGogdlPath { get; set; } = "";
        public int MaxWorkers { get; set; } = 0;
        public bool UnattendedInstall { get; set; } = false;
        public bool DownloadAllDlcs { get; set; } = false;
        public bool DisplayDownloadSpeedInBits { get; set; } = false;
        public bool DisplayDownloadTaskFinishedNotifications { get; set; } = true;
        public DownloadCompleteAction DoActionAfterDownloadComplete { get; set; } = DownloadCompleteAction.Nothing;
    }
    public class CometLibrarySettingsViewModel : PluginSettingsViewModel<CometLibrarySettings, CometLibrary>
    {
        public CometLibrarySettingsViewModel(CometLibrary library, IPlayniteAPI api) : base(library, api)
        {
            Settings = LoadSavedSettings() ?? new CometLibrarySettings();
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
