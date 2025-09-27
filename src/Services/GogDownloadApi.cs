using CommonPlugin;
using GogOssLibraryNS.Models;
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
        public static readonly HttpClient HttpClient = new HttpClient();
        public static string UserAgent => @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        public async Task<GogBuildsData> GetProductBuilds(string gameId, string platform = "windows")
        {
            var cachePath = GogOssLibrary.Instance.GetCachePath("downloadbuildscache");

            var newBuildsInfoContent = new GogBuildsData();
            string content = null;

            var cacheInfoFile = Path.Combine(cachePath, $"{gameId}_builds.json");
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

                HttpClient.DefaultRequestHeaders.Clear();
                HttpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var response =
                    await HttpClient.GetAsync(
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

        public async Task<GogGameMetaManifest> GetGameMetaManifest(
            string gameId, string buildId = "", string branch = "", string platform = "windows")
        {
            var manifest = new GogGameMetaManifest();
            var cacheInfoFileName = $"{gameId}.json";
            if (buildId != "")
            {
                cacheInfoFileName = $"{gameId}_build{buildId}.json";
            }

            var cachePath = GogOssLibrary.Instance.GetCachePath("manifests");
            var cacheInfoFile = Path.Combine(cachePath, cacheInfoFileName);
            bool correctJson = false;
            if (File.Exists(cacheInfoFile))
            {
                if (File.GetLastWriteTime(cacheInfoFile) < DateTime.Now.AddDays(-7))
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

                var builds = await GetProductBuilds(gameId, platform);
                if (builds.items.Count > 0)
                {
                    HttpClient.DefaultRequestHeaders.Clear();
                    HttpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                    string chosenBranch = branch;
                    var chosenBuildId = buildId;
                    if (chosenBranch == "disabled")
                    {
                        chosenBranch = "";
                    }

                    var selectedBuild =
                        builds.items.FirstOrDefault(i => i.branch == chosenBranch && i.build_id == chosenBuildId);
                    if (selectedBuild != null)
                    {
                        var response = await HttpClient.GetAsync(selectedBuild.link);
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

                        var result = Helpers.DecompressZlib(content);
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

                                foreach (var depot in manifest.depots)
                                {
                                    if (!manifest.size.ContainsKey("*"))
                                    {
                                        manifest.size.Add("*", new GogGameMetaManifest.SizeType());
                                    }
                                    foreach (var language in depot.languages)
                                    {
                                        var newLanguage = language;
                                        if (newLanguage == "Neutral")
                                        {
                                            newLanguage = "*";
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

                                        if (depot.productId == gameId)
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
                                                    manifest.dlcs[depot.productId].size.Add(newLanguage,
                                                        new GogGameMetaManifest.SizeType());
                                                }

                                                manifest.dlcs[depot.productId].size[newLanguage].download_size +=
                                                    depot.compressedSize;
                                                manifest.dlcs[depot.productId].size[newLanguage].disk_size += depot.size;
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
            var metaManifest = await GetGameMetaManifest(taskData.gameID, taskData.downloadProperties.buildId, taskData.downloadProperties.betaChannel);
            foreach (var depot in metaManifest.depots)
            {
                var chosenlanguage = taskData.downloadProperties.language;
                if (string.IsNullOrEmpty(chosenlanguage))
                {
                    chosenlanguage = metaManifest.languages.First();
                }

                if (depot.languages.Contains(chosenlanguage))
                {
                    var productIds = new List<string>
                    {
                        taskData.gameID
                    };
                    if (taskData.downloadProperties.extraContent != null && taskData.downloadProperties.extraContent.Count > 0)
                    {
                        productIds.AddRange(taskData.downloadProperties.extraContent);
                    }

                    if (productIds.Contains(depot.productId))
                    {
                        var manifestHash = depot.manifest;
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

        public async Task<GogDepot> GetDepotInfo(string manifest, int version = 2)
        {
            var cachePath = GogOssLibrary.Instance.GetCachePath("depotcache");
            var depotManifest = new GogDepot();
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

                HttpClient.DefaultRequestHeaders.Clear();
                HttpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                var response =
                    await HttpClient.GetAsync(
                        $"https://cdn.gog.com/content-system/v2/meta/{GetGalaxyPath(manifest)}");
                Stream content;
                if (response.IsSuccessStatusCode)
                {
                    content = await response.Content.ReadAsStreamAsync();
                }
                else
                {
                    Console.Error.WriteLine($"An error occurred while downloading depot manifest {manifest}.");
                    return depotManifest;
                }

                var result = Helpers.DecompressZlib(content);
                File.WriteAllText(cacheInfoFile, result);
                depotManifest = Serialization.FromJson<GogDepot>(result);
            }

            return depotManifest;
        }


        public async Task<List<string>> GetSecureLinks(DownloadManagerData.Download taskData, string path = "/")
        {
            List<string> urls = new List<string>();
            var url = "";
            var metaManifest = await GetGameMetaManifest(taskData.gameID, taskData.downloadProperties.buildId, taskData.downloadProperties.betaChannel);
            if (metaManifest.version == 2)
            {
                url = $"https://content-system.gog.com/products/{taskData.gameID}/secure_link?generation=2&_version=2&path={path}";
            }
            else
            {
                url = $"https://content-system.gog.com/products/{taskData.gameID}/secure_link?_version=2&type=depot%path={path}";
            }

            var gogAccountClient = new GogAccountClient();
            if (await gogAccountClient.GetIsUserLoggedIn())
            {
                var tokens = gogAccountClient.LoadTokens();
                HttpClient.DefaultRequestHeaders.Clear();
                HttpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                HttpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + tokens.access_token);
                var response = await HttpClient.GetAsync(url);
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
                                foreach (var key in endpoint.parameters.Keys)
                                {
                                    var keyValue = endpoint.parameters[key].ToString();
                                    if (key == "path")
                                    {
                                        keyValue += "/{GALAXY_PATH}";
                                    }

                                    newUrl = newUrl.Replace('{' + key + '}', keyValue);
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
    }
}
