using CliWrap;
using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GogOssLibraryNS
{
    [LoadPlugin]
    public class GogOssLibrary : LibraryPluginBase<GogOssLibrarySettingsViewModel>
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public static GogOssLibrary Instance { get; set; }
        public GogOssDownloadManagerView GogOssDownloadManagerView { get; set; }
        private readonly SidebarItem downloadManagerSidebarItem;
        public Dictionary<string, Installed> installedAppList { get; set; }
        public bool installedAppListModified { get; set; } = false;

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
            SettingsViewModel = new GogOssLibrarySettingsViewModel(this, api);
            LoadExtraLocalization();
            downloadManagerSidebarItem = new SidebarItem
            {
                Title = ResourceProvider.GetString(LOC.GogOssPanel),
                Icon = GogOss.Icon,
                Type = SiderbarItemType.View,
                Opened = () => GetGogOssDownloadManager(),
                ProgressValue = 0,
                ProgressMaximum = 100,
            };
        }

        public static SidebarItem GetPanel()
        {
            return Instance.downloadManagerSidebarItem;
        }

        public static GogOssDownloadManagerView GetGogOssDownloadManager()
        {
            if (Instance.GogOssDownloadManagerView == null)
            {
                Instance.GogOssDownloadManagerView = new GogOssDownloadManagerView();
            }
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
                if (entry.Value.is_dlc)
                {
                    continue;
                }
                if (entry.Value.item_type != DownloadItemType.Game)
                {
                    continue;
                }
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
            var list = Instance.installedAppList;
            if (list == null)
            {
                list = new Dictionary<string, Installed>();
                var installListPath = Path.Combine(Instance.GetPluginUserDataPath(), "installed.json");
                if (File.Exists(installListPath))
                {
                    var content = FileSystem.ReadFileAsStringSafe(installListPath);
                    if (!content.IsNullOrWhiteSpace() && Serialization.TryFromJson(content, out Dictionary<string, Installed> nonEmptyList))
                    {
                        list = nonEmptyList;
                    }
                }
                Instance.installedAppList = list;
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
                    version = program.DisplayVersion,
                    title = program.DisplayName.RemoveTrademarks(),
                };
                if (!GetPlayTasks(gameId, game.install_path).HasItems())
                {
                    continue; // Empty play task = DLC
                }
                var infoManifest = GogOss.GetGogGameInfo(gameId, game.install_path);
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

        public void LoadExtraLocalization()
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

            // Load GOG OSS specific strings
            extraLocDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Localization");
            enXaml = Path.Combine(extraLocDir, "en_US-gog-oss.xaml");
            loadString(enXaml);
            if (currentLanguage != "en_US")
            {
                var langXaml = Path.Combine(extraLocDir, $"{currentLanguage}-gog-oss.xaml");
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

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            StopDownloadManager();
            if (installedAppList != null && installedAppListModified)
            {
                Helpers.SaveJsonSettingsToFile(installedAppList, "installed");
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
                        downloadManager.forcefulInstallerCTS?.Dispose();
                    }
                    download.status = DownloadStatus.Paused;
                }
            }
            downloadManager.SaveData();
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
                            Description = ResourceProvider.GetString(LOC.GogOssLauncherSettings),
                            Icon = "ModifyLaunchSettingsIcon",
                            Action = (args) =>
                            {
                                Window window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                                {
                                    ShowMaximizeButton = false
                                });
                                window.DataContext = game;
                                window.Title = $"{ResourceProvider.GetString(LOC.GogOssLauncherSettings)} - {game.Name}";
                                window.Content = new GogOssGameSettingsView();
                                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                                window.SizeToContent = SizeToContent.WidthAndHeight;
                                window.MinWidth = 600;
                                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                                window.ShowDialog();
                            }
                        };
                        //    yield return new GameMenuItem
                        //    {
                        //        Description = ResourceProvider.GetString(LOC.Legendary3P_PlayniteCheckForUpdates),
                        //        Icon = "UpdateDbIcon",
                        //        Action = (args) =>
                        //        {
                        //            if (!LegendaryLauncher.IsInstalled)
                        //            {
                        //                throw new Exception(ResourceProvider.GetString(LOC.LegendaryLauncherNotInstalled));
                        //            }

                        //            LegendaryUpdateController legendaryUpdateController = new LegendaryUpdateController();
                        //            var gamesToUpdate = new Dictionary<string, UpdateInfo>();
                        //            GlobalProgressOptions updateCheckProgressOptions = new GlobalProgressOptions(ResourceProvider.GetString(LOC.LegendaryCheckingForUpdates), false) { IsIndeterminate = true };
                        //            PlayniteApi.Dialogs.ActivateGlobalProgress(async (a) =>
                        //            {
                        //                gamesToUpdate = await legendaryUpdateController.CheckGameUpdates(game.Name, game.GameId);
                        //            }, updateCheckProgressOptions);
                        //            if (gamesToUpdate.Count > 0)
                        //            {
                        //                var successUpdates = gamesToUpdate.Where(i => i.Value.Success).ToDictionary(i => i.Key, i => i.Value);
                        //                if (successUpdates.Count > 0)
                        //                {
                        //                    Window window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                        //                    {
                        //                        ShowMaximizeButton = false,
                        //                    });
                        //                    window.DataContext = successUpdates;
                        //                    window.Title = $"{ResourceProvider.GetString(LOC.Legendary3P_PlayniteExtensionsUpdates)}";
                        //                    window.Content = new LegendaryUpdater();
                        //                    window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                        //                    window.SizeToContent = SizeToContent.WidthAndHeight;
                        //                    window.MinWidth = 600;
                        //                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        //                    window.ShowDialog();
                        //                }
                        //                else
                        //                {
                        //                    PlayniteApi.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.Legendary3P_PlayniteUpdateCheckFailMessage), game.Name);
                        //                }
                        //            }
                        //            else
                        //            {
                        //                PlayniteApi.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.LegendaryNoUpdatesAvailable), game.Name);
                        //            }
                        //        }
                        //    };
                    }
                    else
                    {
                        yield return new GameMenuItem
                        {
                            Description = ResourceProvider.GetString(LOC.GogOssImportInstalledGame),
                            Icon = "AddGameIcon",
                            Action = (args) =>
                            {
                                var path = PlayniteApi.Dialogs.SelectFolder();
                                if (path != "")
                                {
                                    GlobalProgressOptions importProgressOptions = new GlobalProgressOptions(ResourceProvider.GetString(LOC.GogOssImportingGame).Format(game.Name), false) { IsIndeterminate = true };
                                    PlayniteApi.Dialogs.ActivateGlobalProgress(async (a) =>
                                    {
                                        game.InstallDirectory = path;
                                        var gogGameInfo = GogOss.GetGogGameInfo(game.GameId, path);
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
                                        var downloadGameInfo = await Gogdl.GetGameInfo(game.GameId, installedInfo);
                                        installedInfo.version = downloadGameInfo.versionName;
                                        var dlcs = GogOss.GetInstalledDlcs(game.GameId, path);
                                        installedInfo.installed_DLCs = dlcs;
                                        game.Version = downloadGameInfo.versionName;
                                        game.IsInstalled = true;
                                        var installedAppList = GetInstalledAppList();
                                        installedAppList.Add(game.GameId, installedInfo);
                                        Instance.installedAppListModified = true;
                                        //var playtimeSyncEnabled = GetSettings().SyncPlaytime;
                                        //if (playtimeSyncEnabled)
                                        //{
                                        //    var accountApi = new EpicAccountClient(PlayniteApi, LegendaryLauncher.TokensPath);
                                        //    var playtimeItems = await accountApi.GetPlaytimeItems();
                                        //    var playtimeItem = playtimeItems?.FirstOrDefault(x => x.artifactId == game.GameId);
                                        //    if (playtimeItem != null)
                                        //    {
                                        //        game.Playtime = playtimeItem.totalTime;
                                        //    }
                                        //}
                                        PlayniteApi.Dialogs.ShowMessage(LOC.GogOssImportFinished);
                                    }, importProgressOptions);
                                }
                            }
                        };
                    }
                    yield return new GameMenuItem
                    {
                        Description = ResourceProvider.GetString(LOC.GogOssManageDlcs),
                        Icon = "AddonsIcon",
                        Action = (args) =>
                        {
                            Window window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                            {
                                ShowMaximizeButton = false,
                            });
                            window.Title = $"{ResourceProvider.GetString(LOC.GogOssManageDlcs)} - {game.Name}";
                            window.DataContext = game;
                            window.Content = new GogOssDlcManager();
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
                            Description = ResourceProvider.GetString(LOC.GogOssMove),
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
                                        var moveConfirm = PlayniteApi.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.GogOssMoveConfirm).Format(game.Name, newPath), ResourceProvider.GetString(LOC.GogOssMove), MessageBoxButton.YesNo, MessageBoxImage.Question);
                                        if (moveConfirm == MessageBoxResult.Yes)
                                        {
                                            GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(ResourceProvider.GetString(LOC.GogOssMovingGame).Format(game.Name, newPath), false);
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
                                                        PlayniteApi.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.GogOssMoveGameSuccess).Format(game.Name, newPath));
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        a.CurrentProgressValue = 3;
                                                        PlayniteApi.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.GogOssMoveGameError).Format(game.Name, newPath));
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
                            Description = ResourceProvider.GetString(LOC.GogOss3P_PlayniteInstallGame),
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
                        Description = ResourceProvider.GetString(LOC.GogOssRepair),
                        Icon = "RepairIcon",
                        Action = (args) =>
                        {
                            var installData = new List<DownloadManagerData.Download>();
                            foreach (var game in installedGogOssGames)
                            {
                                var installProperties = new DownloadProperties { downloadAction = DownloadAction.Repair };
                                installData.Add(new DownloadManagerData.Download { gameID = game.GameId, name = game.Name, downloadProperties = installProperties });
                            }
                            GogOssInstallController.LaunchInstaller(installData);
                        }
                    };
                    if (gogOssGames.Count > 1)
                    {
                        yield return new GameMenuItem
                        {
                            Description = ResourceProvider.GetString(LOC.GogOss3P_PlayniteUninstallGame),
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
    }
}
