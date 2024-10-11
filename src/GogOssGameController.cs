using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CliWrap;
using CliWrap.EventStream;
using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Playnite.Common;
using Playnite.SDK;
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
            Game.IsInstalling = false;
        }

        public static void LaunchInstaller(List<DownloadManagerData.Download> installData)
        {
            if (!Gogdl.IsInstalled)
            {
                throw new Exception(ResourceProvider.GetString(LOC.GogOssGogdlNotInstalled));
            }
            var playniteAPI = API.Instance;
            Window window = null;
            if (playniteAPI.ApplicationInfo.Mode == ApplicationMode.Desktop)
            {
                window = playniteAPI.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMaximizeButton = false,
                });
            }
            else
            {
                window = new Window
                {
                    Background = System.Windows.Media.Brushes.DodgerBlue
                };
            }
            window.DataContext = installData;
            window.Content = new GogOssGameInstallerView();
            window.Owner = playniteAPI.Dialogs.GetCurrentAppWindow();
            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.MinWidth = 600;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var title = ResourceProvider.GetString(LOC.GogOss3P_PlayniteInstallGame);
            if (installData[0].downloadProperties.downloadAction == DownloadAction.Repair)
            {
                title = ResourceProvider.GetString(LOC.GogOssRepair);
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
            var result = MessageCheckBoxDialog.ShowMessage(ResourceProvider.GetString(LOC.GogOss3P_PlayniteUninstallGame), ResourceProvider.GetString(LOC.GogOssUninstallGameConfirm).Format(gamesCombined), LOC.GogOssRemoveGameLaunchSettings, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result.Result)
            {
                var uninstalledGames = new List<Game>();
                GlobalProgressOptions globalProgressOptions = new GlobalProgressOptions($"{ResourceProvider.GetString(LOC.GogOss3P_PlayniteUninstalling)}... ", false);
                playniteAPI.Dialogs.ActivateGlobalProgress(async (a) =>
                {
                    a.IsIndeterminate = false;
                    a.ProgressMaxValue = games.Count;
                    using (playniteAPI.Database.BufferedUpdate())
                    {
                        var counter = 0;
                        foreach (var game in games)
                        {
                            a.Text = $"{ResourceProvider.GetString(LOC.GogOss3P_PlayniteUninstalling)} {game.Name}... ";
                            var installedAppList = GogOssLibrary.GetInstalledAppList();
                            if (installedAppList.ContainsKey(game.GameId))
                            {
                                installedAppList.Remove(game.GameId);
                            }
                            GogOssLibrary.Instance.installedAppListModified = true;
                            var manifestFile = Path.Combine(Gogdl.ConfigPath, "manifests", game.GameId);
                            if (File.Exists(manifestFile))
                            {
                                File.Delete(manifestFile);
                            }
                            var uninstaller = Path.Combine(game.InstallDirectory, "unins000.exe");
                            if (File.Exists(uninstaller))
                            {
                                var uninstallArgs = new List<string>
                                {
                                    "/VERYSILENT",
                                    $"/ProductId={game.GameId}",
                                    "/galaxyclient",
                                    "/KEEPSAVES"
                                };
                                await Cli.Wrap(uninstaller)
                                         .WithArguments(uninstallArgs)
                                         .AddCommandToLog()
                                         .ExecuteAsync();
                            }
                            if (Directory.Exists(game.InstallDirectory))
                            {
                                Directory.Delete(game.InstallDirectory, true);
                            }

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
                            playniteAPI.Database.Games.Update(game);
                            uninstalledGames.Add(game);
                            counter += 1;
                            a.CurrentProgressValue = counter;

                        }
                    }
                }, globalProgressOptions);

                if (uninstalledGames.Count > 0)
                {
                    if (uninstalledGames.Count == 1)
                    {
                        playniteAPI.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.GogOssUninstallSuccess).Format(uninstalledGames[0].Name));
                    }
                    else
                    {
                        string uninstalledGamesCombined = string.Join(", ", uninstalledGames.Select(item => item.Name));
                        playniteAPI.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.GogOssUninstallSuccessOther).Format(uninstalledGamesCombined));
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
        public bool cometSupportEnabled = false;
        public GameSettings gameSettings;
        public GogOssLibrarySettings globalSettings = GogOssLibrary.GetSettings();

        public GogOssPlayController(Game game) : base(game)
        {
            Name = string.Format(ResourceProvider.GetString(LOC.GogOss3P_GOGStartUsingClient), "Comet");
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
            var installedAppList = GogOssLibrary.GetInstalledAppList();
            if (installedAppList.ContainsKey(Game.GameId))
            {
                var installedInfo = GogOss.GetInstalledInfo(Game.GameId);
                if (installedInfo.is_fully_installed == false)
                {
                    var playniteAPI = API.Instance;
                    GlobalProgressOptions installProgressOptions = new GlobalProgressOptions(ResourceProvider.GetString(LOC.GogOssFinishingInstallation), false);
                    playniteAPI.Dialogs.ActivateGlobalProgress(async (a) =>
                    {
                        await GogOss.CompleteInstallation(Game.GameId);
                        var depends = installedInfo.Dependencies.ToList();
                        if (depends.Count > 0)
                        {
                            bool installedDependsModified = false;
                            var installedDepends = Gogdl.GetInstalledDepends();
                            foreach (var depend in depends)
                            {
                                if (!installedDepends.Contains(depend))
                                {
                                    var dependManifest = await Gogdl.GetRedistInfo(depend);
                                    if (dependManifest.executable.path != "")
                                    {
                                        var dependExe = Path.GetFullPath(Path.Combine(Gogdl.DependenciesInstallationPath, dependManifest.executable.path));
                                        if (File.Exists(dependExe))
                                        {
                                            var process = ProcessStarter.StartProcess(dependExe, dependManifest.executable.arguments, true);
                                            process.WaitForExit();
                                        }
                                    }
                                    installedInfo.Dependencies.Remove(depend);
                                    installedDepends.Add(depend);
                                    installedDependsModified = true;
                                }
                            }
                            if (installedDependsModified)
                            {
                                var installedDependsManifest = new InstalledDepends();
                                installedDependsManifest.InstalledDependsList = installedDepends;
                                Helpers.SaveJsonSettingsToFile(installedDependsManifest, "installedDepends");
                            }
                        }
                        installedInfo.is_fully_installed = true;
                        GogOssLibrary.Instance.installedAppListModified = true;
                    }, installProgressOptions);
                }
            }
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
                var gogAccountClient = new GogAccountClient();
                var tokens = gogAccountClient.LoadTokens();
                if (tokens != null)
                {
                    var account = await gogAccountClient.GetAccountInfo();
                    if (account.isLoggedIn)
                    {
                        var playArgs = new List<string>();
                        playArgs.AddRange(new[] { "--access-token", tokens.access_token });
                        playArgs.AddRange(new[] { "--refresh-token", tokens.refresh_token });
                        playArgs.AddRange(new[] { "--user-id", tokens.user_id });
                        playArgs.AddRange(new[] { "--username", account.username });
                        playArgs.Add("--quit");
                        logger.Info($"Launching Comet ({Comet.ClientExecPath}).");
                        var cmd = Cli.Wrap(Comet.ClientExecPath)
                                     .WithArguments(playArgs)
                                     .WithValidation(CommandResultValidation.None);
                        await foreach (var cmdEvent in cmd.ListenAsync())
                        {
                            switch (cmdEvent)
                            {
                                case StartedCommandEvent started:
                                    cometProcessId = started.ProcessId;
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

            }
        }

        public void OnGameClosed(double sessionLength)
        {
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
                if (File.Exists(gameExe))
                {
                    var playArgs = new List<string>();
                    var gameSettings = GogOssGameSettingsView.LoadGameSettings(Game.GameId);
                    if (gameSettings.StartupArguments?.Any() == true)
                    {
                        playArgs.AddRange(gameSettings.StartupArguments);
                    }
                    if (!gameSettings.OverrideExe.IsNullOrEmpty())
                    {
                        gameExe = gameSettings.OverrideExe;
                    }
                    var cmd = Cli.Wrap(gameExe)
                                 .WithArguments(playArgs)
                                 .AddCommandToLog()
                                 .WithValidation(CommandResultValidation.None);
                    await foreach (var cmdEvent in cmd.ListenAsync())
                    {
                        switch (cmdEvent)
                        {
                            case StartedCommandEvent started:
                                InvokeOnStarted(new GameStartedEventArgs() { StartedProcessId = started.ProcessId });
                                var monitor = new MonitorProcessTree(started.ProcessId);
                                StartTracking(() => monitor.IsProcessTreeRunning());
                                await AfterGameStarting();
                                break;
                        }
                    }
                }
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
                        OnGameClosed(playTimeS);
                        InvokeOnStopped(new GameStoppedEventArgs(playTimeS));
                        return;
                    }

                    try
                    {
                        trackingWatch.Restart();
                        if (!trackingAction())
                        {
                            var playTimeS = playTimeMs / 1000;
                            OnGameClosed(playTimeS);
                            InvokeOnStopped(new GameStoppedEventArgs(playTimeS));
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
}
