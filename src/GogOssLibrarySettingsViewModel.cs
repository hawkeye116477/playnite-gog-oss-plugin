using System;
using System.Collections.Generic;
using Playnite.SDK;
using GogOssLibraryNS.Services;
using GogOssLibraryNS.Enums;

namespace GogOssLibraryNS
{
    public class GogOssLibrarySettings
    {
        public bool ImportInstalledGames { get; set; } = true;
        public bool ConnectAccount { get; set; } = false;
        public bool ImportUninstalledGames { get; set; } = false;
        public bool EnableCometSupport { get; set; } = Comet.IsInstalled;
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
    public class GogOssLibrarySettingsViewModel : PluginSettingsViewModel<GogOssLibrarySettings, GogOssLibrary>
    {
        public GogOssLibrarySettingsViewModel(GogOssLibrary library, IPlayniteAPI api) : base(library, api)
        {
            Settings = LoadSavedSettings() ?? new GogOssLibrarySettings();
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
