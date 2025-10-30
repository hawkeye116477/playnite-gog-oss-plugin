using CommonPlugin;
using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace GogOssLibraryNS.Services
{
    public class GogDownloadApi
    {
        private IPlayniteAPI playniteAPI = API.Instance;
        private ILogger logger = LogManager.GetLogger();
        public static readonly HttpClient Client = new HttpClient();
        public static string UserAgent => @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        public async Task<GogBuildsData> GetProductBuilds(DownloadManagerData.Download downloadInfo, bool forceRefreshCache = false)
        {
            return await GetProductBuilds(downloadInfo.gameID, downloadInfo.downloadProperties.os, forceRefreshCache);
        }

        public async Task<GogBuildsData> GetProductBuilds(string gameId, string platform = "windows", bool forceRefreshCache = false)
        {
            var cachePath = GogOssLibrary.Instance.GetCachePath("downloadbuildscache");

            var newBuildsInfoContent = new GogBuildsData();
            string content = null;

            var cacheInfoFile = Path.Combine(cachePath, $"{gameId}_builds.json");

            if (File.Exists(cacheInfoFile))
            {
                if (File.GetLastWriteTime(cacheInfoFile) < DateTime.Now.AddDays(-7) || forceRefreshCache)
                {
                    File.Delete(cacheInfoFile);
                }
            }

            bool correctJson = false;
            if (File.Exists(cacheInfoFile))
            {
                content = File.ReadAllText(cacheInfoFile);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    if (Serialization.TryFromJson(content, out GogBuildsData buildsInfoContent))
                    {
                        if (buildsInfoContent.items != null && buildsInfoContent.items.Count > 0)
                        {
                            correctJson = true;
                            newBuildsInfoContent = buildsInfoContent;
                        }
                    }
                }
            }

            if (!correctJson)
            {
                if (!Directory.Exists(cachePath))
                {
                    Directory.CreateDirectory(cachePath);
                }

                Client.DefaultRequestHeaders.Clear();
                Client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var response =
                    await Client.GetAsync(
                        $"https://content-system.gog.com/products/{gameId}/os/{platform}/builds?generation=2");
                if (response.IsSuccessStatusCode)
                {
                    content = await response.Content.ReadAsStringAsync();
                    if (!content.IsNullOrWhiteSpace())
                    {
                        newBuildsInfoContent = Serialization.FromJson<GogBuildsData>(content);
                        if (newBuildsInfoContent.items != null && newBuildsInfoContent.items.Count > 0)
                        {
                            foreach (var build in newBuildsInfoContent.items)
                            {
                                if (string.IsNullOrEmpty(build.branch))
                                {
                                    build.branch = "";
                                }

                                if (!newBuildsInfoContent.available_branches.Contains(build.branch))
                                {
                                    newBuildsInfoContent.available_branches.Add(build.branch);
                                }
                            }
                            newBuildsInfoContent.items = newBuildsInfoContent.items.OrderByDescending(p => p.date_published).ToList();
                            var buildsInfoContentString = Serialization.ToJson(newBuildsInfoContent);
                            File.WriteAllText(cacheInfoFile, buildsInfoContentString);
                        }
                    }
                }
                else
                {
                    newBuildsInfoContent.errorDisplayed = true;
                    logger.Error($"[GOG OSS] An error occurred while downloading {gameId} builds info.");
                }
            }
            return newBuildsInfoContent;
        }


        public async Task<GogGameMetaManifest> GetGameMetaManifest(DownloadManagerData.Download downloadData, bool forceRefreshCache = false)
        {
            return await GetGameMetaManifest(downloadData.gameID, downloadData.downloadProperties.buildId, downloadData.downloadProperties.betaChannel, downloadData.downloadProperties.os, forceRefreshCache, downloadData.downloadItemType);
        }

        public async Task<GogGameMetaManifest> GetGameMetaManifest(string gameId, Installed installedInfo, bool skipRefreshing = false, bool silently = false, bool forceRefreshCache = false, DownloadItemType downloadItemType = DownloadItemType.Game)
        {
            var downloadData = new DownloadManagerData.Download
            {
                gameID = gameId,
                name = installedInfo.title,
                downloadItemType = downloadItemType
            };
            downloadData.downloadProperties.buildId = installedInfo.build_id;
            downloadData.downloadProperties.os = installedInfo.platform;
            return await GetGameMetaManifest(downloadData);
        }

        public async Task<GogGameMetaManifest> GetGameMetaManifest(
            string gameId, string buildId = "", string branch = "", string platform = "windows", bool forceRefreshCache = false, DownloadItemType downloadItemType = DownloadItemType.Game)
        {
            var manifest = new GogGameMetaManifest();
            var cacheInfoFileName = $"{gameId}.json";

            if (buildId != "")
            {
                cacheInfoFileName = $"{gameId}_build{buildId}.json";
            }
            else if (downloadItemType == DownloadItemType.Game)
            {
                var builds = await GetProductBuilds(gameId, platform);
                if (builds.items.Count > 0)
                {
                    Client.DefaultRequestHeaders.Clear();
                    Client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                    string chosenBranch = branch;
                    if (chosenBranch == "disabled")
                    {
                        chosenBranch = "";
                    }
                    var selectedBuild = builds.items.FirstOrDefault(i => i.branch == chosenBranch);
                    if (selectedBuild != null)
                    {
                        var newBuildId = selectedBuild.legacy_build_id;
                        if (newBuildId.IsNullOrEmpty())
                        {
                            newBuildId = selectedBuild.build_id;
                        }
                        cacheInfoFileName = $"{gameId}_build{newBuildId}.json";
                    }
                }
            }

            var cachePath = GogOssLibrary.Instance.GetCachePath("manifests");
            var cacheInfoFile = Path.Combine(cachePath, cacheInfoFileName);
            bool correctJson = false;
            if (File.Exists(cacheInfoFile))
            {
                if (File.GetLastWriteTime(cacheInfoFile) < DateTime.Now.AddDays(-7) || forceRefreshCache)
                {
                    File.Delete(cacheInfoFile);
                }
            }
            var newManifest = new GogGameMetaManifest();
            if (File.Exists(cacheInfoFile))
            {
                var content = File.ReadAllText(cacheInfoFile);
                if (!string.IsNullOrWhiteSpace(content) && Serialization.TryFromJson(content, out newManifest))
                {
                    if (newManifest != null)
                    {
                        correctJson = true;
                        manifest = newManifest;
                    }
                }
            }

            if (!correctJson)
            {
                if (!Directory.Exists(cachePath))
                {
                    Directory.CreateDirectory(cachePath);
                }
                if (downloadItemType == DownloadItemType.Dependency)
                {
                    var redistManifest = await GetRedistInfo(gameId, "2", false, forceRefreshCache);
                    manifest.executable = redistManifest.executable;
                    manifest.buildId = redistManifest.build_id;
                    manifest.size = new Dictionary<string, GogGameMetaManifest.SizeType>();
                    manifest.readableName = redistManifest.readableName;
                    var redistSizes = new GogGameMetaManifest.SizeType
                    {
                        disk_size = redistManifest.size,
                        download_size = redistManifest.compressedSize
                    };
                    manifest.size.Add("*", redistSizes);
                    manifest.executable = redistManifest.executable;
                    return manifest;
                }

                var builds = await GetProductBuilds(gameId, platform);
                if (builds.items.Count > 0)
                {
                    Client.DefaultRequestHeaders.Clear();
                    Client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                    string chosenBranch = branch;
                    var chosenBuildId = buildId;
                    if (chosenBranch == "disabled")
                    {
                        chosenBranch = "";
                    }
                    var selectedBuild = builds.items.FirstOrDefault(i => i.branch == chosenBranch && (string.IsNullOrEmpty(chosenBuildId) || i.build_id == chosenBuildId || i.legacy_build_id == chosenBuildId));
                    if (selectedBuild != null)
                    {
                        var newBuildId = selectedBuild.legacy_build_id;
                        if (newBuildId.IsNullOrEmpty())
                        {
                            newBuildId = selectedBuild.build_id;
                        }

                        cacheInfoFileName = $"{gameId}_build{newBuildId}.json";
                        cacheInfoFile = Path.Combine(cachePath, cacheInfoFileName);
                        var response = await Client.GetAsync(selectedBuild.link);
                        Stream content = null;
                        if (response.IsSuccessStatusCode)
                        {
                            content = await response.Content.ReadAsStreamAsync();
                        }
                        else
                        {
                            manifest.errorDisplayed = true;
                            return manifest;
                        }

                        var result = "";
                        if (selectedBuild.generation >= 2)
                        {
                            result = Helpers.DecompressZlib(content);
                        }
                        else
                        {
                            using var reader = new StreamReader(content);
                            result = await reader.ReadToEndAsync();
                        }
                        var gogAccountClient = new GogAccountClient();
                        if (!string.IsNullOrWhiteSpace(result))
                        {
                            manifest = Serialization.FromJson<GogGameMetaManifest>(result);
                            if (manifest != null)
                            {
                                manifest.size = new Dictionary<string, GogGameMetaManifest.SizeType>();
                                manifest.languages = new List<string>();
                                manifest.versionName = selectedBuild.version_name;
                                var ownedItems = await gogAccountClient.GetOwnedIds();
                                if (manifest.version == 2)
                                {
                                    foreach (var product in manifest.products)
                                    {
                                        if (product.productId != gameId)
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
                                else if (manifest.version == 1)
                                {
                                    manifest.installDirectory = manifest.product.installDirectory;
                                    manifest.buildId = newBuildId;
                                    foreach (var product in manifest.product.gameIDs)
                                    {
                                        if (product.gameID != gameId)
                                        {
                                            if (ownedItems.Contains(int.Parse(product.gameID)))
                                            {
                                                var dlc = new GogGameMetaManifest.Dlc
                                                {
                                                    title = product.name["en"]
                                                };
                                                manifest.dlcs.Add(product.gameID, dlc);
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    logger.Debug($"Unsupported manifest version: {manifest.version}. Please report that.");
                                }

                                var depots = new List<GogGameMetaManifest.Depot>();
                                if (manifest.version == 1)
                                {
                                    depots = manifest.product.depots;
                                }
                                else if (manifest.version == 2)
                                {
                                    depots = manifest.depots;
                                }
                                if (!manifest.size.ContainsKey("*"))
                                {
                                    manifest.size.Add("*", new GogGameMetaManifest.SizeType());
                                }
                                foreach (var depot in depots)
                                {
                                    if (!depot.targetDir.IsNullOrEmpty())
                                    {
                                        var redistManifest = await GetRedistInfo(depot.redist, "2", false, forceRefreshCache);
                                        depot.size = redistManifest.size;
                                        depot.compressedSize = redistManifest.compressedSize;
                                        depot.languages.Add("*");
                                    }
                                    foreach (var language in depot.languages.ToList())
                                    {
                                        var newLanguage = language;
                                        if (newLanguage == "Neutral")
                                        {
                                            newLanguage = "*";
                                            depot.languages.Remove("Neutral");
                                            depot.languages.Add("*");
                                        }

                                        if (newLanguage != "*")
                                        {
                                            if (!manifest.languages.Contains(newLanguage))
                                            {
                                                manifest.languages.Add(newLanguage);
                                            }
                                        }

                                        if (!manifest.size.ContainsKey(newLanguage))
                                        {
                                            manifest.size.Add(newLanguage, new GogGameMetaManifest.SizeType());
                                        }

                                        if (manifest.version == 2)
                                        {
                                            if (manifest.dlcs.ContainsKey(depot.productId))
                                            {
                                                if (!manifest.dlcs[depot.productId].size.ContainsKey(newLanguage))
                                                {
                                                    manifest.dlcs[depot.productId].size.Add(newLanguage,
                                                        new GogGameMetaManifest.SizeType());
                                                }

                                                manifest.dlcs[depot.productId].size[newLanguage].download_size +=
                                                    depot.compressedSize;
                                                manifest.dlcs[depot.productId].size[newLanguage].disk_size += depot.size;
                                            }
                                            else if (depot.productId == gameId)
                                            {
                                                manifest.size[newLanguage].download_size += depot.compressedSize;
                                                manifest.size[newLanguage].disk_size += depot.size;
                                            }
                                        }
                                        else if (manifest.version == 1)
                                        {
                                            // Manifest V1 hasn't compression
                                            if (depot.gameIDs.Any(sgame => manifest.dlcs.ContainsKey(sgame)))
                                            {
                                                if (!manifest.dlcs[depot.productId].size.ContainsKey(newLanguage))
                                                {
                                                    manifest.dlcs[depot.productId].size.Add(newLanguage,
                                                        new GogGameMetaManifest.SizeType());
                                                }
                                                manifest.dlcs[depot.productId].size[newLanguage].download_size +=
                                                    depot.size;
                                                manifest.dlcs[depot.productId].size[newLanguage].disk_size += depot.size;
                                            }
                                            else if (depot.gameIDs.Contains(gameId) || !depot.targetDir.IsNullOrEmpty())
                                            {
                                                manifest.size[newLanguage].disk_size += depot.size;
                                                if (depot.compressedSize == 0)
                                                {
                                                    manifest.size[newLanguage].download_size += depot.size;
                                                }
                                                else
                                                {
                                                    manifest.size[newLanguage].download_size += depot.compressedSize;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            manifest.errorDisplayed = true;
                        }

                        File.WriteAllText(cacheInfoFile, Serialization.ToJson(manifest));
                    }
                }
            }

            return manifest;
        }

        public async Task<List<string>> GetNeededDepotManifestHashes(DownloadManagerData.Download taskData)
        {
            List<string> depotHashes = new List<string>();
            var metaManifest = await GetGameMetaManifest(taskData);
            var depots = metaManifest.depots;
            if (metaManifest.version == 1)
            {
                depots = metaManifest.product.depots;
            }
            foreach (var depot in depots)
            {
                var chosenlanguage = taskData.downloadProperties.language;
                if (string.IsNullOrEmpty(chosenlanguage))
                {
                    chosenlanguage = metaManifest.languages.First();
                }

                if (depot.languages.Contains(chosenlanguage) || depot.languages.Contains("*"))
                {
                    var productIds = new List<string>
                    {
                        taskData.gameID
                    };
                    if (taskData.downloadProperties.extraContent != null && taskData.downloadProperties.extraContent.Count > 0)
                    {
                        productIds.AddRange(taskData.downloadProperties.extraContent);
                    }

                    var manifestHash = depot.manifest;
                    if (metaManifest.version == 2 && productIds.Contains(depot.productId))
                    {
                        depotHashes.Add(manifestHash);
                    }
                    else if (metaManifest.version == 1 && depot.gameIDs.Any(sgame => productIds.Contains(sgame)))
                    {
                        depotHashes.Add(manifestHash);
                    }
                }
            }

            return depotHashes;
        }
        public string GetGalaxyPath(string manifestHash)
        {
            var galaxyPath = manifestHash;
            if (galaxyPath.IndexOf("/") == -1)
            {
                galaxyPath = manifestHash[..2] + "/" + manifestHash.Substring(2, 2) + "/" + galaxyPath;
            }
            return galaxyPath;
        }

        public async Task<GogDepot> GetDepotInfo(string manifest, DownloadManagerData.Download taskData, int version = 2)
        {
            var cachePath = GogOssLibrary.Instance.GetCachePath("depotcache");
            var depotManifest = new GogDepot();
            if (version == 1)
            {
                manifest = manifest.Replace(".json", "");
            }
            var cacheInfoFileName = $"depot_{manifest}.json";
            var cacheInfoFile = Path.Combine(cachePath, cacheInfoFileName);
            bool correctJson = false;
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
                if (!string.IsNullOrWhiteSpace(content) && Serialization.TryFromJson(content, out GogDepot newManifest))
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
                if (!Directory.Exists(cachePath))
                {
                    Directory.CreateDirectory(cachePath);
                }

                Client.DefaultRequestHeaders.Clear();
                Client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var url = $"https://cdn.gog.com/content-system/v2/meta";
                if (version == 1 && taskData.downloadItemType == DownloadItemType.Game)
                {
                    url = $"https://cdn.gog.com/content-system/v1/manifests/{taskData.gameID}/{taskData.downloadProperties.os}/{taskData.downloadProperties.buildId}";
                }
                if (taskData.downloadItemType == DownloadItemType.Dependency)
                {
                    url = $"https://cdn.gog.com/content-system/v{version}/dependencies/meta";
                }
                var fullUrl = $"{url}/{GetGalaxyPath(manifest)}";
                if (version == 1 && taskData.downloadItemType == DownloadItemType.Game)
                {
                    fullUrl = $"{url}/{manifest}.json";
                }
                var response = await Client.GetAsync(fullUrl);
                Stream content;
                if (response.IsSuccessStatusCode)
                {
                    content = await response.Content.ReadAsStreamAsync();
                }
                else
                {
                    logger.Error($"An error occurred while downloading {manifest} depot manifest.");
                    return depotManifest;
                }

                var result = "";
                if (version == 2)
                {
                    result = Helpers.DecompressZlib(content);
                }
                else
                {
                    using var reader = new StreamReader(content);
                    result = await reader.ReadToEndAsync();
                }
                File.WriteAllText(cacheInfoFile, result);
                depotManifest = Serialization.FromJson<GogDepot>(result);
            }

            return depotManifest;
        }


        public async Task<List<string>> GetSecureLinks(DownloadManagerData.Download taskData, string path = "/")
        {
            List<string> urls = new List<string>();
            var url = "";
            var metaManifest = await GetGameMetaManifest(taskData);
            if (taskData.downloadItemType == DownloadItemType.Game)
            {
                if (metaManifest.version == 2)
                {
                    url = $"https://content-system.gog.com/products/{taskData.gameID}/secure_link?generation=2&path={path}&_version=2";
                }
                else
                {
                    url = $"https://content-system.gog.com/products/{taskData.gameID}/secure_link?_version=2&type=depot&path={path}{taskData.downloadProperties.os}/{taskData.downloadProperties.buildId}";
                }
            }
            else
            {
                url = $"https://content-system.gog.com/open_link?generation=2&_version=2&path=/dependencies/store/{path}";
            }

            var gogAccountClient = new GogAccountClient();
            if (await gogAccountClient.GetIsUserLoggedIn())
            {
                var tokens = gogAccountClient.LoadTokens();
                Client.DefaultRequestHeaders.Clear();
                Client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + tokens.access_token);
                var response = await Client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        if (Serialization.TryFromJson<GogSecureLinks>(content, out var validJsonResponse))
                        {
                            foreach (var endpoint in validJsonResponse.urls)
                            {
                                var newUrl = endpoint.url_format;
                                if (taskData.downloadItemType == DownloadItemType.Game)
                                {
                                    foreach (var key in endpoint.parameters.Keys)
                                    {
                                        var keyValue = endpoint.parameters[key].ToString();
                                        if (key == "path")
                                        {
                                            keyValue += "/{GALAXY_PATH}";
                                        }
                                        newUrl = newUrl.Replace('{' + key + '}', keyValue);
                                    }
                                }
                                else
                                {
                                    newUrl += "/{GALAXY_PATH}";
                                }
                                urls.Add(newUrl);
                            }
                        }
                    }
                }
            }
            else
            {
                playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.ThirdPartyGogNotLoggedInError), "");
                logger.Error($"Can't get secure links, cuz user is not authenticated.");
            }
            return urls;
        }

        public static async Task<GogDownloadRedistManifest.Depot> GetRedistInfo(string dependId, string version = "2", bool skipRefreshing = false, bool forceRefreshCache = false)
        {
            var cacheInfoPath = GogOssLibrary.Instance.GetCachePath("manifests");
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
                Client.DefaultRequestHeaders.Clear();
                Client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var dependsURL = "https://content-system.gog.com/dependencies/repository?generation=2";
                if (version == "1")
                {
                    dependsURL = "https://content-system.gog.com/redists/repository";
                }
                var response = await Client.GetAsync(dependsURL);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (!content.IsNullOrWhiteSpace())
                    {
                        var jsonResponse = Serialization.FromJson<Dictionary<string, string>>(content);
                        var manifestUrl = jsonResponse["repository_manifest"];
                        if (!manifestUrl.IsNullOrEmpty())
                        {
                            var manifestResponse = await Client.GetAsync(manifestUrl);
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
                redistManifest = depots.First(d => d.dependencyId == dependId);
                redistManifest.build_id = manifest.build_id;
            }
            return redistManifest;
        }
    }
}
