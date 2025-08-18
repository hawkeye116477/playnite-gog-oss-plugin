using CliWrap;
using CliWrap.Buffered;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Enums;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using CommonPlugin;
using Linguini.Shared.Types.Bundle;

namespace GogOssLibraryNS
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
                var savedSettings = GogOssLibrary.GetSettings();
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
                        gogdlPath = savedGogdlPath;
                    }
                }
                if (!File.Exists(gogdlPath))
                {
                    gogdlPath = "";
                }
                return gogdlPath;
            }
        }

        public static Dictionary<string, string> DefaultEnvironmentVariables
        {
            get
            {
                var envDict = new Dictionary<string, string>();
                var heroicGogdlConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heroic", "gogdlConfig", "heroic_gogdl");
                if (ConfigPath == heroicGogdlConfigPath)
                {
                    envDict.Add("GOGDL_CONFIG_PATH", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heroic", "gogdlConfig"));
                }
                return envDict;
            }
        }

        public static string ConfigPath
        {
            get
            {
                var gogdlConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heroic_gogdl");
                var heroicGogdlConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heroic", "gogdlConfig", "heroic_gogdl");
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

        public static string DependenciesInstallationPath
        {
            get
            {
                var dataDir = GogOssLibrary.Instance.GetPluginUserDataPath();
                var dependPath = Path.Combine(dataDir, ".gogRedist");
                return dependPath;
            }
        }

        public static async Task<string> GetVersion()
        {
            var version = "0";
            if (IsInstalled)
            {
                var versionCmd = await Cli.Wrap(ClientInstallationPath)
                                          .WithArguments(new[] { "--auth-config-path", "null", "-v" })
                                          .AddCommandToLog()
                                          .WithValidation(CommandResultValidation.None)
                                          .ExecuteBufferedAsync();
                if (!versionCmd.StandardOutput.IsNullOrEmpty())
                {
                    version = versionCmd.StandardOutput.Trim();
                }
            }
            return version;
        }

        public static async Task<LauncherVersion> GetVersionInfoContent()
        {
            var newVersionInfoContent = new LauncherVersion();
            var logger = LogManager.GetLogger();
            if (!IsInstalled)
            {
                ShowNotInstalledError();
                return newVersionInfoContent;
            }
            var cacheVersionPath = GogOssLibrary.Instance.GetCachePath("infocache");
            if (!Directory.Exists(cacheVersionPath))
            {
                Directory.CreateDirectory(cacheVersionPath);
            }
            var cacheVersionFile = Path.Combine(cacheVersionPath, "gogdlVersion.json");
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
                httpClient.DefaultRequestHeaders.Add("User-Agent", GogOss.UserAgent);
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
                logger.Error("An error occurred while downloading Gogdl's version info.");
            }
            else if (Serialization.TryFromJson(content, out LauncherVersion versionInfoContent))
            {
                newVersionInfoContent = versionInfoContent;
            }
            return newVersionInfoContent;
        }

        public static GogGameMetaManifest GetGameMetaManifest(string gameId)
        {
            var logger = LogManager.GetLogger();
            var manifest = new GogGameMetaManifest();
            var newManifest = new GogGameMetaManifest();
            var cacheInfoFile = Path.Combine(ConfigPath, "manifests", gameId);
            bool correctJson = false;
            if (File.Exists(cacheInfoFile))
            {
                var content = FileSystem.ReadFileAsStringSafe(cacheInfoFile);
                if (!content.IsNullOrWhiteSpace() && Serialization.TryFromJson(content, out newManifest))
                {
                    if (newManifest != null && (newManifest.baseProductId != null || newManifest.product.rootGameID != null))
                    {
                        correctJson = true;
                    }
                }
            }
            if (correctJson)
            {
                manifest = newManifest;
            }
            else
            {
                logger.Error("Can't read game meta manifest");
            }
            return manifest;
        }

        public static async Task<GogDownloadGameInfo> GetGameInfo(string gameId, Installed installedInfo, bool skipRefreshing = false, bool silently = false, bool forceRefreshCache = false)
        {
            var downloadData = new DownloadManagerData.Download
            {
                gameID = gameId,
                name = installedInfo.title
            };
            downloadData.downloadProperties.buildId = installedInfo.build_id;
            return await GetGameInfo(downloadData);
        }

        public static async Task<GogDownloadGameInfo> GetGameInfo(DownloadManagerData.Download downloadData, bool skipRefreshing = false, bool silently = false, bool forceRefreshCache = false)
        {
            var manifest = new GogDownloadGameInfo();
            var playniteAPI = API.Instance;
            var logger = LogManager.GetLogger();
            var cacheInfoPath = GogOssLibrary.Instance.GetCachePath("infocache");
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
                    var redistManifest = await GetRedistInfo(downloadData.gameID, skipRefreshing, silently, forceRefreshCache);
                    manifest.executable = redistManifest.executable;
                    manifest.buildId = redistManifest.build_id;
                    manifest.size = new Dictionary<string, GogDownloadGameInfo.SizeType>();
                    manifest.readableName = redistManifest.readableName;
                    downloadData.name = manifest.readableName;
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
                infoArgs.AddRange(new[] { "--auth-config-path", GogOss.TokensPath });
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
                            playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.GogOss3P_PlayniteMetadataDownloadError).Format(ResourceProvider.GetString(LOC.GogOss3P_PlayniteLoginRequired)), downloadData.name);
                        }
                        else if (result.StandardError.Contains("Game doesn't support content system api"))
                        {
                            playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.GogOssGameNotInstallable, new Dictionary<string, IFluentType> { ["gameTitle"] = (FluentString)downloadData.name, ["url"] = (FluentString)"https://gog.com/account " }));
                        }
                        else
                        {
                            playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.GogOss3P_PlayniteMetadataDownloadError).Format(LocalizationManager.Instance.GetString(LOC.CommonCheckLog)), downloadData.name);
                        }
                    }
                    manifest.errorDisplayed = true;
                }
                else
                {
                    File.WriteAllText(cacheInfoFile, result.StandardOutput);
                    manifest = Serialization.FromJson<GogDownloadGameInfo>(result.StandardOutput);
                }
            }
            return manifest;
        }

        public static async Task<GogDownloadRedistManifest.Depot> GetRedistInfo(string gameId, bool skipRefreshing = false, bool silently = false, bool forceRefreshCache = false)
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
                infoArgs.AddRange(new[] { "--auth-config-path", GogOss.TokensPath });
                infoArgs.AddRange(new[] { "redist", "--ids", gameId, "--path", "/", "--print-manifest" });

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
                            playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.GogOss3P_PlayniteMetadataDownloadError).Format(ResourceProvider.GetString(LOC.GogOss3P_PlayniteLoginRequired)));
                        }
                        else
                        {
                            playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.GogOss3P_PlayniteMetadataDownloadError).Format(LocalizationManager.Instance.GetString(LOC.CommonCheckLog)));
                        }
                    }
                }
                else
                {
                    manifest = Serialization.FromJson<GogDownloadRedistManifest>(result.StandardOutput);
                    var depots = manifest.depots;
                    redistManifest = depots.FirstOrDefault(d => d.dependencyId == gameId);
                    if (redistManifest != null)
                    {
                        redistManifest.build_id = manifest.build_id;
                    }
                    else
                    {
                        redistManifest = new GogDownloadRedistManifest.Depot();
                    }
                }
            }
            return redistManifest;
        }

        public static List<string> GetDownloadedDepends()
        {
            var depends = new List<string>();
            var redistManifestFile = Path.Combine(DependenciesInstallationPath, ".gogdl-redist-manifest");
            if (File.Exists(redistManifestFile))
            {
                var redistManifest = Serialization.FromJson<GogDownloadRedistManifest>(File.ReadAllText(redistManifestFile));
                if (redistManifest.HGLInstalled.Count > 0)
                {
                    depends = redistManifest.HGLInstalled;
                }
            }
            return depends;
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


        public static List<string> GetRequiredDepends()
        {
            var cacheMetaManifestsDir = Path.Combine(ConfigPath, "manifests");
            var depends = new List<string>();
            var installedAppList = GogOssLibrary.GetInstalledAppList();
            if (Directory.Exists(cacheMetaManifestsDir))
            {
                var apps = installedAppList.Keys.ToList();
                foreach (var app in apps)
                {
                    var cacheMetaManifestFile = Path.Combine(cacheMetaManifestsDir, app);
                    if (File.Exists(cacheMetaManifestFile))
                    {
                        var metaManifest = GetGameMetaManifest(app);
                        if (metaManifest.scriptInterpreter)
                        {
                            depends.AddMissing("ISI");
                        }
                        if (metaManifest.dependencies.Count > 0)
                        {
                            foreach (var depend in metaManifest.dependencies)
                            {
                                depends.AddMissing(depend);
                            }
                        }
                    }
                }
            }
            return depends;
        }

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
            var manifest = await GetGameInfo(installData);
            var size = new GogDownloadGameInfo.SizeType
            {
                download_size = 0,
                disk_size = 0
            };
            if (manifest.size.ContainsKey("*"))
            {
                size.download_size += manifest.size["*"].download_size;
                size.disk_size += manifest.size["*"].disk_size;
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
                    if (selectedDlcs.Contains(dlc.id))
                    {
                        if (dlc.size.ContainsKey("*"))
                        {
                            size.download_size += dlc.size["*"].download_size;
                            size.disk_size += dlc.size["*"].disk_size;
                        }
                        if (dlc.size.ContainsKey(selectedLanguage))
                        {
                            size.download_size += dlc.size[selectedLanguage].download_size;
                            size.disk_size += dlc.size[selectedLanguage].disk_size;
                        }
                    }
                }
            }
            return size;
        }

        public static void ShowNotInstalledError()
        {
            var playniteAPI = API.Instance;
            var options = new List<MessageBoxOption>
            {
                new MessageBoxOption(ResourceProvider.GetString(LOC.GogOss3P_PlayniteInstallGame)),
                new MessageBoxOption(ResourceProvider.GetString(LOC.GogOss3P_PlayniteOKLabel)),
            };
            var result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonLauncherNotInstalled, new Dictionary<string, IFluentType> { ["launcherName"] = (FluentString)"Gogdl" }), "GOG OSS library integration", MessageBoxImage.Information, options);
            if (result == options[0])
            {
                Playnite.Commands.GlobalCommands.NavigateUrl("https://github.com/hawkeye116477/playnite-gog-oss-plugin/wiki/Troubleshooting#gogdl-or-comet-is-not-installed");
            }
        }
    }
}
