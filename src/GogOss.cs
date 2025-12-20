using CliWrap;
using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace GogOssLibraryNS
{
    public class GogOss
    {
        public const string EnStoreLocaleString = "US_USD_en-US";
        public static string TokensPath = Path.Combine(GogOssLibrary.Instance.GetPluginUserDataPath(), "tokens.json");
        public static string EncryptedTokensPath = Path.Combine(GogOssLibrary.Instance.GetPluginUserDataPath(), "tokens_encrypted.json");
        public static string Icon => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Resources\gogicon.png");
        public static string UserAgent => @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";
        private static readonly ILogger logger = LogManager.GetLogger();

        public static Installed GetInstalledInfo(string gameId)
        {
            var installedAppList = GogOssLibrary.GetInstalledAppList();
            var installedInfo = new Installed();
            if (installedAppList.ContainsKey(gameId))
            {
                installedInfo = installedAppList[gameId];
            }
            else
            {
                installedInfo.is_fully_installed = true;
            }
            return installedInfo;
        }

        public static async Task CompleteInstallation(string gameId)
        {
            var gogDownloadApi = new GogDownloadApi();
            var installedInfo = GetInstalledInfo(gameId);
            var metaManifest = await gogDownloadApi.GetGameMetaManifest(gameId, installedInfo);
            var shortLang = installedInfo.language.Split('-')[0];
            var langInEnglish = "";
            if (!shortLang.IsNullOrEmpty())
            {
                try
                {
                    langInEnglish = new CultureInfo(shortLang).EnglishName;
                }
                catch (Exception ex)
                {
                    logger.Warn(ex, $"Unrecognized language: {shortLang}.");
                    langInEnglish = shortLang;
                }
            }
            else
            {
                langInEnglish = "English";
            }
            if (installedInfo.language.IsNullOrEmpty())
            {
                installedInfo.language = "en-US";
            }
            if (metaManifest.version == 1)
            {
                if (metaManifest.product.support_commands.Count > 0)
                {
                    foreach (var support_command in metaManifest.product.support_commands)
                    {
                        if (!support_command.executable.IsNullOrEmpty())
                        {
                            var supportPath = Path.Combine(installedInfo.install_path, "gog-support", support_command.gameID);
                            var supportExe = Path.GetFullPath(Path.Combine(supportPath, support_command.executable.TrimStart('/')));
                            var supportArgs = new List<string>
                            {
                                "/VERYSILENT",
                                $"/DIR={installedInfo.install_path}",
                                $"/ProductId={gameId}",
                                $"/buildId={metaManifest.product.timestamp}",
                                $"/versionName={installedInfo.version}",
                                $"/Language={langInEnglish}",
                                $"/LANG={langInEnglish}",
                                "/galaxyclient",
                                "/nodesktopshortcut",
                                "/nodesktopshorctut", // Yes, they made a typo
                            };
                            if (File.Exists(supportExe))
                            {
                                await Cli.Wrap(supportExe)
                                         .WithArguments(supportArgs)
                                         .WithWorkingDirectory(supportPath)
                                         .AddCommandToLog()
                                         .ExecuteAsync();
                            }
                        }
                    }
                }
            }
            else if (metaManifest.scriptInterpreter)
            {
                var isiInstallPath = Path.Combine(DependenciesInstallationPath, "__redist", "ISI");
                if (isiInstallPath != "" && Directory.Exists(isiInstallPath))
                {
                    foreach (var product in metaManifest.products)
                    {
                        if (product.productId != gameId && !installedInfo.installed_DLCs.Contains(product.productId))
                        {
                            continue;
                        }
                        var args = new List<string>
                        {
                            "/VERYSILENT",
                            $"/DIR={installedInfo.install_path}",
                            $"/ProductId={product.productId}",
                            $"/buildId={installedInfo.build_id}",
                            $"/versionName={installedInfo.version}",
                            $"/Language={langInEnglish}",
                            $"/LANG={langInEnglish}",
                            $"/lang-code={installedInfo.language}",
                            "/galaxyclient",
                            "/nodesktopshortcut",
                            "/nodesktopshorctut", // Yes, they made a typo
                        };
                        var supportPath = Path.Combine(installedInfo.install_path, "gog-support");
                        if (Directory.Exists(supportPath))
                        {
                            args.Add($"/supportDir={supportPath}");
                        }
                        var isiExe = Path.Combine(isiInstallPath, "scriptinterpreter.exe");
                        if (File.Exists(isiExe))
                        {
                            await Cli.Wrap(isiExe)
                                     .WithArguments(args)
                                     .WithWorkingDirectory(isiInstallPath)
                                     .AddCommandToLog()
                                     .ExecuteAsync();
                        }
                    }
                }
            }
            else
            {
                var product = metaManifest.products.FirstOrDefault(i => i.productId == gameId);
                if (product != null && !product.temp_executable.IsNullOrEmpty())
                {
                    var supportPath = Path.Combine(installedInfo.install_path, "gog-support", gameId);
                    var tempExe = Path.GetFullPath(Path.Combine(supportPath, product.temp_executable.TrimStart('/')));
                    var tempArgs = new List<string>
                    {
                        "/VERYSILENT",
                        $"/DIR={installedInfo.install_path}",
                        $"/ProductId={gameId}",
                        $"/buildId={installedInfo.build_id}",
                        $"/versionName={installedInfo.version}",
                        $"/Language={langInEnglish}",
                        $"/LANG={langInEnglish}",
                        $"/lang-code={installedInfo.language}",
                        "/galaxyclient",
                        "/nodesktopshortcut",
                        "/nodesktopshorctut", // Yes, they made a typo
                    };
                    if (File.Exists(tempExe))
                    {
                        await Cli.Wrap(tempExe)
                                 .WithArguments(tempArgs)
                                 .WithWorkingDirectory(supportPath)
                                 .AddCommandToLog()
                                 .ExecuteAsync();
                    }
                }
            }
            var folderName = Path.GetFileName(installedInfo.install_path);
            var startMenuFolderName = $"{folderName} [GOG.com]";
            var startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", startMenuFolderName);
            var startMenuShortcut = Path.Combine(startMenuDir, $"{folderName}.lnk");
            if (File.Exists(startMenuShortcut))
            {
                File.Delete(startMenuShortcut);
            }
            var tasks = GogOssLibrary.GetPlayTasks(gameId, installedInfo.install_path);
            var gameExe = tasks[0].Path;
            using var shortcut = new WindowsShortcutFactory.WindowsShortcut
            {
                Path = gameExe,
                WorkingDirectory = installedInfo.install_path
            };
            startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", startMenuFolderName);
            if (!Directory.Exists(startMenuDir))
            {
                Directory.CreateDirectory(startMenuDir);
            }
            shortcut.Save(Path.Combine(startMenuDir, $"{folderName}.lnk"));

            // Install dependencies
            var depends = installedInfo.Dependencies.ToList();
            if (depends.Count > 0)
            {
                bool installedDependsModified = false;
                var installedDepends = GetInstalledDepends();
                foreach (var depend in depends)
                {
                    if (!installedDepends.Contains(depend))
                    {
                        var dependManifest = await GogDownloadApi.GetRedistInfo(depend);
                        if (dependManifest.executable.path != "")
                        {
                            var dependExe = Path.GetFullPath(Path.Combine(DependenciesInstallationPath, dependManifest.executable.path));
                            if (File.Exists(dependExe))
                            {
                                var process = ProcessStarter.StartProcess(dependExe, dependManifest.executable.arguments, true);
                                process.WaitForExit();
                            }
                        }
                        installedInfo.Dependencies.Remove(depend);
                        installedDepends.Add(depend);
                        installedDependsModified = true;
                    }
                }
                if (installedDependsModified)
                {
                    var installedDependsManifest = new InstalledDepends();
                    installedDependsManifest.InstalledDependsList = installedDepends;
                    var commonHelpers = GogOssLibrary.Instance.commonHelpers;
                    commonHelpers.SaveJsonSettingsToFile(installedDependsManifest, "", "installedDepends", true);
                }
            }
            installedInfo.is_fully_installed = true;
            GogOssLibrary.Instance.installedAppListModified = true;
        }

        public static GogGameActionInfo GetGogGameInfoFromFile(string manifestFilePath)
        {
            var gameInfo = new GogGameActionInfo();
            if (File.Exists(manifestFilePath))
            {
                var content = FileSystem.ReadFileAsStringSafe(manifestFilePath);
                if (!content.IsNullOrWhiteSpace())
                {
                    try
                    {
                        gameInfo = Serialization.FromJson<GogGameActionInfo>(content);
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, $"Failed to read install gog game manifest: {manifestFilePath}.");
                    }
                }
            }
            return gameInfo;
        }

        public static GogGameActionInfo GetGogGameInfo(string gameId, string installPath)
        {
            var manifestFile = Path.Combine(installPath, $"goggame-{gameId}.info");
            return GetGogGameInfoFromFile(manifestFile);
        }

        public static List<string> GetInstalledDlcs(string gameId, string gamePath)
        {
            var dlcs = new List<string>();
            string[] files = Directory.GetFiles(gamePath, "goggame-*.info", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (!fileName.Contains(gameId))
                {
                    var dlcInfo = GetGogGameInfoFromFile(file);
                    dlcs.Add(dlcInfo.gameId);
                }
            }
            return dlcs;
        }

        public static void ClearCache()
        {
            var dataDir = GogOssLibrary.Instance.GetPluginUserDataPath();
            var cacheDir = Path.Combine(dataDir, "cache");
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
            }
        }

        public static bool DefaultPlaytimeSyncEnabled
        {
            get
            {
                var playniteAPI = API.Instance;
                var playtimeSyncEnabled = false;
                if (playniteAPI.ApplicationSettings.PlaytimeImportMode != PlaytimeImportMode.Never)
                {
                    playtimeSyncEnabled = true;
                }
                return playtimeSyncEnabled;
            }
        }

        public static int DefaultConnectionTimeout = 10;
        public static int MaxMaxWorkers = 40;

        public static async Task<GogGameMetaManifest.SizeType> CalculateGameSize(string gameId, Installed installedInfo)
        {
            var downloadProperties = new DownloadProperties
            {
                buildId = installedInfo.build_id,
                extraContent = installedInfo.installed_DLCs,
                language = installedInfo.language,
                version = installedInfo.version,
                os = installedInfo.platform
            };
            var downloadData = new DownloadManagerData.Download
            {
                gameID = gameId,
                name = installedInfo.title,
                downloadProperties = downloadProperties
            };
            return await CalculateGameSize(downloadData);
        }

        public static async Task<GogGameMetaManifest.SizeType> CalculateGameSize(DownloadManagerData.Download installData)
        {
            var gogDownloadApi = new GogDownloadApi();

            var size = new GogGameMetaManifest.SizeType
            {
                download_size = 0,
                disk_size = 0
            };

            if (installData.downloadItemType == DownloadItemType.Game)
            {
                var manifest = await gogDownloadApi.GetGameMetaManifest(installData);
                if (manifest.size.ContainsKey("*"))
                {
                    size.download_size += manifest.size["*"].download_size;
                    size.disk_size += manifest.size["*"].disk_size;
                }
                if (installData.downloadItemType == DownloadItemType.Dependency)
                {
                    return size;
                }
                var selectedLanguage = installData.downloadProperties.language;
                if (manifest.size.Count == 2)
                {
                    selectedLanguage = manifest.size.ElementAt(1).Key.ToString();
                }
                if (manifest.size.ContainsKey(selectedLanguage))
                {
                    size.download_size += manifest.size[selectedLanguage].download_size;
                    size.disk_size += manifest.size[selectedLanguage].disk_size;
                }
                var selectedDlcs = installData.downloadProperties.extraContent;
                if (selectedDlcs.Count() > 0)
                {
                    foreach (var dlc in manifest.dlcs)
                    {
                        if (selectedDlcs.Contains(dlc.Key))
                        {
                            if (dlc.Value.size.ContainsKey("*"))
                            {
                                size.download_size += dlc.Value.size["*"].download_size;
                                size.disk_size += dlc.Value.size["*"].disk_size;
                            }
                            if (dlc.Value.size.ContainsKey(selectedLanguage))
                            {
                                size.download_size += dlc.Value.size[selectedLanguage].download_size;
                                size.disk_size += dlc.Value.size[selectedLanguage].disk_size;
                            }
                        }
                    }
                }
            }
            else
            {
                var dependManifest = await GogDownloadApi.GetRedistInfo(installData.gameID);
                size.download_size = dependManifest.compressedSize;
                size.disk_size = dependManifest.size;
            }
            return size;
        }

        public static List<string> GetInstalledDepends()
        {
            var depends = new List<string>();
            var dataDir = GogOssLibrary.Instance.GetPluginUserDataPath();
            var installedFile = Path.Combine(dataDir, "installedDepends.json");
            if (File.Exists(installedFile))
            {
                var installedDependsManifest = Serialization.FromJson<InstalledDepends>(File.ReadAllText(installedFile));
                if (installedDependsManifest != null)
                {
                    depends = installedDependsManifest.InstalledDependsList;
                }
            }
            return depends;
        }

        public static string DependenciesInstallationPath
        {
            get
            {
                var dataDir = GogOssLibrary.Instance.GetPluginUserDataPath();
                var dependPath = Path.Combine(dataDir, ".gogRedist");
                return dependPath;
            }
        }

        public static string GamesInstallationPath
        {
            get
            {
                var installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Games");
                var playniteAPI = API.Instance;
                if (playniteAPI.ApplicationInfo.IsPortable)
                {
                    var playniteDirectoryVariable = ExpandableVariables.PlayniteDirectory.ToString();
                    installPath = Path.Combine(playniteDirectoryVariable, "Games");
                }
                var savedSettings = GogOssLibrary.GetSettings();
                if (savedSettings != null)
                {
                    var savedGamesInstallationPath = savedSettings.GamesInstallationPath;
                    if (savedGamesInstallationPath != "")
                    {
                        installPath = savedGamesInstallationPath;
                    }
                }
                return installPath;
            }
        }

        public static string ExtrasInstallationPath
        {
            get
            {
                var installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "GameExtras");
                var playniteAPI = API.Instance;
                if (playniteAPI.ApplicationInfo.IsPortable)
                {
                    var playniteDirectoryVariable = ExpandableVariables.PlayniteDirectory.ToString();
                    installPath = Path.Combine(playniteDirectoryVariable, "GameExtras");
                }
                return installPath;
            }
        }

        public static async Task<GogDepot.Depot> GetInstalledBigDepot(Installed installedInfo, string gameId)
        {
            GogDepot.Depot bigDepot = new();
            var installedDepotPath = Path.Combine(installedInfo.install_path, ".manifest_oss");
            var installedDepotFile = Path.Combine(installedDepotPath, "bigDepot.json");
            bool correctJson = false;
            if (File.Exists(installedDepotFile))
            {
                var content = FileSystem.ReadFileAsStringSafe(installedDepotFile);
                if (!content.IsNullOrWhiteSpace() && Serialization.TryFromJson(content, out GogDepot.Depot newBigDepot))
                {
                    if (newBigDepot != null)
                    {
                        correctJson = true;
                        bigDepot = newBigDepot;
                    }
                }
            }
            if (!correctJson)
            {
                var taskProperties = new DownloadProperties
                {
                    buildId = installedInfo.build_id,
                    extraContent = installedInfo.installed_DLCs,
                    language = installedInfo.language,
                    os = installedInfo.platform,
                    version = installedInfo.version
                };
                var taskData = new DownloadManagerData.Download
                {
                    gameID = gameId,
                    name = installedInfo.title,
                    downloadProperties = taskProperties,
                };
                bigDepot = await CreateNewBigDepot(taskData);
            }
            return bigDepot;
        }

        public static async Task<GogDepot.Depot> CreateNewBigDepot(DownloadManagerData.Download taskData)
        {
            GogDownloadApi gogDownloadApi = new();
            Dictionary<string, List<string>> depotHashes = new();
            if (taskData.downloadItemType == DownloadItemType.Game)
            {
                depotHashes = await gogDownloadApi.GetNeededDepotManifestHashes(taskData);
            }
            else if (taskData.downloadItemType == DownloadItemType.Dependency)
            {
                var dependManifest = await GogDownloadApi.GetRedistInfo(taskData.gameID);
                var dependHashes = new List<string>
                {
                    dependManifest.manifest
                };
                depotHashes.Add(taskData.gameID, dependHashes);
            }

            GogDepot.Depot bigDepot = new();
            var metaManifest = new GogGameMetaManifest();
            if (taskData.downloadItemType == DownloadItemType.Game || taskData.downloadItemType == DownloadItemType.Dependency)
            {
                metaManifest = await gogDownloadApi.GetGameMetaManifest(taskData);
            }

            bigDepot.version = metaManifest.version;

            foreach (var depotHash in depotHashes)
            {
                foreach (var singleDepotHash in depotHash.Value)
                {
                    var depotManifest = await gogDownloadApi.GetDepotInfo(singleDepotHash, taskData, bigDepot.version);
                    if (depotManifest.depot.items.Count > 0)
                    {
                        foreach (var depotItem in depotManifest.depot.items)
                        {
                            if (depotItem.sfcRef != null)
                            {
                                depotItem.sfcRef.depotHash = singleDepotHash;
                            }
                            depotItem.product_id = depotHash.Key;
                            bigDepot.items.Add(depotItem);
                        }
                    }
                    if (depotManifest.depot.smallFilesContainer?.chunks.Count > 0)
                    {
                        depotManifest.depot.smallFilesContainer.product_id = depotHash.Key;
                        bigDepot.sfcContainersByHash.Add(singleDepotHash, depotManifest.depot.smallFilesContainer);
                    }
                    if (depotManifest.depot.files.Count > 0)
                    {
                        foreach (var depotFile in depotManifest.depot.files)
                        {
                            depotFile.product_id = depotHash.Key;
                            bigDepot.files.Add(depotFile);
                        }
                    }
                }
            }

            if (bigDepot.version == 1 && (taskData.downloadItemType == DownloadItemType.Game || taskData.downloadItemType == DownloadItemType.Dependency))
            {
                foreach (var depot in metaManifest.product.depots)
                {
                    if (!depot.redist.IsNullOrEmpty() && !depot.targetDir.IsNullOrEmpty())
                    {
                        var dependManifest = await GogDownloadApi.GetRedistInfo(depot.redist);
                        var dependData = new DownloadManagerData.Download
                        {
                            gameID = depot.redist,
                            downloadItemType = DownloadItemType.Dependency
                        };
                        dependData.downloadProperties = new DownloadProperties
                        {
                            os = taskData.downloadProperties.os,
                        };

                        var depotManifest = await gogDownloadApi.GetDepotInfo(dependManifest.manifest, dependData);
                        if (depotManifest.depot.items.Count > 0)
                        {
                            foreach (var depotItem in depotManifest.depot.items)
                            {
                                depotItem.redistTargetDir = depot.targetDir;
                                bigDepot.items.Add(depotItem);
                            }
                        }
                    }
                }
            }
            else if (taskData.downloadItemType == DownloadItemType.Overlay)
            {
                bigDepot = await GetOverlayManifest();
            }
            else if (taskData.downloadItemType == DownloadItemType.Extra)
            {
                var extrasManifest = await GetExtras(taskData.gameID.Split('_')[0]);
                var extraId = taskData.gameID.Split('_')[1];
                var matchingExtra = extrasManifest.FirstOrDefault(e => e.ManualUrl.Contains(extraId));
                var file = new GogDepot.DepotFile
                {
                    product_id = taskData.gameID,
                    size = (long)taskData.downloadSizeNumber,
                    url = $"https://www.gog.com{matchingExtra.ManualUrl}",
                    path = "/"
                };
                bigDepot.files.Add(file);
            }
            return bigDepot;
        }

        public static async Task <List<Extra>> GetExtras(string gameId)
        {
            List<Extra> gogExtras = new();
            var dataDir = GogOssLibrary.Instance.GetPluginUserDataPath();
            LibraryGameDetailsResponse gameDetailsInfo = new();
            var extrasDir = Path.Combine(dataDir, "cache", "extras");
            var extrasFilePath = Path.Combine(extrasDir, $"{gameId}.json");
            Directory.CreateDirectory(extrasDir);
            bool correctJson = false;
            if (File.Exists(extrasFilePath))
            {
                var extrasFileContent = File.ReadAllText(extrasFilePath);
                if (extrasFileContent != null)
                {
                    if (Serialization.TryFromJson(extrasFileContent, out LibraryGameDetailsResponse newGameDetailsInfo))
                    {
                        if (!newGameDetailsInfo.Title.IsNullOrEmpty())
                        {
                            gameDetailsInfo = newGameDetailsInfo;
                            correctJson = true;
                        }
                    }
                }
            }
            if (!correctJson)
            {
                var gogApi = new GogAccountClient();
                gameDetailsInfo = await gogApi.GetOwnedGameDetails(gameId);
                if (!gameDetailsInfo.Title.IsNullOrEmpty())
                {
                    correctJson = true;
                    File.WriteAllText(extrasFilePath, Serialization.ToJson(gameDetailsInfo));
                }
            }
            gogExtras = gameDetailsInfo.Extras;
            var dlcs = gameDetailsInfo.Dlcs;
            if (dlcs.Count > 0)
            {
                foreach (var dlc in dlcs)
                {
                    gogExtras.AddRange(dlc.Extras);
                }
            }
            return gogExtras;
        }

        public static void AddToHeroicInstalledList(Installed installedInfo, string gameId, double installSize = 0)
        {
            var heroicInstalledPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heroic", "gog_store", "installed.json");
            if (File.Exists(heroicInstalledPath))
            {
                var heroicInstalledContent = FileSystem.ReadFileAsStringSafe(heroicInstalledPath);
                if (!heroicInstalledContent.IsNullOrWhiteSpace())
                {
                    if (installSize == 0)
                    {
                        foreach (var file in Directory.GetFiles(installedInfo.install_path, "*", SearchOption.AllDirectories))
                        {
                            installSize += new FileInfo(file).Length;
                        }
                    }
                    var heroicInstallInfo = new HeroicInstalled.HeroicInstalledSingle
                    {
                        appName = gameId,
                        build_id = installedInfo.build_id,
                        title = installedInfo.title,
                        version = installedInfo.version,
                        platform = installedInfo.platform,
                        install_path = installedInfo.install_path,
                        language = installedInfo.language,
                        installed_DLCs = installedInfo.installed_DLCs,
                        install_size = CommonPlugin.CommonHelpers.FormatSize(installSize)
                    };
                    var heroicInstalledJson = Serialization.FromJson<HeroicInstalled>(heroicInstalledContent);
                    var wantedHeroicItem = heroicInstalledJson.installed.FirstOrDefault(i => i.appName == gameId);
                    if (wantedHeroicItem != null)
                    {
                        heroicInstalledJson.installed.Remove(wantedHeroicItem);
                    }
                    heroicInstalledJson.installed.Add(heroicInstallInfo);
                    var strConf = Serialization.ToJson(heroicInstalledJson, true);
                    File.WriteAllText(heroicInstalledPath, strConf);
                }
            }
        }

        public static async Task<GogDepot.Depot> GetOverlayManifest()
        {
            var depotManifest = new GogDepot.Depot();
            bool correctJson = false;

            var cacheInfoFileName = $"galaxy-overlay.json";
            var cachePath = GogOssLibrary.Instance.GetCachePath("overlay");
            var cacheInfoFile = Path.Combine(cachePath, cacheInfoFileName);
            if (File.Exists(cacheInfoFile))
            {
                if (File.GetLastWriteTime(cacheInfoFile) < DateTime.Now.AddDays(-7))
                {
                    File.Delete(cacheInfoFile);
                }
            }
            if (File.Exists(cacheInfoFile))
            {
                var content = File.ReadAllText(cacheInfoFile);
                if (!string.IsNullOrWhiteSpace(content) && Serialization.TryFromJson(content, out GogDepot.Depot newManifest))
                {
                    if (newManifest != null)
                    {
                        correctJson = true;
                        depotManifest = newManifest;
                    }
                }
            }

            if (!correctJson)
            {
                List<ComponentChoice> components = new();
                components.Add(ComponentChoice.Web);
                components.Add(ComponentChoice.Overlay);
                var gogDownloadApi = new GogDownloadApi();
                foreach (var component in components)
                {
                    var componentManifest = await gogDownloadApi.GetComponentManifest(component);
                    if (component == ComponentChoice.Overlay)
                    {
                        depotManifest.overlayVersion = componentManifest.version;
                    }
                    if (component == ComponentChoice.Web)
                    {
                        depotManifest.webVersion = componentManifest.version;
                    }
                    foreach (var file in componentManifest.files)
                    {
                        var depotFile = new GogDepot.DepotFile
                        {
                            hash = file.hash,
                            path = file.path,
                            product_id = "galaxy-overlay",
                            size = file.size,
                            url = $"{componentManifest.baseURI}/{file.resource}"
                        };
                        depotManifest.files.Add(depotFile);
                    }
                }
                if (depotManifest != null)
                {
                    if (!Directory.Exists(cachePath))
                    {
                        Directory.CreateDirectory(cachePath);
                    }
                    File.WriteAllText(cacheInfoFile, Serialization.ToJson(depotManifest));
                }
            }
            return depotManifest;
        }
    }
}
