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
using System.Net.Http;
using System.Threading.Tasks;

namespace GogOssLibraryNS.Services
{
    public class GogDownloadApi
    {
        private IPlayniteAPI playniteAPI = API.Instance;
        private ILogger logger = LogManager.GetLogger();
        private static readonly RetryHandler retryHandler = new(new HttpClientHandler());
        public static readonly HttpClient Client = new(retryHandler);

        public static string UserAgent => $"Playnite/{GogOssTroubleshootingInformation.PlayniteVersion}";

        static GogDownloadApi()
        {
            Client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        }

        public async Task<GogBuildsData> GetProductBuilds(DownloadManagerData.Download downloadInfo, bool forceRefreshCache = false)
        {
            if (downloadInfo.downloadItemType == DownloadItemType.Game)
            {
                return await GetProductBuilds(downloadInfo.gameID, downloadInfo.downloadProperties.os, forceRefreshCache);
            }
            else
            {
                return new GogBuildsData();
            }
        }

        public async Task<GogBuildsData> GetProductBuilds(string gameId, string platform = "windows", bool forceRefreshCache = false)
        {
            var cachePath = GogOssLibrary.Instance.GetCachePath("builds");

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

                try
                {
                    using var response = await Client.GetAsync($"https://content-system.gog.com/products/{gameId}/os/{platform}/builds?generation=2");
                    response.EnsureSuccessStatusCode();
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
                        else
                        {
                            newBuildsInfoContent.installable = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"[GOG OSS] An error occurred while downloading {gameId} builds info:");
                    newBuildsInfoContent.errorDisplayed = true;
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

            var cachePath = GogOssLibrary.Instance.GetCachePath("meta");
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
                    var redistManifest = await GetRedistInfo(gameId, false, forceRefreshCache);
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

                        var result = "";
                        try
                        {
                            using var response = await Client.GetAsync(selectedBuild.link);
                            response.EnsureSuccessStatusCode();
                            using var content = await response.Content.ReadAsStreamAsync();
                            if (selectedBuild.generation >= 2)
                            {
                                result = await Helpers.DecompressZlib(content);
                            }
                            else
                            {
                                using var reader = new StreamReader(content);
                                result = await reader.ReadToEndAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex.Message);
                            manifest.errorDisplayed = true;
                            return manifest;
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
                                    logger.Error($"Unsupported manifest version: {manifest.version}. Please report that.");
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
                                        var redistManifest = await GetRedistInfo(depot.redist, false, forceRefreshCache);
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

        public async Task<Dictionary<string, List<string>>> GetNeededDepotManifestHashes(DownloadManagerData.Download taskData)
        {
            Dictionary<string, List<string>> depotHashes = new();
            var metaManifest = await GetGameMetaManifest(taskData);
            var depots = metaManifest.depots;
            if (metaManifest.version == 1)
            {
                depots = metaManifest.product.depots;
            }
            var productIds = new List<string> { taskData.gameID };
            if (taskData.downloadProperties.extraContent != null && taskData.downloadProperties.extraContent.Count > 0)
            {
                productIds.AddRange(taskData.downloadProperties.extraContent);
            }

            foreach (var depot in depots)
            {
                var chosenlanguage = taskData.downloadProperties.language;
                if (string.IsNullOrEmpty(chosenlanguage) && metaManifest.languages.Count > 0)
                {
                    chosenlanguage = metaManifest.languages.First();
                }

                if (depot.languages.Contains(chosenlanguage) || depot.languages.Contains("*"))
                {
                    var manifestHash = depot.manifest;
                    if (metaManifest.version == 2 && productIds.Contains(depot.productId))
                    {
                        if (!depotHashes.ContainsKey(depot.productId))
                        {
                            depotHashes.Add(depot.productId, new List<string>());
                        }
                        depotHashes[depot.productId].Add(manifestHash);
                    }
                    else if (metaManifest.version == 1 && depot.gameIDs.Any(sgame => productIds.Contains(sgame)))
                    {
                        if (!depotHashes.ContainsKey(depot.gameIDs[0]))
                        {
                            depotHashes.Add(depot.gameIDs[0], new List<string>());
                        }
                        depotHashes[depot.gameIDs[0]].Add(manifestHash);
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

        public async Task<GogDepot> GetDepotInfo(string manifest, DownloadManagerData.Download taskData, int version = 2, bool isPatch = false)
        {
            var cachePath = GogOssLibrary.Instance.GetCachePath("depot");
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

                var url = $"https://cdn.gog.com/content-system/v2/meta";
                if (isPatch)
                {
                    url = $"https://cdn.gog.com/content-system/v{version}/patches/meta";
                }
                else if (version == 1 && taskData.downloadItemType == DownloadItemType.Game)
                {
                    url = $"https://cdn.gog.com/content-system/v1/manifests/{taskData.gameID}/{taskData.downloadProperties.os}/{taskData.downloadProperties.buildId}";
                }
                else if (taskData.downloadItemType == DownloadItemType.Dependency)
                {
                    url = $"https://cdn.gog.com/content-system/v{version}/dependencies/meta";
                }
                var fullUrl = $"{url}/{GetGalaxyPath(manifest)}";
                if (version == 1 && taskData.downloadItemType == DownloadItemType.Game)
                {
                    fullUrl = $"{url}/{manifest}.json";
                }

                var result = "";
                try
                {
                    using var response = await Client.GetAsync(fullUrl);
                    response.EnsureSuccessStatusCode();
                    using Stream content = await response.Content.ReadAsStreamAsync();
                    if (version == 2)
                    {
                        result = await Helpers.DecompressZlib(content);
                    }
                    else
                    {
                        using var reader = new StreamReader(content);
                        result = await reader.ReadToEndAsync();
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"An error occurred while downloading {manifest} depot manifest from {fullUrl}: {ex}.");
                    return depotManifest;
                }

                if (result.IsNullOrEmpty())
                {
                    return depotManifest;
                }
                depotManifest = Serialization.FromJson<GogDepot>(result);
                if (depotManifest.depot.files.Count > 0)
                {
                    foreach (var depotFile in depotManifest.depot.files)
                    {
                        depotFile.path = depotFile.path.TrimStart('/', '\\');
                    }
                }
                if (depotManifest.depot.items.Count > 0)
                {
                    foreach (var depotItem in depotManifest.depot.items)
                    {
                        depotItem.path = depotItem.path.TrimStart('/', '\\');
                    }
                }
                File.WriteAllText(cacheInfoFile, Serialization.ToJson(depotManifest));
            }

            return depotManifest;
        }

        public async Task<Dictionary<string, List<GogSecureLinks.FinalUrl>>> GetSecureLinksForAllProducts(DownloadManagerData.Download taskData, bool isPatch = false)
        {
            Dictionary<string, List<GogSecureLinks.FinalUrl>> allSecureLinks = new();
            List<string> productIds = new();
            productIds.Add(taskData.gameID);
            if (taskData.downloadProperties.extraContent.Count > 0)
            {
                foreach (var dlc in taskData.downloadProperties.extraContent)
                {
                    productIds.Add(dlc);
                }
            }
            foreach (var productId in productIds)
            {
                var clonedTaskData = Serialization.GetClone(taskData);
                var dlcData = new DownloadManagerData.Download
                {
                    gameID = productId
                };
                dlcData.downloadProperties = clonedTaskData.downloadProperties;
                dlcData.downloadItemType = clonedTaskData.downloadItemType;
                var securelinks = await GetSecureLinks(dlcData, isPatch);
                if (securelinks.Count > 0)
                {
                    allSecureLinks.Add(productId, securelinks);
                }
            }
            return allSecureLinks;
        }


        public async Task<List<GogSecureLinks.FinalUrl>> GetSecureLinks(DownloadManagerData.Download taskData, bool isPatch = false)
        {
            List<GogSecureLinks.FinalUrl> urls = new();
            var url = "";
            var metaManifest = new GogGameMetaManifest();
            if (taskData.downloadItemType != DownloadItemType.Dependency)
            {
                metaManifest = await GetGameMetaManifest(taskData);
            }
            if (isPatch)
            {
                url = $"https://content-system.gog.com/products/{taskData.gameID}/secure_link?_version=2&generation=2&path=/&root=/patches/store";
            }
            else if (taskData.downloadItemType == DownloadItemType.Game)
            {
                if (metaManifest.version == 2)
                {
                    url = $"https://content-system.gog.com/products/{taskData.gameID}/secure_link?_version=2&generation=2&path=/";
                }
                else
                {
                    url = $"https://content-system.gog.com/products/{taskData.gameID}/secure_link?_version=2&type=depot&path=/{taskData.downloadProperties.os}/{taskData.downloadProperties.buildId}";
                }
            }
            else if (taskData.downloadItemType == DownloadItemType.Dependency)
            {
                url = $"https://content-system.gog.com/open_link?generation=2&_version=2&path=/dependencies/store/";
            }

            var gogAccountClient = new GogAccountClient();
            if (await gogAccountClient.GetIsUserLoggedIn())
            {
                var tokens = gogAccountClient.LoadTokens();
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("Authorization", $"Bearer {tokens.access_token}");
                    using var response = await Client.SendAsync(request);
                    response.EnsureSuccessStatusCode();
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
                                var newFinalUrl = new GogSecureLinks.FinalUrl
                                {
                                    formatted_url = newUrl,
                                    endpoint_name = endpoint.endpoint_name
                                };
                                urls.Add(newFinalUrl);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Error($"An error occured while getting secure links: {ex}.");
                }
            }
            else
            {
                playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.ThirdPartyGogNotLoggedInError), "");
                logger.Error($"Can't get secure links, cuz user is not authenticated.");
            }
            return urls;
        }

        public static async Task<GogRedistManifest.Depot> GetRedistInfo(string dependId, bool skipRefreshing = false, bool forceRefreshCache = false)
        {
            var version = "2";
            var cacheInfoPath = GogOssLibrary.Instance.GetCachePath("redist");
            var cacheInfoFileName = $"redist_v{version}.json";
            var cacheInfoFile = Path.Combine(cacheInfoPath, cacheInfoFileName);
            var redistManifest = new GogRedistManifest.Depot();
            var manifest = new GogRedistManifest();
            var playniteAPI = API.Instance;
            var logger = LogManager.GetLogger();
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
                var dependsURL = "https://content-system.gog.com/dependencies/repository?generation=2";
                if (version == "1")
                {
                    dependsURL = "https://content-system.gog.com/redists/repository";
                }

                using var response = await Client.GetAsync(dependsURL);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (!content.IsNullOrWhiteSpace())
                    {
                        var jsonResponse = Serialization.FromJson<Dictionary<string, string>>(content);
                        var manifestUrl = jsonResponse["repository_manifest"];
                        string manifestResult = "";
                        if (!manifestUrl.IsNullOrEmpty())
                        {
                            try
                            {
                                using var manifestResponse = await Client.GetAsync(manifestUrl, HttpCompletionOption.ResponseHeadersRead);
                                manifestResponse.EnsureSuccessStatusCode();
                                using var manifestContent = await manifestResponse.Content.ReadAsStreamAsync();

                                if (manifestContent != null)
                                {
                                    manifestResult = await Helpers.DecompressZlib(manifestContent);
                                }
                            }

                            catch (Exception ex)
                            {
                                logger.Error("An error occured while dowloading depends manifest");
                            }

                            if (!manifestResult.IsNullOrWhiteSpace() && Serialization.TryFromJson(manifestResult, out manifest))
                            {
                                if (manifest != null)
                                {
                                    correctJson = true;
                                }
                                if (!Directory.Exists(cacheInfoPath))
                                {
                                    Directory.CreateDirectory(cacheInfoPath);
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
                redistManifest = depots.FirstOrDefault(d => d.dependencyId == dependId);
                if (redistManifest != null)
                {
                    redistManifest.build_id = manifest.build_id;
                }
                else
                {
                    redistManifest = new();
                    logger.Error($"Unrecognized dependency: {dependId}. Please clear cache or report that if wont help.");
                }
            }
            return redistManifest;
        }

        public async Task<GogGameMetaManifest> GetGogPatchMetaManifest(string gameId, string oldBuildId, string newBuildId)
        {
            var manifest = new GogGameMetaManifest();
            var cacheInfoFileName = $"{gameId}_from{oldBuildId}_to{oldBuildId}.json";
            var cachePath = GogOssLibrary.Instance.GetCachePath("meta_patches");
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
                if (!string.IsNullOrWhiteSpace(content) && Serialization.TryFromJson(content, out GogGameMetaManifest newManifest))
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

                using var response = await Client.GetAsync(
                        $"https://content-system.gog.com/products/{gameId}/patches?_version=4&from_build_id={oldBuildId}&to_build_id={newBuildId}");
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        var jsonResponse = Serialization.FromJson<PatchResponse>(content);
                        if (!jsonResponse.link.IsNullOrEmpty())
                        {
                            using var finalResponse = await Client.GetAsync(jsonResponse.link, HttpCompletionOption.ResponseHeadersRead);
                            if (finalResponse.IsSuccessStatusCode)
                            {
                                using Stream metaContent = await finalResponse.Content.ReadAsStreamAsync();
                                var result = await Helpers.DecompressZlib(metaContent);
                                if (!result.IsNullOrWhiteSpace())
                                {
                                    if (result == "{}")
                                    {
                                        logger.Warn("Empty manifest for patch.");
                                    }
                                    manifest = Serialization.FromJson<GogGameMetaManifest>(result);
                                    if (manifest != null)
                                    {
                                        if (manifest.algorithm != "xdelta3")
                                        {
                                            logger.Warn($"Unsupported patching algorithm: {manifest.algorithm}. Please report that.");
                                            manifest = new GogGameMetaManifest
                                            {
                                                errorDisplayed = true
                                            };
                                            return manifest;
                                        }
                                        var gogAccountClient = new GogAccountClient();
                                        manifest.size = new Dictionary<string, GogGameMetaManifest.SizeType>();
                                        manifest.languages = new List<string>();
                                        var ownedItems = await gogAccountClient.GetOwnedIds();

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

                                        var depots = manifest.depots;
                                        if (!manifest.size.ContainsKey("*"))
                                        {
                                            manifest.size.Add("*", new GogGameMetaManifest.SizeType());
                                        }
                                        foreach (var depot in depots)
                                        {
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
                                        }
                                    }
                                    else
                                    {
                                        manifest.errorDisplayed = true;
                                    }

                                    correctJson = true;
                                    File.WriteAllText(cacheInfoFile, Serialization.ToJson(manifest));
                                }
                            }
                        }
                    }
                }
            }
            if (!correctJson)
            {
                logger.Info($"No patches found for {gameId} from {oldBuildId} to {newBuildId}.");
                manifest.errorDisplayed = true;
            }
            return manifest;
        }

        public async Task<ComponentManifest> GetComponentManifest(ComponentChoice component, string platform = "windows")
        {
            ComponentManifest componentManifest = new();
            var componentValue = component switch
            {
                ComponentChoice.Web => "desktop-galaxy-client",
                ComponentChoice.Overlay => "desktop-galaxy-overlay",
                _ => throw new ArgumentOutOfRangeException()
            };

            var result = "";
            try
            {
                var url = $"https://cfg.gog.com/{componentValue}/7/master/files-{platform}.json";
                using var response = await Client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                result = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                logger.Error(ex.Message);
                return componentManifest;
            }
            if (!string.IsNullOrWhiteSpace(result))
            {
                componentManifest = Serialization.FromJson<ComponentManifest>(result);
                if (component == ComponentChoice.Web)
                {
                    foreach (var file in componentManifest.files.ToList())
                    {
                        if (!file.path.StartsWith("web"))
                        {
                            componentManifest.files.Remove(file);
                        }
                    }
                }
            }
            return componentManifest;
        }

    }
}
