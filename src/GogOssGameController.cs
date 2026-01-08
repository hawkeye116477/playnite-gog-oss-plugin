using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CliWrap;
using CliWrap.EventStream;
using CommonPlugin;
using CommonPlugin.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Linguini.Shared.Types.Bundle;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace GogOssLibraryNS
{
    public class GogOssInstallController : InstallController
    {
        private readonly GogOssLibrary gogLibrary;
        public GogOssInstallController(Game game, GogOssLibrary gogLibrary) : base(game)
        {
            this.gogLibrary = gogLibrary;
        }

        public override void Install(InstallActionArgs args)
        {
            var installProperties = new DownloadProperties { downloadAction = DownloadAction.Install };
            var installData = new List<DownloadManagerData.Download>
            {
                new DownloadManagerData.Download { gameID = Game.GameId, name = Game.Name, downloadProperties = installProperties }
            };
            LaunchInstaller(installData);
            InvokeOnInstallationCancelled(new GameInstallationCancelledEventArgs());
        }

        public static void LaunchInstaller(List<DownloadManagerData.Download> installData)
        {
            var playniteAPI = API.Instance;
            Window window = playniteAPI.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = false,
            });
            window.DataContext = installData;
            window.Content = new GogOssGameInstallerView();
            window.Owner = playniteAPI.Dialogs.GetCurrentAppWindow();
            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.MinWidth = 600;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var title = LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteInstallGame);
            if (installData[0].downloadProperties.downloadAction == DownloadAction.Repair)
            {
                title = LocalizationManager.Instance.GetString(LOC.CommonRepair);
            }
            if (installData.Count == 1)
            {
                title = installData[0].name;
            }
            window.Title = title;
            window.ShowDialog();
        }
    }

    public class GogOssUninstallController : UninstallController
    {
        private static ILogger logger = LogManager.GetLogger();
        public GogOssUninstallController(Game game) : base(game)
        {
            Name = "Uninstall";
        }

        public static void LaunchUninstaller(List<Game> games)
        {
            var playniteAPI = API.Instance;
            string gamesCombined = string.Join(", ", games.Select(item => item.Name));
            var result = MessageCheckBoxDialog.ShowMessage(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUninstallGame), LocalizationManager.Instance.GetString(LOC.CommonUninstallGameConfirm, new Dictionary<string, IFluentType> { ["gameTitle"] = (FluentString)gamesCombined }), LocalizationManager.Instance.GetString(LOC.CommonRemoveGameLaunchSettings), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result.Result)
            {
                var notUninstalledGames = new List<Game>();
                var uninstalledGames = new List<Game>();
                GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions($"{LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUninstalling)}... ", false);
                playniteAPI.Dialogs.ActivateGlobalProgress(async (a) =>
                {
                    a.IsIndeterminate = false;
                    a.ProgressMaxValue = games.Count;
                    using (playniteAPI.Database.BufferedUpdate())
                    {
                        var counter = 0;
                        foreach (var game in games)
                        {
                            a.Text = $"{LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteUninstalling)} {game.Name}... ";
                            var uninstaller = Path.Combine(game.InstallDirectory, "unins000.exe");
                            if (File.Exists(uninstaller))
                            {
                                var uninstallArgs = new List<string>
                                {
                                    "/VERYSILENT",
                                    $"/ProductId={game.GameId}",
                                    "/KEEPSAVES"
                                };
                                await Cli.Wrap(uninstaller)
                                         .WithArguments(uninstallArgs)
                                         .AddCommandToLog()
                                         .ExecuteAsync();
                            }
                            try
                            {
                                if (Directory.Exists(game.InstallDirectory))
                                {
                                    Directory.Delete(game.InstallDirectory, true);
                                }
                            }
                            catch (Exception ex)
                            {
                                notUninstalledGames.Add(game);
                                logger.Error(ex, $"An error occured during uninstalling {game.Name}");
                                counter += 1;
                                a.CurrentProgressValue = counter;
                                continue;
                            }
                            var installedAppList = GogOssLibrary.GetInstalledAppList();
                            if (installedAppList.ContainsKey(game.GameId))
                            {
                                installedAppList.Remove(game.GameId);
                            }
                            GogOssLibrary.Instance.installedAppListModified = true;

                            if (result.CheckboxChecked)
                            {
                                var gameSettingsFile = Path.Combine(Path.Combine(GogOssLibrary.Instance.GetPluginUserDataPath(), "GamesSettings", $"{game.GameId}.json"));
                                if (File.Exists(gameSettingsFile))
                                {
                                    File.Delete(gameSettingsFile);
                                }
                            }
                            game.IsInstalled = false;
                            game.InstallDirectory = "";
                            game.Version = "";
                            game.GameActions = null;
                            playniteAPI.Database.Games.Update(game);
                            uninstalledGames.Add(game);
                            counter += 1;
                            a.CurrentProgressValue = counter;

                        }
                    }
                }, globalProgressOptions);

                if (uninstalledGames.Count > 0)
                {
                    string uninstalledGamesCombined = uninstalledGames[0].Name;
                    if (uninstalledGames.Count > 1)
                    {
                        playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonUninstallSuccess, new Dictionary<string, IFluentType> { ["appName"] = (FluentString)uninstalledGamesCombined, ["count"] = (FluentNumber)uninstalledGames.Count }));
                        uninstalledGamesCombined = string.Join(", ", uninstalledGames.Select(item => item.Name));
                    }
                }

                if (notUninstalledGames.Count > 0)
                {
                    string notUninstalledGamesCombined = notUninstalledGames[0].Name;
                    if (notUninstalledGames.Count == 1)
                    {
                        notUninstalledGamesCombined = string.Join(", ", notUninstalledGames.Select(item => item.Name));
                        playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteGameUninstallError, new Dictionary<string, IFluentType> { ["var0"] = (FluentString)LocalizationManager.Instance.GetString(LOC.CommonCheckLog) }), notUninstalledGamesCombined);
                    }
                    else
                    {
                        playniteAPI.Dialogs.ShowMessage($"{LocalizationManager.Instance.GetString(LOC.CommonUninstallError, new Dictionary<string, IFluentType> { ["appName"] = (FluentString)notUninstalledGamesCombined, ["count"] = (FluentNumber)notUninstalledGames.Count })} {LocalizationManager.Instance.GetString(LOC.CommonCheckLog)}");
                    }
                }
            }
        }

        public override void Uninstall(UninstallActionArgs args)
        {
            Dispose();
            var games = new List<Game>
            {
                Game
            };
            LaunchUninstaller(games);
            Game.IsUninstalling = false;
        }
    }

    public class GogOssPlayController : PlayController
    {
        private static ILogger logger = LogManager.GetLogger();
        private CancellationTokenSource watcherToken;
        public int cometProcessId;
        public int gameProcessId;
        public int primaryProcessId;
        public bool cometSupportEnabled = false;
        public GameSettings gameSettings;
        public GogOssLibrarySettings globalSettings = GogOssLibrary.GetSettings();
        private IPlayniteAPI playniteAPI = API.Instance;
        public GogOssCloud gogOssCloud = new GogOssCloud();
        private static readonly RetryHandler retryHandler = new(new HttpClientHandler());
        public static readonly HttpClient httpClient = new(retryHandler);

        public GogOssPlayController(Game game) : base(game)
        {
            Name = LocalizationManager.Instance.GetString(LOC.ThirdPartyGogStartUsingClient, new Dictionary<string, IFluentType> { ["var0"] = (FluentString)"Comet" });
            gameSettings = GogOssGameSettingsView.LoadGameSettings(Game.GameId);
        }

        public override void Dispose()
        {
            watcherToken?.Dispose();
            watcherToken = null;
        }

        public override async void Play(PlayActionArgs args)
        {
            Dispose();
            if (Directory.Exists(Game.InstallDirectory))
            {
                BeforeGameStarting();
                await LaunchGame();
            }
            else
            {
                InvokeOnStopped(new GameStoppedEventArgs());
            }
        }

        public void BeforeGameStarting()
        {
            var installedInfo = GogOss.GetInstalledInfo(Game.GameId);
            if (installedInfo.is_fully_installed == false)
            {
                var playniteAPI = API.Instance;
                GlobalProgressOptions installProgressOptions = new GlobalProgressOptions(LocalizationManager.Instance.GetString(LOC.CommonFinishingInstallation), false);
                playniteAPI.Dialogs.ActivateGlobalProgress(async (a) =>
                {
                    await GogOss.CompleteInstallation(Game.GameId);
                }, installProgressOptions);
            }
            gogOssCloud.SyncGameSaves(Game, CloudSyncAction.Download);
        }

        public async Task AfterGameStarting()
        {
            cometSupportEnabled = globalSettings.EnableCometSupport;
            if (gameSettings.EnableCometSupport != null)
            {
                cometSupportEnabled = (bool)gameSettings.EnableCometSupport;
            }
            if (cometSupportEnabled && Comet.IsInstalled)
            {
                // Find game process id, cuz primary process may be launcher
                var infoManifest = GogOss.GetGogGameInfo(Game.GameId, Game.InstallDirectory);
                if (infoManifest.playTasks.Count > 0)
                {
                    bool hasLauncher = infoManifest.playTasks.Where(a => a.isPrimary && a.category == "launcher").Count() > 0;
                    if (!hasLauncher)
                    {
                        gameProcessId = primaryProcessId;
                    }
                    else
                    {
                        var gameExeTasks = infoManifest.playTasks.Where(a => a.category == "game" && (a.isPrimary || a.isHidden));
                        if (gameExeTasks.Count() == 0)
                        {
                            logger.Warn("This game has launcher, but can't find game process id, so using launcher process id");
                            gameProcessId = primaryProcessId;
                        }
                        else
                        {
                            var gameExeNames = gameExeTasks.Select(a => Path.GetFileNameWithoutExtension(a.path)).ToList();
                            while (gameProcessId == 0)
                            {
                                try
                                {
                                    foreach (var proc in Process.GetProcesses())
                                    {
                                        if (gameExeNames.Contains(proc.ProcessName))
                                        {
                                            gameProcessId = proc.Id;
                                            logger.Debug($"Found game process id: {gameProcessId}");
                                            break;
                                        }
                                    }
                                    await Task.Delay(1000);
                                }
                                catch (Exception ex)
                                {
                                    logger.Debug(ex, "An error occured during searching for game process id");
                                }
                            }
                        }
                    }
                }

                var gogAccountClient = new GogAccountClient();
                var account = await gogAccountClient.GetAccountInfo();
                if (account.isLoggedIn)
                {
                    var tokens = gogAccountClient.LoadTokens();
                    if (tokens != null)
                    {
                        var playArgs = new List<string>();
                        playArgs.AddRange(new[] { "--access-token", tokens.access_token });
                        playArgs.AddRange(new[] { "--refresh-token", tokens.refresh_token });
                        playArgs.AddRange(new[] { "--user-id", tokens.user_id });
                        playArgs.AddRange(new[] { "--username", account.username });
                        playArgs.Add("--quit");
                        var cmd = Cli.Wrap(Comet.ClientExecPath)
                                     .WithArguments(playArgs)
                                     .WithValidation(CommandResultValidation.None)
                                     .AddCommandToLog();
                        await foreach (var cmdEvent in cmd.ListenAsync())
                        {
                            switch (cmdEvent)
                            {
                                case StartedCommandEvent started:
                                    cometProcessId = started.ProcessId;

                                    bool overlayEnabled = globalSettings.EnableOverlay;
                                    if (gameSettings.EnableOverlay != null)
                                    {
                                        overlayEnabled = (bool)gameSettings.EnableOverlay;
                                    }
                                    if (overlayEnabled && GalaxyOverlay.IsInstalled)
                                    {
                                        var overlayInstalledInfo = GalaxyOverlay.GetInstalledInfo();
                                        var overlayInstallPath = overlayInstalledInfo.install_path;
                                        var overlayExe = Path.Combine(overlayInstallPath, "overlay", "GalaxyOverlay.exe");
                                        if (File.Exists(overlayExe))
                                        {
                                            var galaxyOverlay = new GalaxyOverlay();
                                            galaxyOverlay.CreateNeededDirectories();
                                            var notifyCometSuccess = await galaxyOverlay.NotifyComet(gameProcessId);
                                            if (notifyCometSuccess)
                                            {
                                                string pipeName = $"Galaxy-{gameProcessId}-CommunicationService-Overlay";

                                                Stopwatch swComet = Stopwatch.StartNew();
                                                TimeSpan timeout = TimeSpan.FromSeconds(60);
                                                bool found = false;

                                                logger.Info($"Waiting for {pipeName} pipe (timeout: {timeout.TotalSeconds}s)...");

                                                int delay = 200;
                                                while (swComet.Elapsed < timeout)
                                                {
                                                    if (Directory.GetFiles(@"\\.\pipe\", pipeName).Length > 0)
                                                    {
                                                        found = true;
                                                        break;
                                                    }
                                                    await Task.Delay(delay);
                                                    if (delay < 1000)
                                                    {
                                                        delay += 100;
                                                    }
                                                }
                                                swComet.Stop();
                                                if (found)
                                                {
                                                    logger.Info($"Pipe found after {swComet.Elapsed.TotalSeconds}s.");
                                                }
                                                else
                                                {
                                                    logger.Warn($"Pipe not found within {timeout.TotalSeconds}s. Overlay may not work.");
                                                }
                                                var screenshotPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots", Game.Name);
                                                var overlayWebPath = Path.Combine(overlayInstallPath, "web", "overlay.html");
                                                string programDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                                                var cacheOverlayPath = Path.Combine(programDataPath, "GOG.com", "Galaxy", "webcache", $"{Game.GameId}-overlay");
                                                var currentPlayniteLanguage = playniteAPI.ApplicationSettings.Language.Replace("_", "-");
                                                var overlayArgs = new List<string>
                                                {
                                                    "--advanced-features",
                                                    "--startoverlay",
                                                    $"--startup-url={new Uri(overlayWebPath)}",
                                                    $"--product-id={Game.GameId}",
                                                    $"--client-language-code={currentPlayniteLanguage}",
                                                    $"--attached-pid={gameProcessId}",
                                                    $"--cache-path={cacheOverlayPath}",
                                                    $"--screenshot-path={screenshotPath}"
                                                };

                                                var overlayCmd = Cli.Wrap(overlayExe)
                                                                    .WithArguments(overlayArgs);
                                                ProcessStarter.StartProcess(overlayExe, overlayCmd.Arguments, Path.Combine(overlayInstallPath, "overlay"));
                                            }
                                        }
                                    }
                                    break;
                                case StandardOutputCommandEvent stdOut:
                                    logger.Debug(stdOut.Text);
                                    break;
                                case StandardErrorCommandEvent stdErr:
                                    logger.Debug(stdErr.Text);
                                    break;
                            }
                        }
                    }
                }
                else
                {
                    logger.Error("User is not authenticated, so can't launch Comet");
                }
            }
        }

        public void OnGameClosed(double sessionLength)
        {
            gogOssCloud.SyncGameSaves(Game, CloudSyncAction.Upload);
            var playtimeSyncEnabled = false;
            if (playniteAPI.ApplicationSettings.PlaytimeImportMode != PlaytimeImportMode.Never)
            {
                playtimeSyncEnabled = globalSettings.SyncPlaytime;
                if (gameSettings?.AutoSyncPlaytime != null)
                {
                    playtimeSyncEnabled = (bool)gameSettings.AutoSyncPlaytime;
                }
                if (playtimeSyncEnabled)
                {
                    GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions(LocalizationManager.Instance.GetString(LOC.CommonUploadingPlaytime, new Dictionary<string, IFluentType> { ["gameTitle"] = (FluentString)Game.Name }), false);
                    playniteAPI.Dialogs.ActivateGlobalProgress(async (a) =>
                    {
                        a.IsIndeterminate = true;
                        var gogAccountClient = new GogAccountClient();
                        var accountInfo = await gogAccountClient.GetAccountInfo();
                        if (accountInfo.isLoggedIn)
                        {
                            var tokens = gogAccountClient.LoadTokens();
                            var uri = $"https://gameplay.gog.com/games/{Game.GameId}/users/{tokens.user_id}/sessions";
                            PlaytimePayload playtimePayload = new PlaytimePayload();
                            DateTimeOffset now = DateTime.UtcNow;
                            var totalSeconds = sessionLength;
                            var startTime = now.AddSeconds(-(double)totalSeconds);
                            playtimePayload.session_date = startTime.ToUnixTimeSeconds();
                            TimeSpan totalTimeSpan = TimeSpan.FromSeconds(totalSeconds);
                            playtimePayload.time = totalTimeSpan.Minutes;
                            var playtimeJson = Serialization.ToJson(playtimePayload);
                            var content = new StringContent(playtimeJson, Encoding.UTF8, "application/json");
                            a.CurrentProgressValue = 1;
                            if (playtimePayload.time > 0)
                            {
                                var request = new HttpRequestMessage(HttpMethod.Post, uri)
                                {
                                    Content = content
                                };
                                request.Headers.Add("Authorization", $"Bearer {tokens.access_token}");
                                request.Headers.Add("User-Agent", GogOssCloud.UserAgent);
                                try
                                {
                                    using var response = await httpClient.SendAsync(request);
                                    response.EnsureSuccessStatusCode();
                                }
                                catch (Exception ex)
                                {
                                    playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.CommonUploadPlaytimeError, new Dictionary<string, IFluentType> { ["gameTitle"] = (FluentString)Game.Name }));
                                    logger.Error(ex, $"An error occured during uploading playtime to the cloud.");
                                }
                            }
                        }
                        else
                        {
                            playniteAPI.Dialogs.ShowErrorMessage(LocalizationManager.Instance.GetString(LOC.ThirdPartyGogNotLoggedInError), "");
                            logger.Error($"Can't upload playtime, because user is not authenticated.");
                        }
                    }, globalProgressOptions);
                }
            }

            if (cometSupportEnabled && Comet.IsInstalled)
            {
                Process cometProcess = null;
                try
                {
                    cometProcess = Process.GetProcessById(cometProcessId);
                }
                catch (Exception)
                {

                }
                if (cometProcess != null && !cometProcess.HasExited)
                {
                    cometProcess.Kill();
                }
            }
        }

        public async Task LaunchGame()
        {
            Dispose();
            if (Directory.Exists(Game.InstallDirectory))
            {
                var task = GogOssLibrary.GetPlayTasks(Game.GameId, Game.InstallDirectory);
                var gameExe = task[0].Path;
                var workingDir = task[0].WorkingDir;
                var gameExeFullPath = gameExe;
                if (!workingDir.IsNullOrEmpty())
                {
                    gameExeFullPath = Path.Combine(workingDir, gameExe);
                }
                if (File.Exists(gameExeFullPath))
                {
                    var playArgs = new List<string>();
                    var gameSettings = GogOssGameSettingsView.LoadGameSettings(Game.GameId);
                    if (!task[0].Arguments.IsNullOrEmpty())
                    {
                        var providedArgs = Helpers.SplitArguments(task[0].Arguments);
                        playArgs.AddRange(providedArgs);
                    }
                    if (gameSettings.StartupArguments?.Any() == true)
                    {
                        playArgs.AddRange(gameSettings.StartupArguments);
                    }
                    if (!gameSettings.OverrideExe.IsNullOrEmpty())
                    {
                        gameExe = gameSettings.OverrideExe;
                        gameExeFullPath = gameExe;
                    }
                    if (workingDir.IsNullOrEmpty())
                    {
                        workingDir = Game.InstallDirectory;
                    }

                    var cmd = Cli.Wrap(gameExeFullPath)
                                 .WithArguments(playArgs);

                    if (!Directory.Exists(workingDir))
                    {
                        logger.Error($"Working directory {workingDir} doesn't exists.");
                        workingDir = Game.InstallDirectory;
                    }
                    var primaryProcess = ProcessStarter.StartProcess(gameExeFullPath, cmd.Arguments, workingDir);
                    InvokeOnStarted(new GameStartedEventArgs() { StartedProcessId = primaryProcess.Id });
                    var monitor = new MonitorProcessTree(primaryProcess.Id);
                    StartTracking(() => monitor.IsProcessTreeRunning());
                    primaryProcessId = primaryProcess.Id;
                    await AfterGameStarting();
                }
                else
                {
                    InvokeOnStopped(new GameStoppedEventArgs());
                }
            }
            else
            {
                InvokeOnStopped(new GameStoppedEventArgs());
            }
        }

        public void StartTracking(Func<bool> trackingAction,
                                  Func<int> startupCheck = null,
                                  int trackingFrequency = 2000,
                                  int trackingStartDelay = 0)
        {
            if (watcherToken != null)
            {
                throw new Exception("Game is already being tracked.");
            }

            watcherToken = new CancellationTokenSource();
            Task.Run(async () =>
            {
                ulong playTimeMs = 0;
                var trackingWatch = new Stopwatch();
                var maxFailCount = 5;
                var failCount = 0;

                if (trackingStartDelay > 0)
                {
                    await Task.Delay(trackingStartDelay, watcherToken.Token).ContinueWith(task => { });
                }

                if (startupCheck != null)
                {
                    while (true)
                    {
                        if (watcherToken.IsCancellationRequested)
                        {
                            return;
                        }

                        if (failCount >= maxFailCount)
                        {
                            InvokeOnStopped(new GameStoppedEventArgs(0));
                            return;
                        }

                        try
                        {
                            var id = startupCheck();
                            if (id > 0)
                            {
                                InvokeOnStarted(new GameStartedEventArgs { StartedProcessId = id });
                                break;
                            }
                        }
                        catch (Exception e)
                        {
                            failCount++;
                            logger.Error(e, "Game startup tracking iteration failed.");
                        }

                        await Task.Delay(trackingFrequency, watcherToken.Token).ContinueWith(task => { });
                    }
                }

                while (true)
                {
                    failCount = 0;
                    if (watcherToken.IsCancellationRequested)
                    {
                        return;
                    }

                    if (failCount >= maxFailCount)
                    {
                        var playTimeS = playTimeMs / 1000;
                        InvokeOnStopped(new GameStoppedEventArgs(playTimeS));
                        OnGameClosed(playTimeS);
                        return;
                    }

                    try
                    {
                        trackingWatch.Restart();
                        if (!trackingAction())
                        {
                            var playTimeS = playTimeMs / 1000;
                            InvokeOnStopped(new GameStoppedEventArgs(playTimeS));
                            OnGameClosed(playTimeS);
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        failCount++;
                        logger.Error(e, "Game tracking iteration failed.");
                    }

                    await Task.Delay(trackingFrequency, watcherToken.Token).ContinueWith(task => { });
                    trackingWatch.Stop();
                    if (trackingWatch.ElapsedMilliseconds > (trackingFrequency + 30_000))
                    {
                        // This is for cases where system is put into sleep or hibernation.
                        // Realistically speaking, one tracking interation should never take 30+ seconds,
                        // but lets use that as safe value in case this runs super slowly on some weird PCs.
                        continue;
                    }

                    playTimeMs += (ulong)trackingWatch.ElapsedMilliseconds;
                }
            });
        }

    }

    public class GogOssUpdateController
    {
        private IPlayniteAPI playniteAPI = API.Instance;
        private static ILogger logger = LogManager.GetLogger();
        public GogDownloadApi gogDownloadApi = new GogDownloadApi();

        public async Task<Dictionary<string, UpdateInfo>> CheckGameUpdates(string gameTitle, string gameId, bool forceRefreshCache = false)
        {
            var gamesToUpdate = new Dictionary<string, UpdateInfo>();
            var installedInfo = GogOss.GetInstalledInfo(gameId);
            var oldGameData = new DownloadManagerData.Download
            {
                gameID = gameId,
                name = gameTitle
            };
            var newGameInfo = await gogDownloadApi.GetProductBuilds(oldGameData, forceRefreshCache);

            if (newGameInfo.items.Count > 0)
            {
                bool updateAvailable = false;
                var newBuild = newGameInfo.items.FirstOrDefault(i => i.branch == "");
                var oldBuildBranch = newGameInfo.items.FirstOrDefault(i => i.build_id == installedInfo.build_id)?.branch;
                if (oldBuildBranch.IsNullOrEmpty())
                {
                    oldBuildBranch = newGameInfo.items.FirstOrDefault(i => i.legacy_build_id == installedInfo.build_id)?.branch;
                }
                if (!oldBuildBranch.IsNullOrEmpty())
                {
                    newBuild = newGameInfo.items[0];
                }
                if (!newBuild.legacy_build_id.IsNullOrEmpty())
                {
                    if (installedInfo.build_id != newBuild.legacy_build_id)
                    {
                        updateAvailable = true;
                        if (!newBuild.build_id.IsNullOrEmpty())
                        {
                            if (installedInfo.build_id == newBuild.build_id)
                            {
                                updateAvailable = false;
                            }
                        }
                    }
                }
                else if (installedInfo.build_id != newBuild.build_id)
                {
                    updateAvailable = true;
                }
                if (updateAvailable)
                {
                    var updateSize = await GogOss.CalculateGameSize(gameId, installedInfo);
                    DateTimeFormatInfo formatInfo = CultureInfo.CurrentCulture.DateTimeFormat;
                    var newVersionName = $"{newBuild.version_name} — ";
                    if (newBuild.version_name.IsNullOrEmpty())
                    {
                        newVersionName = "";
                    }
                    newVersionName = $"{newVersionName}{newBuild.date_published.ToLocalTime().ToString("d", formatInfo)}";
                    var newManifest = await gogDownloadApi.GetGameMetaManifest(newBuild.build_id, newBuild.branch, newBuild.os);
                    var updateInfo = new UpdateInfo
                    {
                        Install_path = installedInfo.install_path,
                        Version = newBuild.version_name,
                        Title = installedInfo.title,
                        Title_for_updater = $"{installedInfo.title} {newVersionName}",
                        Download_size = updateSize.download_size,
                        Disk_size = updateSize.disk_size,
                        Build_id = newBuild.build_id,
                        Language = installedInfo.language,
                        ExtraContent = installedInfo.installed_DLCs,
                    };
                    if (!newBuild.branch.IsNullOrEmpty())
                    {
                        updateInfo.BetaChannel = newBuild.branch;
                    }
                    gamesToUpdate.Add(gameId, updateInfo);
                }
            }
            else
            {
                logger.Error($"An error occured during checking {gameTitle} updates.");
                var updateInfo = new UpdateInfo
                {
                    Version = "0",
                    Title = gameTitle,
                    Download_size = 0,
                    Disk_size = 0,
                    Success = false
                };
                gamesToUpdate.Add(gameId, updateInfo);
            }
            return gamesToUpdate;
        }

        public async Task UpdateGame(Dictionary<string, UpdateInfo> gamesToUpdate, string gameTitle = "", bool silently = false, DownloadProperties downloadProperties = null)
        {
            var updateTasks = new List<DownloadManagerData.Download>();
            if (gamesToUpdate.Count > 0)
            {
                bool canUpdate = true;
                if (canUpdate)
                {
                    if (silently)
                    {
                        var playniteApi = API.Instance;
                        playniteApi.Notifications.Add(new NotificationMessage("GogOssGamesUpdates", LocalizationManager.Instance.GetString(LOC.CommonGamesUpdatesUnderway), NotificationType.Info));
                    }
                    GogOssDownloadManagerView downloadManager = GogOssLibrary.GetGogOssDownloadManager();
                    foreach (var gameToUpdate in gamesToUpdate)
                    {
                        var wantedItem = downloadManager.downloadManagerData.downloads.FirstOrDefault(item => item.gameID == gameToUpdate.Key);
                        if (wantedItem != null)
                        {
                            if (wantedItem.status == DownloadStatus.Completed)
                            {
                                downloadManager.downloadManagerData.downloads.Remove(wantedItem);
                                downloadManager.downloadsChanged = true;
                                wantedItem = null;
                            }
                        }
                        if (wantedItem != null)
                        {
                            if (!silently)
                            {
                                playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonDownloadAlreadyExists, new Dictionary<string, IFluentType> { ["appName"] = (FluentString)wantedItem.name }), "", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        else
                        {
                            var settings = GogOssLibrary.GetSettings();
                            DownloadProperties newDownloadProperties = new()
                            {
                                downloadAction = DownloadAction.Update,
                                maxWorkers = settings.MaxWorkers,
                            };
                            if (downloadProperties != null)
                            {
                                newDownloadProperties = Serialization.GetClone(downloadProperties);
                            }
                            newDownloadProperties.buildId = gameToUpdate.Value.Build_id;
                            newDownloadProperties.version = gameToUpdate.Value.Version;
                            newDownloadProperties.language = gameToUpdate.Value.Language;
                            newDownloadProperties.extraContent = gameToUpdate.Value.ExtraContent;
                            newDownloadProperties.os = gameToUpdate.Value.Os;
                            newDownloadProperties.installPath = gameToUpdate.Value.Install_path;
                            if (!gameToUpdate.Value.BetaChannel.IsNullOrEmpty())
                            {
                                newDownloadProperties.betaChannel = gameToUpdate.Value.BetaChannel;
                            }
                            var updateTask = new DownloadManagerData.Download
                            {
                                gameID = gameToUpdate.Key,
                                name = gameToUpdate.Value.Title,
                                downloadSizeNumber = gameToUpdate.Value.Download_size,
                                installSizeNumber = gameToUpdate.Value.Disk_size,
                                downloadProperties = newDownloadProperties,
                                downloadItemType = gameToUpdate.Value.DownloadItemType,
                            };
                            updateTask.fullInstallPath = gameToUpdate.Value.Install_path;
                            updateTasks.Add(updateTask);
                        }
                    }
                    if (updateTasks.Count > 0)
                    {
                        await downloadManager.EnqueueMultipleJobs(updateTasks, silently);
                    }
                }
            }
            else if (!silently)
            {
                playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonNoUpdatesAvailable), gameTitle);
            }
        }

        public async Task<Dictionary<string, UpdateInfo>> CheckAllGamesUpdates()
        {
            var appList = GogOssLibrary.GetInstalledAppList();
            var gamesToUpdate = new Dictionary<string, UpdateInfo>();
            foreach (var game in appList.OrderBy(item => item.Value.title))
            {
                var gameID = game.Key;
                var gameSettings = GogOssGameSettingsView.LoadGameSettings(gameID);
                bool canUpdate = true;
                if (gameSettings.DisableGameVersionCheck == true)
                {
                    canUpdate = false;
                }
                if (canUpdate)
                {
                    GogOssUpdateController GogOssUpdateController = new GogOssUpdateController();
                    var gameToUpdate = await GogOssUpdateController.CheckGameUpdates(game.Value.title, gameID);
                    if (gameToUpdate.Count > 0)
                    {
                        foreach (var singleGame in gameToUpdate)
                        {
                            gamesToUpdate.Add(singleGame.Key, singleGame.Value);
                        }
                    }
                }
            }
            return gamesToUpdate;
        }
    }
}
