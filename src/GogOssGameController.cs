using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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

        private void LaunchInstaller(List<DownloadManagerData.Download> installData)
        {
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
        private CancellationTokenSource watcherToken;

        public GogOssUninstallController(Game game) : base(game)
        {
            Name = "Uninstall";
        }

        public override void Dispose()
        {
            watcherToken?.Cancel();
        }

        public override void Uninstall(UninstallActionArgs args)
        {
            Dispose();
            var result = MessageCheckBoxDialog.ShowMessage(ResourceProvider.GetString(LOC.GogOss3P_PlayniteUninstallGame), ResourceProvider.GetString(LOC.GogOssUninstallGameConfirm).Format(Game.Name), LOC.GogOssRemoveGameLaunchSettings, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result.Result)
            {
                var canContinue = GogOssLibrary.Instance.StopDownloadManager(true);
                if (!canContinue)
                {
                    return;
                }
                if (result.CheckboxChecked)
                {
                    var gameSettingsFile = Path.Combine(Path.Combine(GogOssLibrary.Instance.GetPluginUserDataPath(), "GamesSettings", $"{Game.GameId}.json"));
                    if (File.Exists(gameSettingsFile))
                    {
                        File.Delete(gameSettingsFile);
                    }
                }
                var installedAppList = GogOssLibrary.GetInstalledAppList();
                if (installedAppList.ContainsKey(Game.GameId))
                {
                    installedAppList.Remove(Game.GameId);
                    Helpers.SaveJsonSettingsToFile(installedAppList, "installed.json");
                }
                var manifestFile = Path.Combine(Gogdl.ConfigPath, "manifests", Game.GameId);
                if (File.Exists(manifestFile))
                {
                    File.Delete(manifestFile);
                }
                var uninstaller = Path.Combine(Game.InstallDirectory, "unins000.exe");
                if (!File.Exists(uninstaller))
                {
                    throw new FileNotFoundException("Uninstaller not found.");
                }

                Process.Start(uninstaller);
                StartUninstallWatcher();
            }
        }

        public async void StartUninstallWatcher()
        {
            watcherToken = new CancellationTokenSource();
            while (true)
            {
                if (watcherToken.IsCancellationRequested)
                {
                    return;
                }

                var games = GogOssLibrary.GetInstalledGames();
                if (!games.ContainsKey(Game.GameId))
                {
                    InvokeOnUninstalled(new GameUninstalledEventArgs());
                    return;
                }

                await Task.Delay(5000);
            }
        }
    }

    public class CometPlayController : PlayController
    {
        private static ILogger logger = LogManager.GetLogger();
        private CancellationTokenSource watcherToken;
        public int cometProcessId;

        public CometPlayController(Game game) : base(game)
        {
            Name = string.Format(ResourceProvider.GetString(LOC.GogOss3P_GOGStartUsingClient), "Comet");
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
                LaunchGame();
                await AfterGameStarting();
            }
            else
            {
                InvokeOnStopped(new GameStoppedEventArgs());
            }
        }

        public void BeforeGameStarting()
        {

        }

        public async Task AfterGameStarting()
        {
            if (GogOssLibrary.GetSettings().EnableCometSupport && Comet.IsInstalled)
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
            if (GogOssLibrary.GetSettings().EnableCometSupport && Comet.IsInstalled)
            {
                Process cometProcess = null;
                try
                {
                    cometProcess = Process.GetProcessById(cometProcessId);
                }
                catch (Exception)
                {

                }
                if (!cometProcess.HasExited)
                {
                    cometProcess.Kill();
                }
            }
        }

        public void LaunchGame()
        {
            Dispose();
            if (Directory.Exists(Game.InstallDirectory))
            {
                var task = GogOssLibrary.GetPlayTasks(Game.GameId, Game.InstallDirectory);
                var gameExe = task[0].Path;
                Process proc;
                if (File.Exists(gameExe))
                {
                    proc = ProcessStarter.StartProcess(gameExe);
                    InvokeOnStarted(new GameStartedEventArgs() { StartedProcessId = proc.Id });
                    var monitor = new MonitorProcessTree(proc.Id);
                    StartTracking(() => monitor.IsProcessTreeRunning());
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
