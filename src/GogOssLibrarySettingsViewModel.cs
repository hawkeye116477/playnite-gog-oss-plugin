using System.Collections.Generic;
using Playnite.SDK;
using GogOssLibraryNS.Enums;
using CommonPlugin.Enums;
using System.IO;
using System;
using Playnite.SDK.Data;
using Tomlet;
using Tomlet.Attributes;
using System.Linq;

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
        public string SelectedXdeltaPath { get; set; } = "";
        public int MaxWorkers { get; set; } = 0;
        public bool UnattendedInstall { get; set; } = false;
        public bool DownloadAllDlcs { get; set; } = false;
        public bool DisplayDownloadSpeedInBits { get; set; } = false;
        public bool DisplayDownloadTaskFinishedNotifications { get; set; } = true;
        public DownloadCompleteAction DoActionAfterDownloadComplete { get; set; } = DownloadCompleteAction.Nothing;
        public UpdatePolicy GamesUpdatePolicy { get; set; } = UpdatePolicy.Month;
        public long NextGamesUpdateTime { get; set; } = 0;
        public bool AutoUpdateGames { get; set; } = false;
        public UpdatePolicy CometUpdatePolicy { get; set; } = UpdatePolicy.Month;
        public long NextCometUpdateTime { get; set; } = 0;
        public bool SyncPlaytime { get; set; } = GogOss.DefaultPlaytimeSyncEnabled;
        public ClearCacheTime AutoRemoveCompletedDownloads { get; set; } = ClearCacheTime.Never;
        public ClearCacheTime AutoClearCache { get; set; } = ClearCacheTime.Never;
        public long NextClearingTime { get; set; } = 0;
        public long NextRemovingCompletedDownloadsTime { get; set; } = 0;
        public bool SyncGameSaves { get; set; } = false;
        public GogCdn PreferredCdn { get; set; } = GogCdn.Fastly;
        public bool EnableOverlay { get; set; } = true;
    }


    public class GalaxyOverlaySettings
    {
        [TomlProperty("overlay")]
        public OverlaySettings Overlay { get; set; } = new();

        public class OverlaySettings
        {
            [TomlProperty("notification_volume")]
            public int NotificationVolume { get; set; } = 50;

            [TomlProperty("position")]
            public string Position { get; set; } = "bottom_right";

            [TomlProperty("notifications")]
            public Notifications Notifications { get; set; } = new();
        }

        public class Notifications
        {
            [TomlProperty("achievements")]
            public NotificationSettings Achievements { get; set; } = new();

            [TomlProperty("chat")]
            public NotificationSettings Chat { get; set; } = new();

            [TomlProperty("friend_online")]
            public NotificationSettings FriendOnline { get; set; } = new();

            [TomlProperty("friend_invite")]
            public NotificationSettings FriendInvite { get; set; } = new();

            [TomlProperty("friend_game_start")]
            public NotificationSettings FriendGameStart { get; set; } = new();

            [TomlProperty("game_invite")]
            public NotificationSettings GameInvite { get; set; } = new();

            [TomlNonSerialized]
            private NotificationSettings[] AllNotifications => new[] { Achievements, Chat, FriendOnline, FriendInvite, FriendGameStart, GameInvite };

            [TomlNonSerialized]
            public bool MasterSound
            {
                get
                {
                    return AllNotifications.Where(n => n.Enabled).All(n => n.Sound);
                }
                set
                {
                    foreach (var notificationType in AllNotifications)
                    {
                        if (notificationType.Enabled)
                        {
                            notificationType.Sound = value;
                        }
                    }
                }
            }
        }

        public class NotificationSettings
        {
            [TomlProperty("enabled")]
            public bool Enabled { get; set; } = true;

            [TomlProperty("sound")]
            public bool Sound { get; set; } = false;
        }
    }

    public class GogOssLibrarySettingsViewModel : PluginSettingsViewModel<GogOssLibrarySettings, GogOssLibrary>
    {
        public GalaxyOverlaySettings GalaxyOverlaySettings { get; set; }
        public GogOssLibrarySettingsViewModel(GogOssLibrary library, IPlayniteAPI api) : base(library, api)
        {
            Settings = LoadSavedSettings() ?? new GogOssLibrarySettings();
            GalaxyOverlaySettings = new GalaxyOverlaySettings();
            var overlayConfigFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "comet", "config.toml");
            if (File.Exists(overlayConfigFilePath))
            {
                var content = File.ReadAllText(overlayConfigFilePath);
                if (!content.IsNullOrEmpty())
                {
                    try
                    {
                        GalaxyOverlaySettings = TomletMain.To<GalaxyOverlaySettings>(content);
                    }
                    catch
                    {
                        GalaxyOverlaySettings = new GalaxyOverlaySettings();
                    }
                }
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

        public override void EndEdit()
        {
            if (EditingClone.AutoClearCache != Settings.AutoClearCache)
            {
                if (Settings.AutoClearCache != ClearCacheTime.Never)
                {
                    Settings.NextClearingTime = GogOssLibrary.GetNextClearingTime(Settings.AutoClearCache);
                }
                else
                {
                    Settings.NextClearingTime = 0;
                }
            }
            if (EditingClone.AutoRemoveCompletedDownloads != Settings.AutoRemoveCompletedDownloads)
            {
                if (Settings.AutoRemoveCompletedDownloads != ClearCacheTime.Never)
                {
                    Settings.NextRemovingCompletedDownloadsTime = GogOssLibrary.GetNextClearingTime(Settings.AutoRemoveCompletedDownloads);
                }
                else
                {
                    Settings.NextRemovingCompletedDownloadsTime = 0;
                }
            }
            if (EditingClone.GamesUpdatePolicy != Settings.GamesUpdatePolicy)
            {
                if (Settings.GamesUpdatePolicy != UpdatePolicy.Never)
                {
                    Settings.NextGamesUpdateTime = GogOssLibrary.GetNextUpdateCheckTime(Settings.GamesUpdatePolicy);
                }
                else
                {
                    Settings.NextGamesUpdateTime = 0;
                }
            }
            if (EditingClone.CometUpdatePolicy != Settings.CometUpdatePolicy)
            {
                if (Settings.CometUpdatePolicy != UpdatePolicy.Never)
                {
                    Settings.NextCometUpdateTime = GogOssLibrary.GetNextUpdateCheckTime(Settings.CometUpdatePolicy);
                }
                else
                {
                    Settings.NextCometUpdateTime = 0;
                }
            }

            var overlayConfigDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "comet");
            var overlayConfigFilePath = Path.Combine(overlayConfigDirectory, "config.toml");
            if (!Directory.Exists(overlayConfigDirectory))
            {
                Directory.CreateDirectory(overlayConfigDirectory);
            }
           
            File.WriteAllText(overlayConfigFilePath, TomletMain.TomlStringFrom(GalaxyOverlaySettings));
            base.EndEdit();
        }
    }
}
