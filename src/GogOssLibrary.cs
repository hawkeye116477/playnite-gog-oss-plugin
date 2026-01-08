using CommonPlugin;
using CommonPlugin.Enums;
using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Linguini.Shared.Types.Bundle;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace GogOssLibraryNS
{
    [LoadPlugin]
    public class GogOssLibrary : LibraryPluginBase<GogOssLibrarySettingsViewModel>
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public static GogOssLibrary Instance { get; set; }
        private GogOssDownloadManagerView GogOssDownloadManagerView;
        private SidebarItem downloadManagerSidebarItem;
        public Dictionary<string, Installed> installedAppList { get; set; }
        public bool installedAppListModified { get; set; } = false;
        public CommonHelpers commonHelpers { get; set; }
        public GogDownloadApi gogDownloadApi = new();

        public GogOssLibrary(IPlayniteAPI api) : base(
            "GOG OSS",
            Guid.Parse("03689811-3F33-4DFB-A121-2EE168FB9A5C"),
            new LibraryPluginProperties { CanShutdownClient = false, HasSettings = true },
            default,
            GogOss.Icon,
            (_) => new GogOssLibrarySettingsView(),
            api)
        {
            Instance = this;
            commonHelpers = new CommonHelpers(Instance);
            SettingsViewModel = new GogOssLibrarySettingsViewModel(this, api);
            LoadExtraLocalization();
            commonHelpers.LoadNeededResources();
            GogOssDownloadManagerView = new GogOssDownloadManagerView();
        }

        public static SidebarItem GetPanel()
        {
            if (Instance.downloadManagerSidebarItem == null)
            {
                Instance.downloadManagerSidebarItem = new SidebarItem
                {
                    Title = LocalizationManager.Instance.GetString(LOC.CommonPanel),
                    Icon = GogOss.Icon,
                    Type = SiderbarItemType.View,
                    Opened = () => GetGogOssDownloadManager(),
                    ProgressValue = 0,
                    ProgressMaximum = 100,
                };
            }
            return Instance.downloadManagerSidebarItem;
        }

        public static GogOssDownloadManagerView GetGogOssDownloadManager()
        {
            return Instance.GogOssDownloadManagerView;
        }

        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            yield return new GogOssInstallController(args.Game, this);
        }

        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            yield return new GogOssUninstallController(args.Game);
        }

        public override LibraryMetadataProvider GetMetadataDownloader()
        {
            return new GogMetadataProvider(PlayniteApi, SettingsViewModel.Settings);
        }

        public override IEnumerable<PlayController> GetPlayActions(GetPlayActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            if (!GetInstalledAppList().ContainsKey(args.Game.GameId))
            {
                yield break;
            }

            yield return new GogOssPlayController(args.Game);
        }

        public static GogOssLibrarySettings GetSettings()
        {
            return Instance.SettingsViewModel?.Settings ?? null;
        }

        internal static List<GameAction> GetPlayTasks(string gameId, string installDir)
        {
            var gameInfoPath = Path.Combine(installDir, string.Format("goggame-{0}.info", gameId));
            if (!File.Exists(gameInfoPath))
            {
                return new List<GameAction>();
            }

            GogGameActionInfo gameTaskData = null;
            try
            {
                gameTaskData = Serialization.FromJsonFile<GogGameActionInfo>(gameInfoPath);
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to read install gog game manifest: {gameInfoPath}.");
                return new List<GameAction>();
            }

            if (gameTaskData == null)
            {
                return new List<GameAction>();
            }

            try
            {
                var playTasks = gameTaskData.playTasks?.Where(a => a.isPrimary).Select(a => a.ConvertToGenericTask(installDir)).ToList();
                return playTasks ?? new List<GameAction>();
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to get GOG play task: {gameId} {installDir}");
                return new List<GameAction>();
            }
        }

        internal static List<GameAction> GetOtherTasks(string gameId, string installDir)
        {
            var gameInfoPath = Path.Combine(installDir, string.Format("goggame-{0}.info", gameId));
            if (!File.Exists(gameInfoPath))
            {
                return new List<GameAction>();
            }

            GogGameActionInfo gameTaskData = null;
            try
            {
                gameTaskData = Serialization.FromJsonFile<GogGameActionInfo>(gameInfoPath);
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to read install gog game manifest: {gameInfoPath}.");
                return new List<GameAction>();
            }

            if (gameTaskData == null)
            {
                return new List<GameAction>();
            }

            try
            {
                var otherTasks = new List<GameAction>();
                foreach (var task in gameTaskData.playTasks.Where(a => !a.isPrimary && !a.isHidden))
                {
                    otherTasks.Add(task.ConvertToGenericTask(installDir));
                }

                if (gameTaskData.supportTasks != null)
                {
                    foreach (var task in gameTaskData.supportTasks)
                    {
                        otherTasks.Add(task.ConvertToGenericTask(installDir));
                    }
                }

                return otherTasks;
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to get GOG game tasks: {gameId} {installDir}");
                return new List<GameAction>();
            }
        }

        internal static Dictionary<string, GameMetadata> GetInstalledGames()
        {
            var games = new Dictionary<string, GameMetadata>();
            foreach (var entry in GetInstalledAppList())
            {
                var game = new GameMetadata()
                {
                    InstallDirectory = entry.Value.install_path,
                    GameId = entry.Key,
                    Source = new MetadataNameProperty("GOG"),
                    Name = entry.Value.title,
                    IsInstalled = true,
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("pc_windows") }
                };
                game.GameActions = GetOtherTasks(game.GameId, game.InstallDirectory);
                games.Add(game.GameId, game);
            }
            return games;
        }

        public static Dictionary<string, Installed> GetInstalledAppList()
        {
            if (Instance.installedAppList == null)
            {
                Instance.installedAppList = new Dictionary<string, Installed>();
                var installListPath = Path.Combine(Instance.GetPluginUserDataPath(), "installed.json");
                if (File.Exists(installListPath))
                {
                    var content = FileSystem.ReadFileAsStringSafe(installListPath);
                    if (!content.IsNullOrWhiteSpace() && Serialization.TryFromJson(content, out Dictionary<string, Installed> nonEmptyList))
                    {
                        Instance.installedAppList = nonEmptyList;
                    }
                }
            }
            var programs = Programs.GetUnistallProgramsList();
            foreach (var program in programs)
            {
                var match = Regex.Match(program.RegistryKeyName, @"^(\d+)_is1");
                if (!match.Success || program.Publisher != "GOG.com" || program.RegistryKeyName.StartsWith("GOGPACK"))
                {
                    continue;
                }

                var gameId = match.Groups[1].Value;
                if (Instance.installedAppList.ContainsKey(gameId))
                {
                    continue;
                }

                if (!Directory.Exists(program.InstallLocation))
                {
                    continue;
                }

                var game = new Installed()
                {
                    platform = "windows",
                    install_path = Paths.FixSeparators(program.InstallLocation),
                    title = program.DisplayName.RemoveTrademarks(),
                };
                if (!program.DisplayVersion.IsNullOrWhiteSpace())
                {
                    game.version = program.DisplayVersion;
                }

                var infoManifest = GogOss.GetGogGameInfo(gameId, game.install_path);
                if (!infoManifest.rootGameId.IsNullOrEmpty())
                {
                    if (infoManifest.rootGameId != gameId)
                    {
                        continue; // That's DLC
                    }
                }
                if (!GetPlayTasks(gameId, game.install_path).HasItems())
                {
                    continue; // Empty play task = DLC
                }
                if (infoManifest.buildId != null)
                {
                    game.build_id = infoManifest.buildId;
                }
                var installedDlcs = GogOss.GetInstalledDlcs(gameId, game.install_path);
                game.installed_DLCs = installedDlcs;
                Instance.installedAppList.Add(gameId, game);
                Instance.installedAppListModified = true;
            }
            return Instance.installedAppList;
        }

        internal async Task<List<GameMetadata>> GetLibraryGames()
        {
            using (var view = PlayniteApi.WebViews.CreateOffscreenView())
            {
                var api = new GogAccountClient();
                if (!await api.GetIsUserLoggedIn())
                {
                    throw new Exception("User is not logged in to GOG account.");
                }

                var libGames = await api.GetOwnedGames();
                if (libGames == null)
                {
                    throw new Exception("Failed to obtain library data.");
                }

                var accountInfo = await api.GetAccountInfo();
                var libGamesStats = await api.GetOwnedGames(accountInfo);
                if (libGamesStats != null)
                {
                    foreach (LibraryGameResponse libGame in libGames)
                    {
                        libGame.stats = libGamesStats?.FirstOrDefault(x => x.game.id.Equals(libGame.game.id))?.stats ?? null;
                    }
                }
                else
                {
                    Logger.Warn("Failed to obtain library stats data.");
                }

                return LibraryGamesToGames(libGames).ToList();
            }
        }

        internal IEnumerable<GameMetadata> LibraryGamesToGames(List<LibraryGameResponse> libGames)
        {
            foreach (var game in libGames)
            {
                var newGame = new GameMetadata()
                {
                    Source = new MetadataNameProperty("GOG"),
                    GameId = game.game.id,
                    Name = game.game.title.RemoveTrademarks(),
                    Platforms = new HashSet<MetadataProperty> { new MetadataSpecProperty("pc_windows") }
                };

                // This is a hack for inconsistent data model on GOG's side.
                // For some reason game stats are returned as an empty array if no stats exist for a game.
                // But single object representation is returned instead if stats do exits.
                // Better solution would require adding JSON.NET dependency.
                var playtimeSyncEnabled = GetSettings().SyncPlaytime;
                var gameSettings = GogOssGameSettingsView.LoadGameSettings(game.game.id);
                if (gameSettings.AutoSyncPlaytime != null)
                {
                    playtimeSyncEnabled = (bool)gameSettings.AutoSyncPlaytime;
                }
                if (game.stats?.GetType().Name == "JObject")
                {
                    var stats = ((dynamic)game.stats).ToObject<Dictionary<string, LibraryGameResponse.Stats>>() as Dictionary<string, LibraryGameResponse.Stats>;
                    if (gameSettings?.AutoSyncPlaytime != null)
                    {
                        playtimeSyncEnabled = (bool)gameSettings.AutoSyncPlaytime;
                    }
                    if (stats.Keys?.Any() == true && playtimeSyncEnabled)
                    {
                        var acc = stats.Keys.First();
                        newGame.Playtime = Convert.ToUInt64(stats[acc].playtime * 60);
                        newGame.LastActivity = stats[acc].lastSession;
                    }
                }

                yield return newGame;
            }
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var allGames = new List<GameMetadata>();
            var installedGames = new Dictionary<string, GameMetadata>();
            Exception importError = null;

            if (SettingsViewModel.Settings.ImportInstalledGames)
            {
                try
                {
                    installedGames = GetInstalledGames();
                    Logger.Debug($"Found {installedGames.Count} installed GOG games.");
                    allGames.AddRange(installedGames.Values.ToList());
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to import installed GOG games.");
                    importError = e;
                }
            }

            if (SettingsViewModel.Settings.ConnectAccount)
            {
                try
                {
                    var libraryGames = GetLibraryGames().GetAwaiter().GetResult();
                    Logger.Debug($"Found {libraryGames.Count} library GOG games.");

                    if (!SettingsViewModel.Settings.ImportUninstalledGames)
                    {
                        libraryGames = libraryGames.Where(lg => installedGames.ContainsKey(lg.GameId)).ToList();
                    }

                    foreach (var game in libraryGames)
                    {
                        if (installedGames.TryGetValue(game.GameId, out var installed))
                        {
                            installed.Playtime = game.Playtime;
                            installed.LastActivity = game.LastActivity;
                        }
                        else
                        {
                            allGames.Add(game);
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to import linked account GOG games details.");
                    importError = e;
                }
            }

            if (importError != null)
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    ImportErrorMessageId,
                    LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteLibraryImportError, new Dictionary<string, IFluentType> { ["var0"] = (FluentString)Name }) +
                    System.Environment.NewLine + importError.Message,
                    NotificationType.Error,
                    () => OpenSettingsView()));
            }
            else
            {
                PlayniteApi.Notifications.Remove(ImportErrorMessageId);
            }

            return allGames;
        }

        public void LoadExtraLocalization()
        {
            var currentLanguage = PlayniteApi.ApplicationSettings.Language;
            LocalizationManager.Instance.SetLanguage(currentLanguage);
            var commonFluentArgs = new Dictionary<string, IFluentType>
            {
                { "pluginShortName", (FluentString)"GOG OSS" },
                { "originalPluginShortName", (FluentString)"GOG" },
                { "updatesSourceName", (FluentString)"GOG" }
            };
            LocalizationManager.Instance.SetCommonArgs(commonFluentArgs);
        }

        public string GetCachePath(string dirName)
        {
            var cacheDir = Path.Combine(GetPluginUserDataPath(), "cache", dirName);
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }
            return cacheDir;
        }

        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return downloadManagerSidebarItem;
        }

        public override async void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            var globalSettings = GetSettings();
            if (globalSettings != null)
            {
                if (globalSettings.GamesUpdatePolicy != UpdatePolicy.Never)
                {
                    var nextGamesUpdateTime = globalSettings.NextGamesUpdateTime;
                    if (nextGamesUpdateTime != 0)
                    {
                        DateTimeOffset now = DateTime.UtcNow;
                        if (now.ToUnixTimeSeconds() >= nextGamesUpdateTime)
                        {
                            globalSettings.NextGamesUpdateTime = GetNextUpdateCheckTime(globalSettings.GamesUpdatePolicy);
                            SavePluginSettings(globalSettings);
                            GogOssUpdateController GogOssUpdateController = new GogOssUpdateController();
                            var gamesUpdates = await GogOssUpdateController.CheckAllGamesUpdates();
                            if (gamesUpdates.Count > 0)
                            {
                                var successUpdates = gamesUpdates.Where(i => i.Value.Success).ToDictionary(i => i.Key, i => i.Value);
                                if (successUpdates.Count > 0)
                                {
                                    if (globalSettings.AutoUpdateGames)
                                    {
                                        await GogOssUpdateController.UpdateGame(successUpdates, "", true);
                                    }
                                    else
                                    {
                                        Window window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                                        {
                                            ShowMaximizeButton = false,
                                        });
                                        window.DataContext = successUpdates;
                                        window.Title = $"{LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteExtensionsUpdates)}";
                                        window.Content = new GogOssUpdaterView();
                                        window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                                        window.SizeToContent = SizeToContent.WidthAndHeight;
                                        window.MinWidth = 600;
                                        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                                        window.ShowDialog();
                                    }
                                }
                                else
                                {
                                    PlayniteApi.Notifications.Add(new NotificationMessage("GogOssGamesUpdateCheckFail",
                                                                                          $"{Name} {Environment.NewLine}{LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUpdateCheckFailMessage)}",
                                                                                          NotificationType.Error));
                                }
                            }
                        }
                    }
                }
                if (globalSettings.CometUpdatePolicy != UpdatePolicy.Never && Comet.IsInstalled)
                {
                    var nextCometUpdateTime = globalSettings.NextCometUpdateTime;
                    if (nextCometUpdateTime != 0)
                    {
                        DateTimeOffset now = DateTime.UtcNow;
                        if (now.ToUnixTimeSeconds() >= nextCometUpdateTime)
                        {
                            globalSettings.NextCometUpdateTime = GetNextUpdateCheckTime(globalSettings.CometUpdatePolicy);
                            SavePluginSettings(globalSettings);
                            if (Comet.IsInstalled)
                            {
                                var cometVersionInfoContent = await Comet.GetVersionInfoContent();
                                if (cometVersionInfoContent.Tag_name != null)
                                {
                                    var newVersion = new Version(cometVersionInfoContent.Tag_name.Replace("v", ""));
                                    var oldVersion = new Version(await Comet.GetCometVersion());
                                    if (oldVersion.CompareTo(newVersion) < 0)
                                    {
                                        var options = new List<MessageBoxOption>
                                    {
                                        new MessageBoxOption(LocalizationManager.Instance.GetString(LOC.CommonViewChangelog), true),
                                        new MessageBoxOption(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteOkLabel), false, true),
                                    };
                                        var result = PlayniteApi.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonNewVersionAvailable, new Dictionary<string, IFluentType> { ["appName"] = (FluentString)"Comet", ["appVersion"] = (FluentString)newVersion.ToString() }), LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUpdaterWindowTitle), MessageBoxImage.Information, options);
                                        if (result == options[0])
                                        {
                                            var changelogURL = $"https://github.com/imLinguin/comet/releases/tag/v{newVersion}";
                                            Playnite.Commands.GlobalCommands.NavigateUrl(changelogURL);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            StopDownloadManager();
            if (installedAppList != null && installedAppListModified)
            {
                var commonHelpers = Instance.commonHelpers;
                commonHelpers.SaveJsonSettingsToFile(installedAppList, "", "installed", true);
            }
            GogOssDownloadManagerView downloadManager = GetGogOssDownloadManager();
            var settings = GetSettings();
            if (settings != null)
            {
                if (settings.AutoRemoveCompletedDownloads != ClearCacheTime.Never)
                {
                    var nextRemovingCompletedDownloadsTime = settings.NextRemovingCompletedDownloadsTime;
                    if (nextRemovingCompletedDownloadsTime != 0)
                    {
                        DateTimeOffset now = DateTime.UtcNow;
                        if (now.ToUnixTimeSeconds() >= nextRemovingCompletedDownloadsTime)
                        {
                            foreach (var downloadItem in downloadManager.downloadManagerData.downloads.ToList())
                            {
                                if (downloadItem.status == DownloadStatus.Completed)
                                {
                                    downloadManager.downloadManagerData.downloads.Remove(downloadItem);
                                    downloadManager.downloadsChanged = true;
                                }
                            }
                            settings.NextRemovingCompletedDownloadsTime = GetNextClearingTime(settings.AutoRemoveCompletedDownloads);
                            SavePluginSettings(settings);
                        }
                    }
                    else
                    {
                        settings.NextRemovingCompletedDownloadsTime = GetNextClearingTime(settings.AutoRemoveCompletedDownloads);
                        SavePluginSettings(settings);
                    }
                }
                if (settings.AutoClearCache != ClearCacheTime.Never)
                {
                    var nextClearingTime = settings.NextClearingTime;
                    if (nextClearingTime != 0)
                    {
                        DateTimeOffset now = DateTime.UtcNow;
                        if (now.ToUnixTimeSeconds() >= nextClearingTime)
                        {
                            GogOss.ClearCache();
                            settings.NextClearingTime = GetNextClearingTime(settings.AutoClearCache);
                            SavePluginSettings(settings);
                        }
                    }
                    else
                    {
                        settings.NextClearingTime = GetNextClearingTime(settings.AutoClearCache);
                        SavePluginSettings(settings);
                    }
                }
                downloadManager.SaveData();
            }
        }

        public bool StopDownloadManager()
        {
            GogOssDownloadManagerView downloadManager = GetGogOssDownloadManager();
            var runningAndQueuedDownloads = downloadManager.downloadManagerData.downloads.Where(i => i.status == DownloadStatus.Running
                                                                                                     || i.status == DownloadStatus.Queued).ToList();
            if (runningAndQueuedDownloads.Count > 0)
            {
                foreach (var download in runningAndQueuedDownloads)
                {
                    if (download.status == DownloadStatus.Running)
                    {
                        downloadManager.gracefulInstallerCTS?.Cancel();
                        downloadManager.gracefulInstallerCTS?.Dispose();
                    }
                    download.status = DownloadStatus.Paused;
                }
                downloadManager.SaveData();
            }
            return true;
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            var gogOssGames = args.Games.Where(i => i.PluginId == Id).ToList();
            if (gogOssGames.Count > 0)
            {
                if (gogOssGames.Count == 1)
                {
                    Game game = gogOssGames.FirstOrDefault();
                    if (game.IsInstalled)
                    {
                        yield return new GameMenuItem
                        {
                            Description = LocalizationManager.Instance.GetString(LOC.CommonLauncherSettings),
                            Icon = "ModifyLaunchSettingsIcon",
                            Action = (args) =>
                            {
                                Window window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                                {
                                    ShowMaximizeButton = false
                                });
                                window.DataContext = game;
                                window.Title = $"{LocalizationManager.Instance.GetString(LOC.CommonLauncherSettings)} - {game.Name}";
                                window.Content = new GogOssGameSettingsView();
                                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                                window.SizeToContent = SizeToContent.WidthAndHeight;
                                window.MinWidth = 600;
                                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                                window.ShowDialog();
                            }
                        };
                        yield return new GameMenuItem
                        {
                            Description = LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteCheckForUpdates),
                            Icon = "UpdateDbIcon",
                            Action = (args) =>
                            {
                                GogOssUpdateController gogOssUpdateController = new GogOssUpdateController();
                                var gamesToUpdate = new Dictionary<string, UpdateInfo>();
                                GlobalProgressOptions updateCheckProgressOptions = new GlobalProgressOptions(LocalizationManager.Instance.GetString(LOC.CommonCheckingForUpdates), false) { IsIndeterminate = true };
                                PlayniteApi.Dialogs.ActivateGlobalProgress(async (a) =>
                                {
                                    gamesToUpdate = await gogOssUpdateController.CheckGameUpdates(game.Name, game.GameId);
                                }, updateCheckProgressOptions);
                                if (gamesToUpdate.Count > 0)
                                {
                                    var successUpdates = gamesToUpdate.Where(i => i.Value.Success).ToDictionary(i => i.Key, i => i.Value);
                                    if (successUpdates.Count > 0)
                                    {
                                        Window window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                                        {
                                            ShowMaximizeButton = false,
                                        });
                                        window.DataContext = successUpdates;
                                        window.Title = $"{LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteExtensionsUpdates)}";
                                        window.Content = new GogOssUpdaterView();
                                        window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                                        window.SizeToContent = SizeToContent.WidthAndHeight;
                                        window.MinWidth = 600;
                                        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                                        window.ShowDialog();
                                    }
                                    else
                                    {
                                        PlayniteApi.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUpdateCheckFailMessage), game.Name);
                                    }
                                }
                                else
                                {
                                    PlayniteApi.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonNoUpdatesAvailable), game.Name);
                                }
                            }
                        };
                    }
                    else
                    {
                        yield return new GameMenuItem
                        {
                            Description = LocalizationManager.Instance.GetString(LOC.CommonImportInstalledGame),
                            Icon = "AddGameIcon",
                            Action = (args) =>
                            {
                                var path = PlayniteApi.Dialogs.SelectFolder();
                                if (path != "")
                                {
                                    GlobalProgressOptions importProgressOptions = new GlobalProgressOptions(LocalizationManager.Instance.GetString(LOC.CommonImportingGame, new Dictionary<string, IFluentType> { ["gameTitle"] = (FluentString)game.Name }), false) { IsIndeterminate = true };
                                    PlayniteApi.Dialogs.ActivateGlobalProgress(async (a) =>
                                    {
                                        game.InstallDirectory = path;
                                        var gogGameInfo = GogOss.GetGogGameInfo(game.GameId, game.InstallDirectory);
                                        var installedInfo = new Installed
                                        {
                                            install_path = path,
                                            build_id = gogGameInfo.buildId,
                                            title = gogGameInfo.name.RemoveTrademarks(),
                                            platform = "windows",
                                            is_fully_installed = true
                                        };
                                        var installedLanguage = "";
                                        if (gogGameInfo.languages.Count > 0)
                                        {
                                            installedLanguage = gogGameInfo.languages[0];
                                        }
                                        else if (!gogGameInfo.language.IsNullOrEmpty())
                                        {
                                            installedLanguage = gogGameInfo.language;
                                        }
                                        else
                                        {
                                            installedLanguage = "en-US";
                                        }

                                        game.Name = installedInfo.title;
                                        var downloadGameInfo = await gogDownloadApi.GetGameMetaManifest(game.GameId, installedInfo);
                                        installedInfo.version = downloadGameInfo.versionName;
                                        game.Version = downloadGameInfo.versionName;
                                        var dlcs = GogOss.GetInstalledDlcs(game.GameId, path);
                                        installedInfo.installed_DLCs = dlcs;
                                        game.IsInstalled = true;
                                        var installedAppList = GetInstalledAppList();
                                        installedAppList.Add(game.GameId, installedInfo);
                                        Instance.installedAppListModified = true;
                                        PlayniteApi.Database.Games.Update(game);
                                        PlayniteApi.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonImportFinished));
                                    }, importProgressOptions);
                                }
                            }
                        };
                    }
                    yield return new GameMenuItem
                    {
                        Description = LocalizationManager.Instance.GetString(LOC.CommonManageDlcs),
                        Icon = "AddonsIcon",
                        Action = (args) =>
                        {
                            Window window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                            {
                                ShowMaximizeButton = false,
                            });
                            window.Title = $"{LocalizationManager.Instance.GetString(LOC.CommonManageDlcs)} - {game.Name}";
                            window.DataContext = game;
                            window.Content = new GogOssDlcManager();
                            window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                            window.SizeToContent = SizeToContent.WidthAndHeight;
                            window.MinWidth = 600;
                            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                            window.ShowDialog();
                        }
                    };
                    yield return new GameMenuItem
                    {
                        Description = LocalizationManager.Instance.GetString(LOC.GogOssManageExtras),
                        Icon = "ExtrasIcon",
                        Action = (args) =>
                        {
                            Window window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                            {
                                ShowMaximizeButton = false,
                            });
                            window.Title = $"{LocalizationManager.Instance.GetString(LOC.GogOssManageExtras)} - {game.Name}";
                            window.DataContext = game;
                            window.Content = new GogOssExtrasManager();
                            window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                            window.SizeToContent = SizeToContent.WidthAndHeight;
                            window.MinWidth = 600;
                            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                            window.ShowDialog();
                        }
                    };
                    if (game.IsInstalled)
                    {
                        yield return new GameMenuItem
                        {
                            Description = LocalizationManager.Instance.GetString(LOC.CommonMove),
                            Icon = "MoveIcon",
                            Action = (args) =>
                            {
                                var newPath = PlayniteApi.Dialogs.SelectFolder();
                                if (newPath != "")
                                {
                                    var oldPath = game.InstallDirectory;
                                    if (Directory.Exists(oldPath) && Directory.Exists(newPath))
                                    {
                                        string sepChar = Path.DirectorySeparatorChar.ToString();
                                        string altChar = Path.AltDirectorySeparatorChar.ToString();
                                        if (!oldPath.EndsWith(sepChar) && !oldPath.EndsWith(altChar))
                                        {
                                            oldPath += sepChar;
                                        }
                                        var folderName = Path.GetFileName(Path.GetDirectoryName(oldPath));
                                        newPath = Path.Combine(newPath, folderName);
                                        var moveConfirm = PlayniteApi.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonMoveConfirm, new Dictionary<string, IFluentType> { ["appName"] = (FluentString)game.Name, ["path"] = (FluentString)newPath }), LocalizationManager.Instance.GetString(LOC.CommonMove), MessageBoxButton.YesNo, MessageBoxImage.Question);
                                        if (moveConfirm == MessageBoxResult.Yes)
                                        {
                                            GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(LocalizationManager.Instance.GetString(LOC.CommonMovingGame, new Dictionary<string, IFluentType> { ["appName"] = (FluentString)game.Name, ["path"] = (FluentString)newPath }), false);
                                            PlayniteApi.Dialogs.ActivateGlobalProgress((a) =>
                                            {
                                                a.ProgressMaxValue = 3;
                                                a.CurrentProgressValue = 0;
                                                _ = (Application.Current.Dispatcher?.BeginInvoke((Action)async delegate
                                                {
                                                    try
                                                    {
                                                        Directory.Move(oldPath, newPath);
                                                        a.CurrentProgressValue = 1;
                                                        var installedAppList = GetInstalledAppList();
                                                        if (installedAppList.ContainsKey(game.GameId))
                                                        {
                                                            var installedApp = installedAppList[game.GameId];
                                                            installedApp.install_path = newPath;
                                                            installedAppListModified = true;
                                                            await GogOss.CompleteInstallation(game.GameId);
                                                        }
                                                        a.CurrentProgressValue = 2;
                                                        game.InstallDirectory = newPath;
                                                        PlayniteApi.Database.Games.Update(game);
                                                        a.CurrentProgressValue = 3;
                                                        PlayniteApi.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonMoveGameSuccess, new Dictionary<string, IFluentType> { ["appName"] = (FluentString)game.Name, ["path"] = (FluentString)newPath }));
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        a.CurrentProgressValue = 3;
                                                        PlayniteApi.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.CommonMoveGameError, new Dictionary<string, IFluentType> { ["appName"] = (FluentString)game.Name, ["path"] = (FluentString)newPath }));
                                                        logger.Error(e.Message);
                                                    }
                                                }));
                                            }, globalProgressOptions);
                                        }
                                    }
                                }
                            }
                        };
                    }
                }

                var notInstalledGogOssGames = gogOssGames.Where(i => i.IsInstalled == false).ToList();
                if (notInstalledGogOssGames.Count > 0)
                {
                    if (gogOssGames.Count > 1)
                    {
                        var installData = new List<DownloadManagerData.Download>();
                        foreach (var notInstalledLegendaryGame in notInstalledGogOssGames)
                        {
                            var installProperties = new DownloadProperties { downloadAction = DownloadAction.Install };
                            installData.Add(new DownloadManagerData.Download { gameID = notInstalledLegendaryGame.GameId, name = notInstalledLegendaryGame.Name, downloadProperties = installProperties });
                        }
                        yield return new GameMenuItem
                        {
                            Description = LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteInstallGame),
                            Icon = "InstallIcon",
                            Action = (args) =>
                            {
                                GogOssInstallController.LaunchInstaller(installData);
                            }
                        };
                    }
                }
                var installedGogOssGames = gogOssGames.Where(i => i.IsInstalled).ToList();
                if (installedGogOssGames.Count > 0)
                {
                    yield return new GameMenuItem
                    {
                        Description = LocalizationManager.Instance.GetString(LOC.CommonRepair),
                        Icon = "RepairIcon",
                        Action = (args) =>
                        {
                            var installData = new List<DownloadManagerData.Download>();
                            foreach (var game in installedGogOssGames)
                            {
                                var installProperties = new DownloadProperties { downloadAction = DownloadAction.Repair, installPath = CommonHelpers.NormalizePath(game.InstallDirectory) };
                                installData.Add(new DownloadManagerData.Download { gameID = game.GameId, name = game.Name, downloadProperties = installProperties, fullInstallPath = installProperties.installPath });
                            }
                            GogOssInstallController.LaunchInstaller(installData);
                        }
                    };
                    if (gogOssGames.Count > 1)
                    {
                        yield return new GameMenuItem
                        {
                            Description = LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUninstallGame),
                            Icon = "UninstallIcon",
                            Action = (args) =>
                            {
                                GogOssUninstallController.LaunchUninstaller(installedGogOssGames);
                            }
                        };
                    }
                }
            }
        }

        public static long GetNextUpdateCheckTime(UpdatePolicy frequency)
        {
            DateTimeOffset? updateTime = null;
            DateTimeOffset now = DateTime.UtcNow;
            switch (frequency)
            {
                case UpdatePolicy.PlayniteLaunch:
                    updateTime = now;
                    break;
                case UpdatePolicy.Day:
                    updateTime = now.AddDays(1);
                    break;
                case UpdatePolicy.Week:
                    updateTime = now.AddDays(7);
                    break;
                case UpdatePolicy.Month:
                    updateTime = now.AddMonths(1);
                    break;
                case UpdatePolicy.ThreeMonths:
                    updateTime = now.AddMonths(3);
                    break;
                case UpdatePolicy.SixMonths:
                    updateTime = now.AddMonths(6);
                    break;
                default:
                    break;
            }
            return updateTime?.ToUnixTimeSeconds() ?? 0;
        }

        public static long GetNextClearingTime(ClearCacheTime frequency)
        {
            DateTimeOffset? clearingTime = null;
            DateTimeOffset now = DateTime.UtcNow;
            switch (frequency)
            {
                case ClearCacheTime.Day:
                    clearingTime = now.AddDays(1);
                    break;
                case ClearCacheTime.Week:
                    clearingTime = now.AddDays(7);
                    break;
                case ClearCacheTime.Month:
                    clearingTime = now.AddMonths(1);
                    break;
                case ClearCacheTime.ThreeMonths:
                    clearingTime = now.AddMonths(3);
                    break;
                case ClearCacheTime.SixMonths:
                    clearingTime = now.AddMonths(6);
                    break;
                default:
                    break;
            }
            return clearingTime?.ToUnixTimeSeconds() ?? 0;
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = LocalizationManager.Instance.GetString(LOC.CommonCheckForGamesUpdatesButton),
                MenuSection = $"@{Instance.Name}",
                Icon = "UpdateDbIcon",
                Action = (args) =>
                {
                    var gamesUpdates = new Dictionary<string, UpdateInfo>();
                    GogOssUpdateController GogOssUpdateController = new GogOssUpdateController();
                    GlobalProgressOptions updateCheckProgressOptions = new GlobalProgressOptions(LocalizationManager.Instance.GetString(LOC.CommonCheckingForUpdates), false) { IsIndeterminate = true };
                    PlayniteApi.Dialogs.ActivateGlobalProgress(async (a) =>
                    {
                        gamesUpdates = await GogOssUpdateController.CheckAllGamesUpdates();
                    }, updateCheckProgressOptions);
                    if (gamesUpdates.Count > 0)
                    {
                        var successUpdates = gamesUpdates.Where(i => i.Value.Success).ToDictionary(i => i.Key, i => i.Value);
                        if (successUpdates.Count > 0)
                        {
                            Window window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                            {
                                ShowMaximizeButton = false,
                            });
                            window.DataContext = successUpdates;
                            window.Title = $"{LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteExtensionsUpdates)}";
                            window.Content = new GogOssUpdaterView();
                            window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                            window.SizeToContent = SizeToContent.WidthAndHeight;
                            window.MinWidth = 600;
                            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                            window.ShowDialog();
                        }
                        else
                        {
                            PlayniteApi.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUpdateCheckFailMessage));
                        }
                    }
                    else
                    {
                        PlayniteApi.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonNoUpdatesAvailable));
                    }
                }
            };

            if (PlayniteApi.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
            {
                yield return new MainMenuItem
                {
                    Description = LocalizationManager.Instance.GetString(LOC.CommonDownloadManager),
                    MenuSection = $"@{Instance.Name}",
                    Icon = "InstallIcon",
                    Action = (args) =>
                    {
                        Window window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                        {
                            ShowMaximizeButton = true,
                        });
                        window.Title = $"{LocalizationManager.Instance.GetString(LOC.CommonPanel)}";
                        window.Content = GetGogOssDownloadManager();
                        window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                        window.SizeToContent = SizeToContent.WidthAndHeight;
                        window.ShowDialog();
                    }
                };
            }

            yield return new MainMenuItem
            {
                Description = LocalizationManager.Instance.GetString(LOC.CommonFinishInstallation),
                MenuSection = $"@{Instance.Name}",
                Icon = "FinishInstallationIcon",
                Action = (args) =>
                {
                    var installedAppList = GetInstalledAppList();
                    var gamesToCompleteInstall = installedAppList.Where(g => !GogOss.GetInstalledInfo(g.Key).is_fully_installed).ToList();

                    if (gamesToCompleteInstall.Any())
                    {
                        GlobalProgressOptions installProgressOptions = new(LocalizationManager.Instance.GetString(LOC.CommonFinishingInstallation), false) { IsIndeterminate = false };
                        PlayniteApi.Dialogs.ActivateGlobalProgress(async (progress) =>
                        {
                            progress.ProgressMaxValue = gamesToCompleteInstall.Count;
                            int current = 0;
                            foreach (var game in gamesToCompleteInstall)
                            {
                                progress.Text = $"{LocalizationManager.Instance.GetString(LOC.CommonFinishingInstallation)} ({game.Value.title})";
                                await GogOss.CompleteInstallation(game.Key);
                                current++;
                                progress.CurrentProgressValue = current;
                            }
                        }, installProgressOptions);
                    }
                    else
                    {
                        PlayniteApi.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonNoFinishNeeded));
                    }
                }
            };

        }
    }
}
