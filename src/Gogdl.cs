using CliWrap;
using CliWrap.Buffered;
using CometLibrary.Models;
using CometLibraryNS.Enums;
using CometLibraryNS.Models;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace CometLibraryNS
{
    public class Gogdl
    {
        public static bool IsInstalled
        {
            get
            {
                if (string.IsNullOrEmpty(ClientInstallationPath) || !File.Exists(ClientInstallationPath))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        public static string ClientInstallationPath
        {
            get
            {
                var gogdlPath = "";
                var heroicGogdlBinary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                           @"Programs\heroic\resources\app.asar.unpacked\build\bin\x64\win32\gogdl.exe");
                if (File.Exists(heroicGogdlBinary))
                {
                    gogdlPath = heroicGogdlBinary;
                }
                else
                {
                    var pf64 = Environment.GetEnvironmentVariable("ProgramW6432");
                    if (string.IsNullOrEmpty(pf64))
                    {
                        pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    }
                    gogdlPath = Path.Combine(pf64, "GOG OSS", "gogdl_windows_x86_64.exe");
                    if (!File.Exists(gogdlPath))
                    {
                        var playniteAPI = API.Instance;
                        if (playniteAPI.ApplicationInfo.IsPortable)
                        {
                            gogdlPath = Path.Combine(playniteAPI.Paths.ApplicationPath, "GOG OSS", "gogdl_windows_x86_64.exe");
                        }
                    }
                }
                var savedSettings = CometLibrary.GetSettings();
                if (savedSettings != null)
                {
                    var savedGogdlPath = savedSettings.SelectedGogdlPath;
                    var playniteDirectoryVariable = ExpandableVariables.PlayniteDirectory.ToString();
                    if (savedGogdlPath != "")
                    {
                        if (savedGogdlPath.Contains(playniteDirectoryVariable))
                        {
                            var playniteAPI = API.Instance;
                            savedGogdlPath = savedGogdlPath.Replace(playniteDirectoryVariable, playniteAPI.Paths.ApplicationPath);
                        }
                        if (Directory.Exists(savedGogdlPath))
                        {
                            gogdlPath = savedGogdlPath;
                        }
                    }
                }
                if (!File.Exists(gogdlPath))
                {
                    gogdlPath = "";
                }
                return gogdlPath;
            }
        }

        public static string ConfigPath
        {
            get
            {
                var gogdlConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heroic_gogdl");
                var heroicGogdlConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heroic", "gogdlConfig");
                if (Directory.Exists(heroicGogdlConfigPath))
                {
                    gogdlConfigPath = heroicGogdlConfigPath;
                }
                var envGogdlConfigPath = Environment.GetEnvironmentVariable("GOGDL_CONFIG_PATH");
                if (!envGogdlConfigPath.IsNullOrWhiteSpace() && Directory.Exists(envGogdlConfigPath))
                {
                    gogdlConfigPath = envGogdlConfigPath;
                }
                return gogdlConfigPath;
            }
        }

        public static async Task<LauncherVersion> GetVersionInfoContent()
        {
            var newVersionInfoContent = new LauncherVersion();
            var logger = LogManager.GetLogger();
            if (!IsInstalled)
            {
                throw new Exception(ResourceProvider.GetString(LOC.CometCometNotInstalled));
            }
            var cacheVersionPath = CometLibrary.Instance.GetCachePath("infocache");
            if (!Directory.Exists(cacheVersionPath))
            {
                Directory.CreateDirectory(cacheVersionPath);
            }
            var cacheVersionFile = Path.Combine(cacheVersionPath, "cometVersion.json");
            string content = null;
            if (File.Exists(cacheVersionFile))
            {
                if (File.GetLastWriteTime(cacheVersionFile) < DateTime.Now.AddDays(-7))
                {
                    File.Delete(cacheVersionFile);
                }
            }
            if (!File.Exists(cacheVersionFile))
            {
                var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", Comet.GetUserAgent());
                var response = await httpClient.GetAsync("https://api.github.com/repos/Heroic-Games-Launcher/heroic-gogdl/releases/latest");
                if (response.IsSuccessStatusCode)
                {
                    content = await response.Content.ReadAsStringAsync();
                    if (!Directory.Exists(cacheVersionPath))
                    {
                        Directory.CreateDirectory(cacheVersionPath);
                    }
                    File.WriteAllText(cacheVersionFile, content);
                }
                httpClient.Dispose();
            }
            else
            {
                content = FileSystem.ReadFileAsStringSafe(cacheVersionFile);
            }
            if (content.IsNullOrWhiteSpace())
            {
                logger.Error("An error occurred while downloading Comet's version info.");
            }
            else if (Serialization.TryFromJson(content, out LauncherVersion versionInfoContent))
            {
                newVersionInfoContent = versionInfoContent;
            }
            return newVersionInfoContent;
        }

        public static GogGameMetaManifest GetGameMetaManifest(string gameId)
        {
            var manifest = new GogGameMetaManifest();
            var newManifest = new GogGameMetaManifest();
            var playniteAPI = API.Instance;
            var logger = LogManager.GetLogger();
            var cacheInfoFile = Path.Combine(ConfigPath, "manifests", gameId);
            bool correctJson = false;
            if (File.Exists(cacheInfoFile))
            {
                var content = FileSystem.ReadFileAsStringSafe(cacheInfoFile);
                if (!content.IsNullOrWhiteSpace() && Serialization.TryFromJson(content, out newManifest))
                {
                    if (manifest != null && manifest.buildId != null)
                    {
                        correctJson = true;
                    }
                }
            }
            if (correctJson)
            {
                manifest = newManifest;
            }
            return manifest;
        }

        public static async Task<GogDownloadGameInfo> GetGameInfo(DownloadManagerData.Download downloadData, bool skipRefreshing = false, bool silently = false, bool forceRefreshCache = false)
        {
            var manifest = new GogDownloadGameInfo();
            var playniteAPI = API.Instance;
            var logger = LogManager.GetLogger();
            var cacheInfoPath = CometLibrary.Instance.GetCachePath("infocache");
            var cacheInfoFileName = $"{downloadData.gameID}.json";
            if (downloadData.downloadProperties.buildId != "")
            {
                cacheInfoFileName = $"{downloadData.gameID}_build{downloadData.downloadProperties.buildId}.json";
            }
            var cacheInfoFile = Path.Combine(cacheInfoPath, cacheInfoFileName);
            if (!Directory.Exists(cacheInfoPath))
            {
                Directory.CreateDirectory(cacheInfoPath);
            }
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
                    if (manifest != null && manifest.builds != null)
                    {
                        correctJson = true;
                    }
                }
            }
            if (!correctJson)
            {
                if (downloadData.downloadItemType == DownloadItemType.Dependency)
                {
                    var redistManifest = await GetRedistInfo(downloadData, skipRefreshing, silently, forceRefreshCache);
                    manifest.buildId = redistManifest.build_id;
                    manifest.size = new Dictionary<string, GogDownloadGameInfo.SizeType>();
                    var redistSizes = new GogDownloadGameInfo.SizeType
                    {
                        disk_size = redistManifest.size,
                        download_size = redistManifest.compressedSize
                    };
                    manifest.size.Add("*", redistSizes);
                    manifest.executable = redistManifest.executable;
                    File.WriteAllText(cacheInfoFile, Serialization.ToJson(manifest));
                    return manifest;
                }

                BufferedCommandResult result;
                var infoArgs = new List<string>();
                infoArgs.AddRange(new[] { "--auth-config-path", Comet.TokensPath });
                infoArgs.AddRange(new[] { "info", downloadData.gameID, "--platform", downloadData.downloadProperties.os, "--json" });

                if (downloadData.downloadProperties.buildId != "")
                {
                    infoArgs.AddRange(new[] { "--build", downloadData.downloadProperties.buildId });
                }
                result = await Cli.Wrap(ClientInstallationPath)
                                      .WithArguments(infoArgs)
                                      .AddCommandToLog()
                                      .WithValidation(CommandResultValidation.None)
                                      .ExecuteBufferedAsync();
                var errorMessage = result.StandardError;
                if (result.ExitCode != 0 || errorMessage.Contains("ERROR") || errorMessage.Contains("CRITICAL") || errorMessage.Contains("Error"))
                {
                    logger.Error(result.StandardError);
                    if (!silently)
                    {
                        if (result.StandardError.Contains("Failed to establish a new connection")
                            || result.StandardError.Contains("Log in failed")
                            || result.StandardError.Contains("Login failed")
                            || result.StandardError.Contains("No saved credentials"))
                        {
                            playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.Comet3P_PlayniteMetadataDownloadError).Format(ResourceProvider.GetString(LOC.Comet3P_PlayniteLoginRequired)));
                        }
                        else if (result.StandardError.Contains("Game doesn't support content system api"))
                        {
                            playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.CometGameNotInstallable).Format(downloadData.name, "https://gog.com/account "));
                        }
                        else
                        {
                            playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.Comet3P_PlayniteMetadataDownloadError).Format(ResourceProvider.GetString(LOC.CometCheckLog)));
                        }
                    }
                }
                else
                {
                    File.WriteAllText(cacheInfoFile, result.StandardOutput);
                    manifest = Serialization.FromJson<GogDownloadGameInfo>(result.StandardOutput);
                }
            }
            return manifest;
        }

        public static async Task<GogDownloadRedistManifest.Depot> GetRedistInfo(DownloadManagerData.Download downloadData, bool skipRefreshing = false, bool silently = false, bool forceRefreshCache = false)
        {
            var redistManifest = new GogDownloadRedistManifest.Depot();
            var manifest = new GogDownloadRedistManifest();
            var playniteAPI = API.Instance;
            var logger = LogManager.GetLogger();
            bool correctJson = false;
            if (!correctJson)
            {
                BufferedCommandResult result;
                var infoArgs = new List<string>();
                infoArgs.AddRange(new[] { "--auth-config-path", Comet.TokensPath });
                infoArgs.AddRange(new[] { "redist", "--ids", downloadData.gameID, "--path", "/", "--print-manifest" });
               
                result = await Cli.Wrap(ClientInstallationPath)
                                      .WithArguments(infoArgs)
                                      .AddCommandToLog()
                                      .WithValidation(CommandResultValidation.None)
                                      .ExecuteBufferedAsync();
                var errorMessage = result.StandardError;
                if (result.ExitCode != 0 || errorMessage.Contains("ERROR") || errorMessage.Contains("CRITICAL") || errorMessage.Contains("Error"))
                {
                    logger.Error(result.StandardError);
                    if (!silently)
                    {
                        if (result.StandardError.Contains("Failed to establish a new connection")
                            || result.StandardError.Contains("Log in failed")
                            || result.StandardError.Contains("Login failed")
                            || result.StandardError.Contains("No saved credentials"))
                        {
                            playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.Comet3P_PlayniteMetadataDownloadError).Format(ResourceProvider.GetString(LOC.Comet3P_PlayniteLoginRequired)));
                        }
                        else
                        {
                            playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.Comet3P_PlayniteMetadataDownloadError).Format(ResourceProvider.GetString(LOC.CometCheckLog)));
                        }
                    }
                }
                else
                {
                    manifest = Serialization.FromJson<GogDownloadRedistManifest>(result.StandardOutput);
                    var depots = manifest.depots;
                    redistManifest = depots.First(d => d.dependencyId == downloadData.gameID);
                    redistManifest.build_id = manifest.build_id;
                }
            }
            return redistManifest;
        }
    }
}
