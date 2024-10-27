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
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace GogOssLibraryNS
{
    public class GogOss
    {
        public const string EnStoreLocaleString = "US_USD_en-US";
        public static string TokensPath = Path.Combine(GogOssLibrary.Instance.GetPluginUserDataPath(), "tokens.json");
        public static string Icon => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"Resources\gogicon.png");
        public static string DownloaderUserAgent => @"Playnite/10";
        public static string UserAgent => @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly HttpClient httpClient = new HttpClient();

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

        public static async Task LaunchIsi(Installed installedGameInfo, string gameId)
        {
            var isiInstallPath = Path.Combine(Gogdl.DependenciesInstallationPath, "__redist", "ISI");
            if (isiInstallPath != "" && Directory.Exists(isiInstallPath))
            {
                var metaManifest = Gogdl.GetGameMetaManifest(gameId);
                var shortLang = installedGameInfo.language.Split('-')[0];
                var langInEnglish = "";
                if (!shortLang.IsNullOrEmpty())
                {
                    langInEnglish = new CultureInfo(shortLang).EnglishName;
                }
                else
                {
                    langInEnglish = "English";
                }
                foreach (var product in metaManifest.products)
                {
                    if (product.productId != gameId && !installedGameInfo.installed_DLCs.Contains(product.productId))
                    {
                        continue;
                    }
                    var args = new List<string>
                    {
                        "/VERYSILENT",
                        $"/DIR={installedGameInfo.install_path}",
                        $"/ProductId={product.productId}",
                        "/galaxyclient",
                        $"/buildId={installedGameInfo.build_id}",
                        $"/versionName={installedGameInfo.version}",
                        "/nodesktopshortcut",
                        "/nodesktopshorctut", // Yes, they made a typo
                    };
                    if (!langInEnglish.IsNullOrEmpty())
                    {
                        args.AddRange(new[] {
                                    $"/Language={langInEnglish}",
                                    $"/LANG={langInEnglish}",
                                    $"/lang-code={installedGameInfo.language}" });
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

        public static async Task CompleteInstallation(string gameId)
        {
            var installedInfo = GetInstalledInfo(gameId);
            var metaManifest = Gogdl.GetGameMetaManifest(gameId);
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
                                "/galaxyclient",
                                $"/buildId={metaManifest.product.timestamp}",
                                $"/versionName={installedInfo.version}",
                                "/nodesktopshortcut",
                                "/nodesktopshorctut", // Yes, they made a typo
                            };
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
                            if (!langInEnglish.IsNullOrEmpty())
                            {
                                supportArgs.AddRange(new[] {
                                            $"/Language={langInEnglish}",
                                            $"/LANG={langInEnglish}",
                                            $"/lang-code={installedInfo.language}" });
                            }
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
                await LaunchIsi(installedInfo, gameId);
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
                        "/galaxyclient",
                        $"/buildId={installedInfo.build_id}",
                        $"/versionName={installedInfo.version}",
                        "/nodesktopshortcut",
                        "/nodesktopshorctut", // Yes, they made a typo
                   };
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
                    if (!langInEnglish.IsNullOrEmpty())
                    {
                        tempArgs.AddRange(new[] {
                                            $"/Language={langInEnglish}",
                                            $"/LANG={langInEnglish}",
                                            $"/lang-code={installedInfo.language}" });
                    }
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

        public static GogGameActionInfo GetGogGameInfo(string gameId)
        {
            var installedGame = GetInstalledInfo(gameId);
            var manifestFile = Path.Combine(installedGame.install_path, $"goggame-{gameId}.info");
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

        public static async Task<GogGameMetaManifest> GetGameMetaManifest(string gameId, Installed installedInfo, bool skipRefreshing = false, bool silently = false, bool forceRefreshCache = false)
        {
            var downloadData = new DownloadManagerData.Download
            {
                gameID = gameId,
                name = installedInfo.title
            };
            downloadData.downloadProperties.buildId = installedInfo.build_id;
            return await GetGameMetaManifest(downloadData);
        }

        public static async Task<GogGameMetaManifest> GetGameMetaManifest(DownloadManagerData.Download downloadData, bool skipRefreshing = false, bool silently = false, bool forceRefreshCache = false)
        {
            var manifest = new GogGameMetaManifest();
            var playniteAPI = API.Instance;
            var logger = LogManager.GetLogger();
            var cacheInfoPath = GogOssLibrary.Instance.GetCachePath("metacache");
            var cacheInfoFileName = $"{downloadData.gameID}.json";
            if (downloadData.downloadProperties.buildId != "")
            {
                cacheInfoFileName = $"{downloadData.gameID}_build{downloadData.downloadProperties.buildId}.json";
            }
            var cacheInfoFile = Path.Combine(cacheInfoPath, cacheInfoFileName);
            bool correctJson = false;
            if (File.Exists(cacheInfoFile))
            {
                if (!skipRefreshing)
                {
                    if (File.GetLastWriteTime(cacheInfoFile) < DateTime.Now.AddDays(-7) || forceRefreshCache)
                    {
                        File.Delete(cacheInfoFile);
                    }
                }
            }
            if (File.Exists(cacheInfoFile))
            {
                var content = FileSystem.ReadFileAsStringSafe(cacheInfoFile);
                if (!content.IsNullOrWhiteSpace() && Serialization.TryFromJson(content, out manifest))
                {
                    if (manifest != null)
                    {
                        correctJson = true;
                    }
                }
            }
            if (!correctJson)
            {
                if (!Directory.Exists(cacheInfoPath))
                {
                    Directory.CreateDirectory(cacheInfoPath);
                }
                if (downloadData.downloadItemType == DownloadItemType.Dependency)
                {
                    var redistManifest = await GetRedistInfo(downloadData.gameID, downloadData.downloadProperties.version, skipRefreshing, silently, forceRefreshCache);
                    manifest.executable = redistManifest.executable;
                    manifest.buildId = redistManifest.build_id;
                    manifest.size = new Dictionary<string, GogGameMetaManifest.SizeType>();
                    manifest.readableName = redistManifest.readableName;
                    downloadData.name = manifest.readableName;
                    var redistSizes = new GogGameMetaManifest.SizeType
                    {
                        disk_size = redistManifest.size,
                        download_size = redistManifest.compressedSize
                    };
                    manifest.size.Add("*", redistSizes);
                    manifest.executable = redistManifest.executable;
                    return manifest;
                }

                var builds = await GetGameBuilds(downloadData.gameID);
                if (builds.items.Count > 0)
                {
                    httpClient.DefaultRequestHeaders.Clear();
                    httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                    string chosenBranch = downloadData.downloadProperties.betaChannel;
                    var chosenBuildId = downloadData.downloadProperties.buildId;
                    if (chosenBranch == "disabled")
                    {
                        chosenBranch = "";
                    }
                    var selectedBuild = builds.items.FirstOrDefault(i => i.branch == chosenBranch && i.build_id == chosenBuildId);
                    if (selectedBuild != null)
                    {
                        var response = await httpClient.GetAsync(selectedBuild.link);
                        Stream content = null;
                        if (response.IsSuccessStatusCode)
                        {
                            content = await response.Content.ReadAsStreamAsync();
                        }
                        else
                        {
                            manifest.errorDisplayed = true;
                            logger.Error($"An error occurred while downloading {downloadData.name} meta manifest.");
                            return manifest;
                        }
                        if (!Directory.Exists(cacheInfoPath))
                        {
                            Directory.CreateDirectory(cacheInfoPath);
                        }
                        var result = Helpers.DecompressZlib(content);
                        var gogAccountClient = new GogAccountClient();
                        if (!result.IsNullOrWhiteSpace())
                        {
                            manifest = Serialization.FromJson<GogGameMetaManifest>(result);
                            manifest.size = new Dictionary<string, GogGameMetaManifest.SizeType>();
                            manifest.languages = new List<string>();
                            manifest.versionName = selectedBuild.version_name;
                            var ownedItems = await gogAccountClient.GetOwnedIds();
                            if (manifest.version == 2)
                            {
                                logger.Debug(string.Join(",", ownedItems.ToString()));
                                foreach (var product in manifest.products)
                                {
                                    if (product.productId != downloadData.gameID)
                                    {
                                        if (ownedItems.Contains(int.Parse(product.productId)))
                                        {
                                            var dlc = new GogGameMetaManifest.Dlc
                                            {
                                                title = product.name
                                            };
                                            manifest.dlcs.Add(product.productId, dlc);
                                        }
                                    }
                                }
                            }

                            foreach (var depot in manifest.depots)
                            {
                                foreach (var language in depot.languages)
                                {
                                    var newLanguage = language;
                                    if (newLanguage == "Neutral")
                                    {
                                        newLanguage = "*";
                                    }
                                    if (newLanguage != "*")
                                    {
                                        manifest.languages.AddMissing(newLanguage);
                                    }
                                    if (!manifest.size.ContainsKey(newLanguage))
                                    {
                                        manifest.size.Add(newLanguage, new GogGameMetaManifest.SizeType());
                                    }
                                    if (depot.productId == downloadData.gameID)
                                    {
                                        manifest.size[newLanguage].download_size += depot.compressedSize;
                                        manifest.size[newLanguage].disk_size += depot.size;
                                    }
                                    else
                                    {
                                        if (manifest.dlcs.ContainsKey(depot.productId))
                                        {
                                            if (!manifest.dlcs[depot.productId].size.ContainsKey(newLanguage))
                                            {
                                                manifest.dlcs[depot.productId].size.Add(newLanguage, new GogGameMetaManifest.SizeType());
                                            }
                                            manifest.dlcs[depot.productId].size[newLanguage].download_size += depot.compressedSize;
                                            manifest.dlcs[depot.productId].size[newLanguage].disk_size += depot.size;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            manifest.errorDisplayed = true;
                            logger.Error($"An error occurred while downloading {downloadData.name} meta manifest.");
                        }
                        File.WriteAllText(cacheInfoFile, Serialization.ToJson(manifest));
                    }
                }
            }
            return manifest;
        }

        public static async Task<GogBuildsData> GetGameBuilds(string gameId, string platform = "windows")
        {
            var newBuildsInfoContent = new GogBuildsData();
            var logger = LogManager.GetLogger();
            string content = null;
            var cacheInfoPath = GogOssLibrary.Instance.GetCachePath("infocache");
            var cacheInfoFile = Path.Combine(cacheInfoPath, $"{gameId}_builds.json");
            if (File.Exists(cacheInfoFile))
            {
                if (File.GetLastWriteTime(cacheInfoFile) < DateTime.Now.AddDays(-7))
                {
                    File.Delete(cacheInfoFile);
                }
            }
            bool correctJson = false;
            if (File.Exists(cacheInfoFile))
            {
                content = FileSystem.ReadFileAsStringSafe(cacheInfoFile);
                if (!content.IsNullOrWhiteSpace() && Serialization.TryFromJson(content, out GogBuildsData buildsInfoContent))
                {
                    if (buildsInfoContent != null && buildsInfoContent.items.Count > 0)
                    {
                        correctJson = true;
                    }
                }
            }
            if (!correctJson)
            {
                if (!Directory.Exists(cacheInfoPath))
                {
                    Directory.CreateDirectory(cacheInfoPath);
                }
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var response = await httpClient.GetAsync($"https://content-system.gog.com/products/{gameId}/os/{platform}/builds?generation=2");
                if (response.IsSuccessStatusCode)
                {
                    content = await response.Content.ReadAsStringAsync();
                    if (!Directory.Exists(cacheInfoPath))
                    {
                        Directory.CreateDirectory(cacheInfoPath);
                    }
                    newBuildsInfoContent = Serialization.FromJson<GogBuildsData>(content);
                    if (newBuildsInfoContent.items.Count > 0)
                    {
                        foreach (var build in newBuildsInfoContent.items)
                        {
                            if (build.branch.IsNullOrEmpty())
                            {
                                build.branch = "";
                            }
                            newBuildsInfoContent.available_branches.AddMissing(build.branch);
                        }
                    }
                    var strConf = Serialization.ToJson(newBuildsInfoContent, false);
                    File.WriteAllText(cacheInfoFile, strConf);
                }
                else
                {
                    newBuildsInfoContent.errorDisplayed = true;
                    logger.Error($"An error occurred while downloading {gameId} builds info.");
                }
            }
            else
            {
                newBuildsInfoContent = Serialization.FromJson<GogBuildsData>(content);
            }
            return newBuildsInfoContent;
        }

        public static async Task<GogDownloadRedistManifest.Depot> GetRedistInfo(string gameId, string version = "2", bool skipRefreshing = false, bool silently = false, bool forceRefreshCache = false)
        {
            var cacheInfoPath = GogOssLibrary.Instance.GetCachePath("metacache");
            var cacheInfoFileName = $"redist_v{version}.json";
            var cacheInfoFile = Path.Combine(cacheInfoPath, cacheInfoFileName);
            var redistManifest = new GogDownloadRedistManifest.Depot();
            var manifest = new GogDownloadRedistManifest();
            var playniteAPI = API.Instance;
            var logger = LogManager.GetLogger();
            bool correctJson = false;
            if (File.Exists(cacheInfoFile))
            {
                logger.Debug(cacheInfoFile);
                if (!skipRefreshing)
                {
                    if (File.GetLastWriteTime(cacheInfoFile) < DateTime.Now.AddDays(-7) || forceRefreshCache)
                    {
                        File.Delete(cacheInfoFile);
                    }
                }
            }
            if (File.Exists(cacheInfoFile))
            {
                var content = FileSystem.ReadFileAsStringSafe(cacheInfoFile);
                if (!content.IsNullOrWhiteSpace() && Serialization.TryFromJson(content, out manifest))
                {
                    if (manifest != null)
                    {
                        correctJson = true;
                    }
                }
            }
            if (!correctJson)
            {
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var dependsURL = "https://content-system.gog.com/dependencies/repository?generation=2";
                if (version == "1")
                {
                    dependsURL = "https://content-system.gog.com/redists/repository";
                }
                var response = await httpClient.GetAsync(dependsURL);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (!content.IsNullOrWhiteSpace())
                    {
                        var jsonResponse = Serialization.FromJson<Dictionary<string, string>>(content);
                        var manifestUrl = jsonResponse["repository_manifest"];
                        if (!manifestUrl.IsNullOrEmpty())
                        {
                            var manifestResponse = await httpClient.GetAsync(manifestUrl);
                            Stream manifestContent = null;
                            if (response.IsSuccessStatusCode)
                            {
                                manifestContent = await manifestResponse.Content.ReadAsStreamAsync();
                            }
                            else
                            {
                                logger.Error("An error occured while dowloading depends manifest");
                            }
                            if (!Directory.Exists(cacheInfoPath))
                            {
                                Directory.CreateDirectory(cacheInfoPath);
                            }
                            var manifestResult = Helpers.DecompressZlib(manifestContent);
                            if (!manifestResult.IsNullOrWhiteSpace())
                            {
                                if (Serialization.TryFromJson(manifestResult, out manifest))
                                {
                                    if (manifest != null)
                                    {
                                        correctJson = true;
                                    }
                                }
                                logger.Debug("Hurarr");
                                FileSystem.WriteStringToFileSafe(cacheInfoFile, manifestResult);
                            }
                        }
                    }
                }
                else
                {
                    logger.Error("An error occured while dowloading depends manifest");
                }
            }
            if (correctJson)
            {
                var depots = manifest.depots;
                redistManifest = depots.First(d => d.dependencyId == gameId);
                redistManifest.build_id = manifest.build_id;
            }
            return redistManifest;
        }
    }
}
