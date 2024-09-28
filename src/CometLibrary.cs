using CometLibraryNS.Enums;
using CometLibraryNS.Models;
using CometLibraryNS.Services;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CometLibraryNS
{
    [LoadPlugin]
    public class CometLibrary : LibraryPluginBase<CometLibrarySettingsViewModel>
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public static CometLibrary Instance { get; set; }
        public CometDownloadManagerView CometDownloadManagerView { get; set; }
        private readonly SidebarItem downloadManagerSidebarItem;

        public CometLibrary(IPlayniteAPI api) : base(
            "Comet (GOG)",
            Guid.Parse("03689811-3F33-4DFB-A121-2EE168FB9A5C"),
            new LibraryPluginProperties { CanShutdownClient = true, HasSettings = true },
            new CometClient(),
            Comet.Icon,
            (_) => new CometLibrarySettingsView(),
            api)
        {
            Instance = this;
            SettingsViewModel = new CometLibrarySettingsViewModel(this, api);
            Load3pLocalization();
            downloadManagerSidebarItem = new SidebarItem
            {
                Title = ResourceProvider.GetString(LOC.CometPanel),
                Icon = Comet.Icon,
                Type = SiderbarItemType.View,
                Opened = () => GetCometDownloadManager(),
                ProgressValue = 0,
                ProgressMaximum = 100,
            };
        }

        public static SidebarItem GetPanel()
        {
            return Instance.downloadManagerSidebarItem;
        }

        public static CometDownloadManagerView GetCometDownloadManager()
        {
            if (Instance.CometDownloadManagerView == null)
            {
                Instance.CometDownloadManagerView = new CometDownloadManagerView();
            }
            return Instance.CometDownloadManagerView;
        }

        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            yield return new GogInstallController(args.Game, this);
        }

        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            yield return new GogUninstallController(args.Game);
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

            yield return new CometPlayController(args.Game);
        }

        public static CometLibrarySettings GetSettings()
        {
            return Instance.SettingsViewModel?.Settings ?? null;
        }

        public static GogGameActionInfo GetGogGameInfoManifest(string gameId, string installDir)
        {
            GogGameActionInfo gameTaskData = null;
            var gameInfoPath = Path.Combine(installDir, string.Format("goggame-{0}.info", gameId));
            try
            {
                gameTaskData = Serialization.FromJsonFile<GogGameActionInfo>(gameInfoPath);
            }
            catch (Exception e)
            {
                logger.Error(e, $"Failed to read install gog game manifest: {gameInfoPath}.");
            }
            return gameTaskData;
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
                foreach (var task in gameTaskData.playTasks.Where(a => !a.isPrimary))
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
                if (entry.Value.Is_dlc)
                {
                    continue;
                }
                if (entry.Value.Download_item_type != DownloadItemType.Game)
                {
                    continue;
                }
                var game = new GameMetadata()
                {
                    InstallDirectory = entry.Value.Install_path,
                    GameId = entry.Key,
                    Source = new MetadataNameProperty("GOG"),
                    Name = entry.Value.Title,
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
            var installListPath = Path.Combine(Instance.GetPluginUserDataPath(), "installed.json");
            var list = new Dictionary<string, Installed>();
            if (File.Exists(installListPath))
            {
                var content = FileSystem.ReadFileAsStringSafe(installListPath);
                if (!content.IsNullOrWhiteSpace() && Serialization.TryFromJson(content, out Dictionary<string, Installed> nonEmptyList))
                {
                    list = nonEmptyList;
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

                if (!Directory.Exists(program.InstallLocation))
                {
                    continue;
                }

                var gameId = match.Groups[1].Value;
                if (list.ContainsKey(gameId))
                {
                    continue;
                }
                var game = new Installed()
                {
                    Platform = "windows",
                    Install_path = Paths.FixSeparators(program.InstallLocation),
                    Version = program.DisplayVersion,
                    Title = program.DisplayName.RemoveTrademarks(),
                };
                if (!GetPlayTasks(gameId, game.Install_path).HasItems())
                {
                    game.Is_dlc = true; // Empty play task = DLC
                }
                var infoManifest = GetGogGameInfoManifest(gameId, game.Install_path);
                if (infoManifest.buildId != null)
                {
                    game.Build_id = infoManifest.buildId;
                }
                list.Add(gameId, game);
            }
            return list;
        }

        internal async Task<List<GameMetadata>> GetLibraryGames()
        {
            using (var view = PlayniteApi.WebViews.CreateOffscreenView())
            {
                var api = new GogAccountClient(view);
                if (!await api.GetIsUserLoggedIn())
                {
                    throw new Exception("User is not logged in to GOG account.");
                }

                var libGames = api.GetOwnedGames();
                if (libGames == null)
                {
                    throw new Exception("Failed to obtain library data.");
                }

                var accountInfo = await api.GetAccountInfo();
                var libGamesStats = api.GetOwnedGames(accountInfo);
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
                if (game.stats?.GetType().Name == "JObject")
                {
                    var stats = ((dynamic)game.stats).ToObject<Dictionary<string, LibraryGameResponse.Stats>>() as Dictionary<string, LibraryGameResponse.Stats>;
                    if (stats.Keys?.Any() == true)
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
                    string.Format(PlayniteApi.Resources.GetString("LOCLibraryImportError"), Name) +
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

        public void Load3pLocalization()
        {
            var currentLanguage = PlayniteApi.ApplicationSettings.Language;
            var dictionaries = Application.Current.Resources.MergedDictionaries;

            void loadString(string xamlPath)
            {
                ResourceDictionary res = null;
                try
                {
                    res = Xaml.FromFile<ResourceDictionary>(xamlPath);
                    res.Source = new Uri(xamlPath, UriKind.Absolute);
                    foreach (var key in res.Keys)
                    {
                        if (res[key] is string locString && locString.IsNullOrEmpty())
                        {
                            res.Remove(key);
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.Error(e, $"Failed to parse localization file {xamlPath}");
                    return;
                }
                dictionaries.Add(res);
            }

            var extraLocDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Localization\third_party");
            if (!Directory.Exists(extraLocDir))
            {
                return;
            }

            var enXaml = Path.Combine(extraLocDir, "en_US.xaml");
            if (!File.Exists(enXaml))
            {
                return;
            }

            loadString(enXaml);
            if (currentLanguage != "en_US")
            {
                var langXaml = Path.Combine(extraLocDir, $"{currentLanguage}.xaml");
                if (File.Exists(langXaml))
                {
                    loadString(langXaml);
                }
            }
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

        public bool StopDownloadManager(bool displayConfirm = false)
        {
            CometDownloadManagerView downloadManager = GetCometDownloadManager();
            var runningAndQueuedDownloads = downloadManager.downloadManagerData.downloads.Where(i => i.status == DownloadStatus.Running
                                                                                                     || i.status == DownloadStatus.Queued).ToList();
            if (runningAndQueuedDownloads.Count > 0)
            {
                if (displayConfirm)
                {
                    var stopConfirm = PlayniteApi.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.CometInstanceNotice), "", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (stopConfirm == MessageBoxResult.No)
                    {
                        return false;
                    }
                }
                foreach (var download in runningAndQueuedDownloads)
                {
                    if (download.status == DownloadStatus.Running)
                    {
                        downloadManager.gracefulInstallerCTS?.Cancel();
                        downloadManager.gracefulInstallerCTS?.Dispose();
                        downloadManager.forcefulInstallerCTS?.Dispose();
                    }
                    download.status = DownloadStatus.Paused;
                }
                downloadManager.SaveData();
            }
            return true;
        }
    }
}
