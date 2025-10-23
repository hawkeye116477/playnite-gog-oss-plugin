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
                langInEnglish = new CultureInfo(shortLang).EnglishName;
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
                            var playniteAPI = API.Instance;
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
                                $"/lang-code={installedInfo.language}",
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
                var isiInstallPath = Path.Combine(GogOss.DependenciesInstallationPath, "__redist", "ISI");
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

        public static int MaxMaxWorkers = 40;

        public static async Task<GogDownloadGameInfo.SizeType> CalculateGameSize(string gameId, Installed installedInfo)
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

        public static async Task<GogDownloadGameInfo.SizeType> CalculateGameSize(DownloadManagerData.Download installData)
        {
            var gogDownloadApi = new GogDownloadApi();

            var size = new GogDownloadGameInfo.SizeType
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
                if (installData.downloadItemType == Enums.DownloadItemType.Dependency)
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
    }
}
