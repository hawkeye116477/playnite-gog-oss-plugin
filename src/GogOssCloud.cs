using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using static GogOssLibraryNS.Models.GogRemoteConfig;
using static GogOssLibraryNS.Models.TokenResponse;

namespace GogOssLibraryNS
{
    public class GogOssCloud
    {
        public static GogRemoteConfig GetCloudConfig(string gameId, bool skipRefreshingMetadata = true)
        {
            var logger = LogManager.GetLogger();
            string content = null;
            var cacheCloudPath = GogOssLibrary.Instance.GetCachePath("cloudcache");
            var cacheCloudFile = Path.Combine(cacheCloudPath, $"cloudConfig-{gameId}.json");
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
                GlobalProgressOptions metadataProgressOptions = new GlobalProgressOptions(ResourceProvider.GetString(LOC.GogOss3P_PlayniteProgressMetadata), false);
                var playniteAPI = API.Instance;
                playniteAPI.Dialogs.ActivateGlobalProgress(async (a) =>
                {
                    using var httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.Clear();
                    var gameInfo = GogOss.GetGogGameInfo(gameId);
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

        internal static List<CloudLocation> CalculateGameSavesPath(string gameID, bool skipRefreshingMetadata = true)
        {
            var logger = LogManager.GetLogger();
            var installedInfo = GogOss.GetInstalledInfo(gameID);
            var cloudConfig = GetCloudConfig(gameID, skipRefreshingMetadata);
            var calculatedPaths = new List<CloudLocation>();
            var cloudLocations = cloudConfig.content.Windows.cloudStorage.locations;
            if (cloudLocations.Count > 0)
            {
                var pathVariables = new Dictionary<string, string>
                {
                    { "INSTALL", installedInfo.install_path },
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
                var gameInfo = GogOss.GetGogGameInfo(gameID);
                var cloudLocation = new CloudLocation
                {
                    name = "__default",
                    location = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GOG.com", "Galaxy", "Applications", gameInfo.clientId, "Storage", "Shared", "Files")
                };
                calculatedPaths.Add(cloudLocation);
            }
            return calculatedPaths;
        }

        internal static void SyncGameSaves(string gameName, string gameID, CloudSyncAction cloudSyncAction, bool manualSync = false, bool skipRefreshingMetadata = true, string cloudSaveFolder = "")
        {
            var logger = LogManager.GetLogger();
            var cloudSyncEnabled = GogOssLibrary.GetSettings().SyncGameSaves;
            var gameSettings = GogOssGameSettingsView.LoadGameSettings(gameID);
            if (gameSettings?.AutoSyncSaves != null)
            {
                cloudSyncEnabled = (bool)gameSettings.AutoSyncSaves;
            }
            if (manualSync)
            {
                cloudSyncEnabled = true;
            }
            var cloudSaveFolders = new List<CloudLocation>();
            if (cloudSyncEnabled)
            {
                var calculatedCloudSaveFolders = CalculateGameSavesPath(gameID, skipRefreshingMetadata);
                if (cloudSaveFolder.IsNullOrEmpty())
                {
                    if (!gameSettings.CloudSaveFolder.IsNullOrEmpty())
                    {
                        var newCloudSaveFolder = new CloudLocation
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
                    var newCloudSaveFolder = new CloudLocation
                    {
                        name = calculatedCloudSaveFolders[0].name,
                        location = cloudSaveFolder
                    };
                    cloudSaveFolders.Add(newCloudSaveFolder);
                }
            }
            var playniteAPI = API.Instance;
            GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(ResourceProvider.GetString(LOC.GogOssSyncing).Format(gameName), false);
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
                        var metaManifest = Gogdl.GetGameMetaManifest(gameID);
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
                            var credentialsResponseJson = Serialization.FromJson<TokenResponsePart>(credentialsResponseContent);
                            httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + credentialsResponseJson.access_token);
                        }
                        else
                        {
                            logger.Error($"Can't get token for cloud sync: {await credentialsResponse.RequestMessage.Content.ReadAsStringAsync()}.");
                        }
                        var cloudFiles = new List<CloudFile>();
                        var gameInfo = GogOss.GetGogGameInfo(gameID);
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
                                }
                                var wantedItem = cloudSaveFolders.FirstOrDefault(s => cloudFile.name.Contains(s.name));
                                if (wantedItem != null)
                                {
                                    cloudFile.real_file_path = Path.GetFullPath(cloudFile.name.Replace(wantedItem.name, wantedItem.location));
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
                            foreach (var fileName in Directory.GetFiles(cloudSaveFolder.location))
                            {
                                if (File.Exists(fileName))
                                {
                                    DateTimeOffset lastWriteTime = File.GetLastWriteTimeUtc(fileName);
                                    DateTimeOffset lastCreationTime = File.GetCreationTimeUtc(fileName);
                                    var lastWriteTimeTs = lastWriteTime.ToUnixTimeSeconds();
                                    var lastCreationTimeTs = lastCreationTime.ToUnixTimeSeconds();
                                    if (lastCreationTimeTs > lastWriteTimeTs)
                                    {
                                        lastWriteTimeTs = lastCreationTimeTs;
                                    }
                                    var newCloudFile = new CloudFile
                                    {
                                        real_file_path = fileName,
                                        timestamp = lastWriteTimeTs,
                                        last_modified = lastWriteTime.UtcDateTime.ToString("o"),
                                        name = $"{cloudSaveFolder.name}/{Helpers.GetRelativePath(cloudSaveFolder.location, fileName)}"
                                    };
                                    localFiles.Add(newCloudFile);
                                }
                            }
                        }
                        switch (cloudSyncAction)
                        {
                            case CloudSyncAction.Upload:
                            case CloudSyncAction.ForceUpload:
                                if (localFiles.Count == 0)
                                {
                                    logger.Info($"No local files with {gameName} saves.");
                                }
                                else
                                {
                                    foreach (var localFile in localFiles.ToList())
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
                                            if (cloudSyncAction != CloudSyncAction.ForceUpload && fileExistsInCloud.timestamp > localFile.timestamp)
                                            {
                                                logger.Warn($"Skipping upload, cuz '{localFile.name}' file in the cloud is newer.");
                                                continue;
                                            }
                                            if (fileExistsInCloud.timestamp == localFile.timestamp)
                                            {
                                                if (fileExistsInCloud.hash == localFile.hash)
                                                {
                                                    logger.Warn($"Skipping upload, cuz identical '{localFile.name}' file is already in the cloud.");
                                                    continue;
                                                }
                                            }
                                        }
                                        httpClient.DefaultRequestHeaders.Remove("Accept");
                                        httpClient.DefaultRequestHeaders.Remove("Etag");
                                        httpClient.DefaultRequestHeaders.Remove("X-Object-Meta-LocalLastModified");
                                        httpClient.DefaultRequestHeaders.Add("Etag", hash);
                                        httpClient.DefaultRequestHeaders.Add("X-Object-Meta-LocalLastModified", localFile.last_modified);
                                        compressedData.Headers.Add("Content-Encoding", "gzip");
                                        var uploadResponse = await httpClient.PutAsync($"https://cloudstorage.gog.com/v1/{tokens.user_id}/{gameInfo.clientId}/{localFile.name}", compressedData);
                                        if (!uploadResponse.IsSuccessStatusCode)
                                        {
                                            logger.Error($"An error occured while uploading '{localFile.real_file_path}' file: {await uploadResponse.RequestMessage.Content.ReadAsStringAsync()}, {uploadResponse.StatusCode}.");
                                        }
                                        else
                                        {
                                            logger.Info($"'{localFile.real_file_path}' file was uploaded successfully.");
                                        }
                                    }
                                }
                                break;
                            case CloudSyncAction.Download:
                            case CloudSyncAction.ForceDownload:
                                if (cloudFiles.Count == 0)
                                {
                                    logger.Info($"No cloud files with {gameName} saves.");
                                }
                                else
                                {
                                    foreach (var cloudFile in cloudFiles)
                                    {
                                        var fileExistsLocally = localFiles.FirstOrDefault(f => f.name == cloudFile.name);
                                        if (fileExistsLocally != null)
                                        {
                                            if (cloudSyncAction != CloudSyncAction.ForceDownload && fileExistsLocally.timestamp > cloudFile.timestamp)
                                            {
                                                logger.Warn($"Skipping download, cuz '{fileExistsLocally.real_file_path}' local file is newer.");
                                                continue;
                                            }
                                            if (cloudSyncAction != CloudSyncAction.ForceDownload && fileExistsLocally.timestamp == cloudFile.timestamp)
                                            {
                                                logger.Warn($"Skipping download, cuz '{fileExistsLocally.real_file_path}' file with same date is already available.");
                                                continue;
                                            }
                                        }
                                        httpClient.DefaultRequestHeaders.Remove("Accept");
                                        var downloadResponse = await httpClient.GetAsync($"https://cloudstorage.gog.com/v1/{tokens.user_id}/{gameInfo.clientId}/{cloudFile.name}");
                                        if (downloadResponse.IsSuccessStatusCode)
                                        {
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
                                            var localLastModifiedHeader = downloadResponse.Headers.GetValues("X-Object-Meta-LocalLastModified").FirstOrDefault();
                                            if (localLastModifiedHeader != null)
                                            {
                                                File.SetLastWriteTime(cloudFile.real_file_path, DateTime.Parse(localLastModifiedHeader));
                                            }
                                            logger.Info($"'{cloudFile.real_file_path}' file was downloaded successfully.");
                                        }
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
