using CommonPlugin;
using CommonPlugin.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Linguini.Shared.Types.Bundle;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GogOssLibraryNS
{
    public class GogOssCloud
    {
        private static ILogger logger = LogManager.GetLogger();
        public GogDownloadApi gogDownloadApi = new GogDownloadApi();

        public GogRemoteConfig GetCloudConfig(Game game, bool skipRefreshingMetadata = true)
        {
            string content = null;
            var cacheCloudPath = GogOssLibrary.Instance.GetCachePath("cloud");
            var cacheCloudFile = Path.Combine(cacheCloudPath, $"cloudConfig-{game.GameId}.json");
            if (File.Exists(cacheCloudFile))
            {
                if (!skipRefreshingMetadata)
                {
                    if (File.GetLastWriteTime(cacheCloudFile) < DateTime.Now.AddDays(-7))
                    {
                        File.Delete(cacheCloudFile);
                    }
                }
            }
            if (!File.Exists(cacheCloudFile))
            {
                GlobalProgressOptions metadataProgressOptions = new GlobalProgressOptions(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteProgressMetadata), false);
                var playniteAPI = API.Instance;
                playniteAPI.Dialogs.ActivateGlobalProgress(async (a) =>
                {
                    using var httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.Clear();
                    var gameInfo = GogOss.GetGogGameInfo(game.GameId, game.InstallDirectory);
                    var response = await httpClient.GetAsync($"https://remote-config.gog.com/components/galaxy_client/clients/{gameInfo.clientId}?component_version=2.0.45");
                    if (response.IsSuccessStatusCode)
                    {
                        content = await response.Content.ReadAsStringAsync();
                        if (!Directory.Exists(cacheCloudPath))
                        {
                            Directory.CreateDirectory(cacheCloudPath);
                        }
                        File.WriteAllText(cacheCloudFile, content);
                    }
                }, metadataProgressOptions);
            }
            else
            {
                content = FileSystem.ReadFileAsStringSafe(cacheCloudFile);
            }
            var remoteConfig = new GogRemoteConfig();
            if (content.IsNullOrWhiteSpace())
            {
                logger.Error("An error occurred while downloading GOG's remote config.");
            }
            else if (Serialization.TryFromJson(content, out GogRemoteConfig remoteInfoContent))
            {
                remoteConfig = remoteInfoContent;
            }
            return remoteConfig;
        }

        internal List<GogRemoteConfig.CloudLocation> CalculateGameSavesPath(Game game, bool skipRefreshingMetadata = true)
        {
            var logger = LogManager.GetLogger();
            var cloudConfig = GetCloudConfig(game, skipRefreshingMetadata);
            var calculatedPaths = new List<GogRemoteConfig.CloudLocation>();
            var cloudLocations = cloudConfig.content.Windows.cloudStorage.locations;
            if (cloudLocations.Count > 0)
            {
                var pathVariables = new Dictionary<string, string>
                {
                    { "INSTALL", game.InstallDirectory },
                    { "APPLICATION_DATA_LOCAL", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) },
                    { "APPLICATION_DATA_LOCAL_LOW", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow") },
                    { "APPLICATION_DATA_ROAMING",  Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) },
                    { "DOCUMENTS", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
                    { "SAVED_GAMES", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games") }
                };
                foreach (var cloudLocation in cloudLocations)
                {
                    var expression = new Regex(@"<\?(\w+)\?>");
                    var variableMatches = expression.Matches(cloudLocation.location);
                    foreach (Match variableMatch in variableMatches)
                    {
                        var variable = variableMatch.Groups[1].Value;
                        var matchedText = variableMatch.Value;
                        if (pathVariables.ContainsKey(variable))
                        {
                            cloudLocation.location = cloudLocation.location.Replace(matchedText, pathVariables[variable]);
                        }
                        else
                        {
                            logger.Warn($"Unknown variable {variable} in cloud save path");
                        }
                    }
                    cloudLocation.location = Path.GetFullPath(cloudLocation.location);
                    calculatedPaths.AddMissing(cloudLocation);
                }
            }
            else
            {
                var gameInfo = GogOss.GetGogGameInfo(game.GameId, game.InstallDirectory);
                var cloudLocation = new GogRemoteConfig.CloudLocation
                {
                    name = "__default",
                    location = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GOG.com", "Galaxy", "Applications", gameInfo.clientId, "Storage", "Shared", "Files")
                };
                calculatedPaths.Add(cloudLocation);
            }
            return calculatedPaths;
        }

        internal async Task UploadGameSaves(CloudFile localFile, List<CloudFile> cloudFiles, HttpClient httpClient, string urlPart, bool force, int attempts = 3)
        {
            var data = File.ReadAllBytes(localFile.real_file_path);
            StreamContent compressedData;
            MemoryStream outputStream = new MemoryStream();
            using (GZipStream gzip = new GZipStream(outputStream, CompressionLevel.Optimal, true))
            {
                gzip.Write(data, 0, data.Length);
                gzip.Close();
            }
            outputStream.Position = 0;
            compressedData = new StreamContent(outputStream);
            var compressedStream = await compressedData.ReadAsByteArrayAsync();
            var hash = Helpers.GetMD5(compressedStream).ToLower();

            var fileExistsInCloud = cloudFiles.FirstOrDefault(f => f.name == localFile.name);
            if (fileExistsInCloud != null)
            {
                if (fileExistsInCloud.hash == hash)
                {
                    logger.Warn($"Skipping upload, cuz identical '{localFile.name}' file is already in the cloud.");
                    return;
                }
                if (force != true && fileExistsInCloud.timestamp > localFile.timestamp)
                {
                    logger.Warn($"Skipping upload, cuz '{localFile.name}' file in the cloud is newer.");
                    return;
                }
            }
            logger.Debug($"Uploading {localFile.real_file_path} ({localFile.name}) file... .");
            httpClient.DefaultRequestHeaders.Remove("Accept");
            httpClient.DefaultRequestHeaders.Remove("Etag");
            httpClient.DefaultRequestHeaders.Remove("X-Object-Meta-LocalLastModified");
            httpClient.DefaultRequestHeaders.Add("Etag", hash);
            httpClient.DefaultRequestHeaders.Add("X-Object-Meta-LocalLastModified", localFile.last_modified);
            compressedData.Headers.Add("Content-Encoding", "gzip");
            try
            {
                var uploadResponse = await httpClient.PutAsync($"https://cloudstorage.gog.com/v1/{urlPart}", compressedData);
                uploadResponse.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException exception)
            {
                if (attempts > 1)
                {
                    attempts -= 1;
                    logger.Debug($"Retrying upload of '{localFile.real_file_path}' file. Attempts left: {attempts}");
                    await Task.Delay(2000);
                    await UploadGameSaves(localFile, cloudFiles, httpClient, urlPart, force, attempts);
                }
                else
                {
                    logger.Error($"An error occured while uploading '{localFile.real_file_path}' file: {exception}.");
                }
            }
            catch (Exception exception)
            {
                logger.Error($"An error occured while uploading '{localFile.real_file_path}' file: {exception}.");
            }
            finally
            {
                compressedData.Dispose();
            }
        }

        internal async Task<bool> DownloadGameSaves(CloudFile cloudFile, List<CloudFile> localFiles, HttpClient httpClient, string urlPart, bool force, int attempts = 3)
        {
            bool errorDisplayed = false;
            var fileExistsLocally = localFiles.FirstOrDefault(f => f.name == cloudFile.name);
            if (fileExistsLocally != null && force != true)
            {
                var cloudTimeStamp = cloudFile.timestamp;
                if (fileExistsLocally.timestamp > cloudTimeStamp)
                {
                    logger.Warn($"Skipping download, cuz '{fileExistsLocally.real_file_path}' local file is newer.");
                    return errorDisplayed;
                }
                if (fileExistsLocally.timestamp == cloudTimeStamp)
                {
                    logger.Warn($"Skipping download, cuz '{fileExistsLocally.real_file_path}' file with same date is already available.");
                    return errorDisplayed;
                }
            }
            httpClient.DefaultRequestHeaders.Remove("Accept");
            try
            {
                var downloadResponse = await httpClient.GetAsync($"https://cloudstorage.gog.com/v1/{urlPart}");
                downloadResponse.EnsureSuccessStatusCode();
                var localLastModifiedHeader = downloadResponse.Headers.GetValues("X-Object-Meta-LocalLastModified").FirstOrDefault();
                if (localLastModifiedHeader.IsNullOrEmpty())
                {
                    localLastModifiedHeader = cloudFile.last_modified;
                }

                DateTime cloudLocalLastModified = DateTime.Parse(localLastModifiedHeader);
                if (fileExistsLocally != null)
                {
                    var cloudLocalLastModifiedTs = ((DateTimeOffset)cloudLocalLastModified).ToUnixTimeSeconds();
                    if (force != true && fileExistsLocally.timestamp == cloudLocalLastModifiedTs)
                    {
                        logger.Warn($"Skipping download, cuz '{fileExistsLocally.real_file_path}' file with same date is already available.");
                        return errorDisplayed;
                    }
                }

                logger.Debug($"Downloading {cloudFile.name} ({cloudFile.real_file_path}) file... .");
                var downloadStream = await downloadResponse.Content.ReadAsStreamAsync();
                var neededDirectory = Path.GetDirectoryName(cloudFile.real_file_path);
                if (!Directory.Exists(neededDirectory))
                {
                    Directory.CreateDirectory(neededDirectory);
                }
                using (GZipStream gzip = new GZipStream(downloadStream, CompressionMode.Decompress, true))
                {
                    using (var fileStream = File.Create(cloudFile.real_file_path))
                    {
                        gzip.CopyTo(fileStream);
                    }
                    gzip.Close();
                }
                File.SetLastWriteTime(cloudFile.real_file_path, cloudLocalLastModified);
            }
            catch (HttpRequestException exception)
            {
                if (attempts > 1)
                {
                    attempts -= 1;
                    logger.Debug($"Retrying download of '{cloudFile.real_file_path}' file. Attempts left: {attempts}");
                    await Task.Delay(2000);
                    await DownloadGameSaves(cloudFile, localFiles, httpClient, urlPart, force, attempts);
                }
                else
                {
                    logger.Error($"An error occured while downloading '{cloudFile.real_file_path}' file: {exception}.");
                    errorDisplayed = true;
                }
            }
            catch (Exception exception)
            {
                logger.Error($"An error occured while downloading '{cloudFile.real_file_path}' file: {exception}.");
                errorDisplayed = true;
            }
            return errorDisplayed;
        }

        internal void SyncGameSaves(Game game, CloudSyncAction cloudSyncAction, bool force = false, bool manualSync = false, bool skipRefreshingMetadata = true, string cloudSaveFolder = "")
        {
            var logger = LogManager.GetLogger();
            var cloudSyncEnabled = GogOssLibrary.GetSettings().SyncGameSaves;
            var gameSettings = GogOssGameSettingsView.LoadGameSettings(game.GameId);
            if (gameSettings?.AutoSyncSaves != null)
            {
                cloudSyncEnabled = (bool)gameSettings.AutoSyncSaves;
            }
            if (manualSync)
            {
                cloudSyncEnabled = true;
            }
            var cloudSaveFolders = new List<GogRemoteConfig.CloudLocation>();
            if (cloudSyncEnabled)
            {
                var calculatedCloudSaveFolders = CalculateGameSavesPath(game, skipRefreshingMetadata);
                if (cloudSaveFolder.IsNullOrEmpty())
                {
                    if (!gameSettings.CloudSaveFolder.IsNullOrEmpty())
                    {
                        var newCloudSaveFolder = new GogRemoteConfig.CloudLocation
                        {
                            name = calculatedCloudSaveFolders[0].name,
                            location = gameSettings.CloudSaveFolder
                        };
                        cloudSaveFolders.Add(newCloudSaveFolder);
                    }
                    else
                    {
                        cloudSaveFolders = calculatedCloudSaveFolders;
                    }
                }
                else
                {
                    var newCloudSaveFolder = new GogRemoteConfig.CloudLocation
                    {
                        name = calculatedCloudSaveFolders[0].name,
                        location = cloudSaveFolder
                    };
                    cloudSaveFolders.Add(newCloudSaveFolder);
                }
                
                var playniteAPI = API.Instance;
                GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(LocalizationManager.Instance.GetString(LOC.CommonSyncing, new Dictionary<string, IFluentType> { ["gameTitle"] = (FluentString)game.Name }), false);
                playniteAPI.Dialogs.ActivateGlobalProgress(async (a) =>
                {
                    a.IsIndeterminate = true;
                    var gogAccountClient = new GogAccountClient();
                    var account = await gogAccountClient.GetAccountInfo();
                    if (account.isLoggedIn)
                    {
                        var tokens = gogAccountClient.LoadTokens();
                        if (tokens != null)
                        {
                            using var httpClient = new HttpClient();
                            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                            var metaManifest = await gogDownloadApi.GetGameMetaManifest(game.GameId);
                            var urlParams = new Dictionary<string, string>
                            {
                                { "client_id", metaManifest.clientId },
                                { "client_secret", metaManifest.clientSecret },
                                { "grant_type", "refresh_token" },
                                { "refresh_token", tokens.refresh_token }
                            };
                            var tokenUrl = GogAccountClient.FormatUrl(urlParams, "https://auth.gog.com/token?");
                            var credentialsResponse = await httpClient.GetAsync(tokenUrl);
                            if (credentialsResponse.IsSuccessStatusCode)
                            {
                                var credentialsResponseContent = await credentialsResponse.Content.ReadAsStringAsync();
                                var credentialsResponseJson = Serialization.FromJson<TokenResponse.TokenResponsePart>(credentialsResponseContent);
                                httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + credentialsResponseJson.access_token);
                            }
                            else
                            {
                                logger.Error($"Can't get token for cloud sync: {await credentialsResponse.RequestMessage.Content.ReadAsStringAsync()}.");
                            }
                            var cloudFiles = new List<CloudFile>();
                            var gameInfo = GogOss.GetGogGameInfo(game.GameId, game.InstallDirectory);
                            var gogUserAgent = "GOGGalaxyCommunicationService/2.0.13.27 (Windows_32bit) dont_sync_marker/true installation_source/gog";
                            httpClient.DefaultRequestHeaders.Add("User-Agent", gogUserAgent);
                            httpClient.DefaultRequestHeaders.Add("X-Object-Meta-User-Agent", gogUserAgent);
                            var response = await httpClient.GetAsync($"https://cloudstorage.gog.com/v1/{tokens.user_id}/{gameInfo.clientId}");
                            if (response.IsSuccessStatusCode)
                            {
                                var responseHeaders = response.Headers;
                                var content = await response.Content.ReadAsStringAsync();
                                if (!content.IsNullOrEmpty())
                                {
                                    cloudFiles = Serialization.FromJson<List<CloudFile>>(content);
                                }
                            }
                            else
                            {
                                logger.Error($"{response.ReasonPhrase}: {response.StatusCode}");
                            }
                            if (cloudFiles.Count > 0)
                            {
                                foreach (var cloudFile in cloudFiles.ToList())
                                {
                                    DateTimeOffset cloudLastModified = DateTime.Parse(cloudFile.last_modified);
                                    cloudFile.timestamp = cloudLastModified.ToUnixTimeSeconds();
                                    if (cloudFile.hash == "aadd86936a80ee8a369579c3926f1b3c")
                                    {
                                        cloudFiles.Remove(cloudFile);
                                        continue;
                                    }
                                    var wantedItem = cloudSaveFolders.FirstOrDefault(s => cloudFile.name.Contains(s.name));
                                    if (wantedItem != null)
                                    {
                                        cloudFile.real_file_path = CommonHelpers.NormalizePath(cloudFile.name.ReplaceFirst($"{wantedItem.name}", wantedItem.location));
                                    }
                                }
                            }
                            var localFiles = new List<CloudFile>();
                            foreach (var cloudSaveFolder in cloudSaveFolders)
                            {
                                if (!Directory.Exists(cloudSaveFolder.location))
                                {
                                    Directory.CreateDirectory(cloudSaveFolder.location);
                                }
                                foreach (var fileName in Directory.GetFiles(cloudSaveFolder.location, "*", SearchOption.AllDirectories))
                                {
                                    if (File.Exists(fileName))
                                    {
                                        DateTimeOffset lastWriteTime = File.GetLastWriteTimeUtc(fileName);
                                        var lastWriteTimeTs = lastWriteTime.ToUnixTimeSeconds();
                                        var newCloudFile = new CloudFile
                                        {
                                            real_file_path = fileName,
                                            timestamp = lastWriteTimeTs,
                                            last_modified = lastWriteTime.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                                            name = $"{cloudSaveFolder.name}/{RelativePath.Get(cloudSaveFolder.location, fileName).Replace(Path.DirectorySeparatorChar.ToString(), @"/")}"
                                        };
                                        localFiles.Add(newCloudFile);
                                    }
                                }
                            }
                            switch (cloudSyncAction)
                            {
                                case CloudSyncAction.Upload:
                                    if (localFiles.Count == 0)
                                    {
                                        logger.Info($"No local files with {game.Name} saves.");
                                    }
                                    else
                                    {
                                        foreach (var localFile in localFiles.ToList())
                                        {
                                            await UploadGameSaves(localFile, cloudFiles, httpClient, $"{tokens.user_id}/{gameInfo.clientId}/{localFile.name}", force, 3);
                                        }
                                    }
                                    break;
                                case CloudSyncAction.Download:
                                    if (cloudFiles.Count == 0)
                                    {
                                        logger.Info($"No cloud files with {game.Name} saves.");
                                    }
                                    else
                                    {
                                        bool errorDisplayed = false;
                                        var gameSettings = GogOssGameSettingsView.LoadGameSettings(game.GameId);
                                        foreach (var cloudFile in cloudFiles)
                                        {
                                            var result = await DownloadGameSaves(cloudFile, localFiles, httpClient, $"{tokens.user_id}/{gameInfo.clientId}/{cloudFile.name}", force);
                                            if (result)
                                            {
                                                errorDisplayed = true;
                                            }
                                        }
                                        if (!errorDisplayed)
                                        {
                                            gameSettings.LastCloudSavesDownloadAttempt = ((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds();
                                            var commonHelpers = GogOssLibrary.Instance.commonHelpers;
                                            commonHelpers.SaveJsonSettingsToFile(gameSettings, "GamesSettings", game.GameId, true);
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                }, globalProgressOptions);
            }
        }
    }
}
