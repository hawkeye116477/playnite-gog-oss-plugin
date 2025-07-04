﻿using CliWrap;
using CliWrap.EventStream;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.IO;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Enums;
using Playnite.SDK.Data;
using Playnite.Common;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Playnite.SDK.Plugins;
using System.Collections.Specialized;
using System.Text;
using CommonPlugin.Enums;
using CommonPlugin;
using Playnite.SDK.Models;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Interaction logic for GogOssDownloadManagerView.xaml
    /// </summary>
    public partial class GogOssDownloadManagerView : UserControl
    {
        public CancellationTokenSource forcefulInstallerCTS;
        public CancellationTokenSource gracefulInstallerCTS;
        private ILogger logger = LogManager.GetLogger();
        private IPlayniteAPI playniteAPI = API.Instance;
        public DownloadManagerData downloadManagerData;
        public SidebarItem gogPanel = GogOssLibrary.GetPanel();
        public bool downloadsChanged = false;

        public GogOssDownloadManagerView()
        {
            InitializeComponent();

            SelectAllBtn.ToolTip = GetToolTipWithKey(LOC.GogOssSelectAllEntries, "Ctrl+A");
            RemoveDownloadBtn.ToolTip = GetToolTipWithKey(LOC.GogOssRemoveEntry, "Delete");
            MoveTopBtn.ToolTip = GetToolTipWithKey(LOC.GogOssMoveEntryTop, "Alt+Home");
            MoveUpBtn.ToolTip = GetToolTipWithKey(LOC.GogOssMoveEntryUp, "Alt+Up");
            MoveDownBtn.ToolTip = GetToolTipWithKey(LOC.GogOssMoveEntryDown, "Alt+Down");
            MoveBottomBtn.ToolTip = GetToolTipWithKey(LOC.GogOssMoveEntryBottom, "Alt+End");
            DownloadPropertiesBtn.ToolTip = GetToolTipWithKey(LOC.GogOssEditSelectedDownloadProperties, "Ctrl+P");
            OpenDownloadDirectoryBtn.ToolTip = GetToolTipWithKey(LOC.GogOssOpenDownloadDirectory, "Ctrl+O");
            LoadSavedData();
            foreach (DownloadManagerData.Download download in downloadManagerData.downloads)
            {
                download.PropertyChanged += OnPropertyChanged;
            }
            downloadManagerData.downloads.CollectionChanged += OnCollectionChanged;
            var runningAndQueuedDownloads = downloadManagerData.downloads.Where(i => i.status == DownloadStatus.Running
                                                                                     || i.status == DownloadStatus.Queued).ToList();
            if (runningAndQueuedDownloads.Count > 0)
            {
                foreach (var download in runningAndQueuedDownloads)
                {
                    download.status = DownloadStatus.Paused;
                }
            }
        }

        public void OnPropertyChanged(object _, PropertyChangedEventArgs arg)
        {
            downloadsChanged = true;
        }

        public void OnCollectionChanged(object _, NotifyCollectionChangedEventArgs arg)
        {
            downloadsChanged = true;
        }

        public RelayCommand<object> NavigateBackCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                if (playniteAPI.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
                {
                    Window.GetWindow(this).Close();
                }
                else
                {
                    playniteAPI.MainView.SwitchToLibraryView();
                }
            });
        }

        public string GetToolTipWithKey(string description, string shortcut)
        {
            return $"{ResourceProvider.GetString(description)} [{shortcut}]";
        }

        public DownloadManagerData LoadSavedData()
        {
            var dataDir = GogOssLibrary.Instance.GetPluginUserDataPath();
            var dataFile = Path.Combine(dataDir, "downloadManager.json");
            bool correctJson = false;
            if (File.Exists(dataFile))
            {
                var content = FileSystem.ReadFileAsStringSafe(dataFile);
                if (!content.IsNullOrWhiteSpace() && Serialization.TryFromJson(content, out downloadManagerData))
                {
                    if (downloadManagerData != null && downloadManagerData.downloads != null)
                    {
                        correctJson = true;
                    }
                }
            }
            if (!correctJson)
            {
                downloadManagerData = new DownloadManagerData
                {
                    downloads = new ObservableCollection<DownloadManagerData.Download>()
                };
            }
            DownloadsDG.ItemsSource = downloadManagerData.downloads;
            return downloadManagerData;
        }

        public void SaveData()
        {
            if (downloadsChanged)
            {
                var commonHelpers = GogOssLibrary.Instance.commonHelpers;
                commonHelpers.SaveJsonSettingsToFile(downloadManagerData, "", "downloadManager", true);
            }
        }

        public async Task DoNextJobInQueue()
        {
            var running = downloadManagerData.downloads.Any(item => item.status == DownloadStatus.Running);
            var queuedList = downloadManagerData.downloads.Where(i => i.status == DownloadStatus.Queued).ToList();
            if (!running)
            {
                DiskSpeedTB.Text = "";
                DownloadSpeedTB.Text = "";
                ElapsedTB.Text = "";
                EtaTB.Text = "";
                gogPanel.ProgressValue = 0;
                DescriptionTB.Text = "";
                GameTitleTB.Text = "";
            }
            if (!running && queuedList.Count > 0)
            {
                await Install(queuedList[0]);
            }
            else if (!running)
            {
                SaveData();
                downloadsChanged = false;
                var downloadCompleteSettings = GogOssLibrary.GetSettings().DoActionAfterDownloadComplete;
                if (downloadCompleteSettings != DownloadCompleteAction.Nothing)
                {
                    Window window = playniteAPI.Dialogs.CreateWindow(new WindowCreationOptions
                    {
                        ShowMaximizeButton = false,
                    });
                    window.Title = "GOG OSS library integration";
                    window.Content = new GogOssDownloadCompleteActionView();
                    window.Owner = playniteAPI.Dialogs.GetCurrentAppWindow();
                    window.SizeToContent = SizeToContent.WidthAndHeight;
                    window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    window.ShowDialog();
                }
            }
        }

        public void DisplayGreeting()
        {
            var messagesSettings = GogOssMessagesSettings.LoadSettings();
            if (!messagesSettings.DontShowDownloadManagerWhatsUpMsg)
            {
                var result = MessageCheckBoxDialog.ShowMessage("", ResourceProvider.GetString(LOC.GogOssDownloadManagerWhatsUp), ResourceProvider.GetString(LOC.GogOss3P_PlayniteDontShowAgainTitle), MessageBoxButton.OK, MessageBoxImage.Information);
                if (result.CheckboxChecked)
                {
                    messagesSettings.DontShowDownloadManagerWhatsUpMsg = true;
                    GogOssMessagesSettings.SaveSettings(messagesSettings);
                }
            }
        }

        public async Task EnqueueMultipleJobs(List<DownloadManagerData.Download> downloadManagerDataList, bool silently = false)
        {
            if (!silently)
            {
                DisplayGreeting();
            }
            foreach (var downloadJob in downloadManagerDataList)
            {
                var wantedItem = downloadManagerData.downloads.FirstOrDefault(item => item.gameID == downloadJob.gameID);
                if (wantedItem == null)
                {
                    DateTimeOffset now = DateTime.UtcNow;
                    downloadJob.status = DownloadStatus.Queued;
                    downloadJob.addedTime = now.ToUnixTimeSeconds();
                    downloadManagerData.downloads.Add(downloadJob);
                }
                else
                {
                    wantedItem.status = DownloadStatus.Queued;
                }
            }
            await DoNextJobInQueue();
        }

        public async Task Install(DownloadManagerData.Download taskData)
        {
            var installCommand = new List<string>();
            var settings = GogOssLibrary.GetSettings();
            var gameID = taskData.gameID;
            var downloadProperties = taskData.downloadProperties;
            var gameTitle = taskData.name;
            double cachedDownloadSizeNumber = taskData.downloadSizeNumber;
            double downloadCache = 0;
            bool downloadSpeedInBits = false;
            if (settings.DisplayDownloadSpeedInBits)
            {
                downloadSpeedInBits = true;
            }

            installCommand.AddRange(new[] { "--auth-config-path", GogOss.TokensPath });

            if (taskData.downloadItemType != DownloadItemType.Dependency)
            {
                if (downloadProperties.downloadAction == DownloadAction.Install)
                {
                    installCommand.Add("download");
                    var manifestFile = Path.Combine(Gogdl.ConfigPath, "manifests", taskData.gameID);
                    if (File.Exists(manifestFile))
                    {
                        File.Delete(manifestFile);
                    }
                }
                if (downloadProperties.downloadAction == DownloadAction.Repair)
                {
                    installCommand.Add("repair");
                }
                if (downloadProperties.downloadAction == DownloadAction.Update)
                {
                    installCommand.Add("update");
                }
                installCommand.Add(taskData.gameID);
            }
            else
            {
                installCommand.AddRange(new[] { "redist", "--ids", string.Join(",", taskData.depends) });
            }

            if (downloadProperties.installPath != "")
            {
                installCommand.AddRange(new[] { "--path", downloadProperties.installPath });
            }
            if (downloadProperties.maxWorkers != 0)
            {
                installCommand.AddRange(new[] { "--max-workers", downloadProperties.maxWorkers.ToString() });
            }
            if (downloadProperties.betaChannel != "disabled")
            {
                installCommand.AddRange(new[] { "--branch", downloadProperties.betaChannel });
            }
            if (!downloadProperties.buildId.IsNullOrEmpty())
            {
                installCommand.AddRange(new[] { "--build", downloadProperties.buildId });
            }
            if (!downloadProperties.language.IsNullOrEmpty())
            {
                installCommand.AddRange(new[] { "--lang", downloadProperties.language });
            }
            installCommand.AddRange(new[] { "--platform", downloadProperties.os });
            if (downloadProperties.extraContent.Count == 0)
            {
                installCommand.Add("--skip-dlcs");
            }
            else
            {
                installCommand.Add("--with-dlcs");
                installCommand.AddRange(new[] { "--dlcs", string.Join(",", downloadProperties.extraContent) });
            }
            forcefulInstallerCTS = new CancellationTokenSource();
            gracefulInstallerCTS = new CancellationTokenSource();
            try
            {
                bool errorDisplayed = false;
                bool successDisplayed = false;
                bool loginErrorDisplayed = false;
                bool permissionErrorDisplayed = false;
                bool diskSpaceErrorDisplayed = false;
                var cmd = Cli.Wrap(Gogdl.ClientInstallationPath)
                             .WithArguments(installCommand)
                             .WithEnvironmentVariables(Gogdl.DefaultEnvironmentVariables)
                             .AddCommandToLog()
                             .WithValidation(CommandResultValidation.None);
                var wantedItem = downloadManagerData.downloads.FirstOrDefault(item => item.gameID == gameID);
                await foreach (CommandEvent cmdEvent in cmd.ListenAsync(Encoding.Default, Encoding.Default, forcefulInstallerCTS.Token, gracefulInstallerCTS.Token))
                {
                    switch (cmdEvent)
                    {
                        case StartedCommandEvent started:
                            wantedItem.status = DownloadStatus.Running;
                            GameTitleTB.Text = gameTitle;
                            gogPanel.ProgressValue = 0;
                            break;
                        case StandardErrorCommandEvent stdErr:
                            if (stdErr.Text.Contains("Verification") || stdErr.Text.Contains("Verifying"))
                            {
                                DescriptionTB.Text = ResourceProvider.GetString(LOC.GogOssVerifying);
                            }
                            var progressMatch = Regex.Match(stdErr.Text, @"Progress: (\d+\.\d+)");
                            if (progressMatch.Length >= 2)
                            {
                                if (downloadProperties.downloadAction != DownloadAction.Update)
                                {
                                    DescriptionTB.Text = ResourceProvider.GetString(LOC.GogOss3P_PlayniteDownloadingLabel);
                                }
                                else
                                {
                                    DescriptionTB.Text = ResourceProvider.GetString(LOC.GogOssDownloadingUpdate);
                                }
                                double progress = CommonHelpers.ToDouble(progressMatch.Groups[1].Value);
                                wantedItem.progress = progress;
                                gogPanel.ProgressValue = progress;
                            }
                            var elapsedMatch = Regex.Match(stdErr.Text, @"Running for: (\d\d:\d\d:\d\d)");
                            if (elapsedMatch.Length >= 2)
                            {
                                ElapsedTB.Text = elapsedMatch.Groups[1].Value;
                            }
                            var ETAMatch = Regex.Match(stdErr.Text, @"ETA: (\d\d:\d\d:\d\d)");
                            if (ETAMatch.Length >= 2)
                            {
                                EtaTB.Text = ETAMatch.Groups[1].Value;
                            }
                            var downloadedMatch = Regex.Match(stdErr.Text, @"Downloaded: (\S+) (\wiB)");
                            if (downloadedMatch.Length >= 2)
                            {
                                double downloadedNumber = CommonHelpers.ToBytes(CommonHelpers.ToDouble(downloadedMatch.Groups[1].Value), downloadedMatch.Groups[2].Value);
                                double totalDownloadedNumber = downloadedNumber + downloadCache;
                                wantedItem.downloadedNumber = totalDownloadedNumber;
                                //double newProgress = totalDownloadedNumber / wantedItem.downloadSizeNumber * 100;
                                //wantedItem.progress = newProgress;
                                //gogPanel.ProgressValue = newProgress;

                                if (totalDownloadedNumber == wantedItem.downloadSizeNumber)
                                {
                                    switch (downloadProperties.downloadAction)
                                    {
                                        case DownloadAction.Install:
                                            DescriptionTB.Text = ResourceProvider.GetString(LOC.GogOssFinishingInstallation);
                                            break;
                                        case DownloadAction.Update:
                                            DescriptionTB.Text = ResourceProvider.GetString(LOC.GogOssFinishingUpdate);
                                            break;
                                        case DownloadAction.Repair:
                                            DescriptionTB.Text = ResourceProvider.GetString(LOC.GogOssFinishingRepair);
                                            break;
                                        default:
                                            break;
                                    }
                                }
                            }
                            var downloadSpeedMatch = Regex.Match(stdErr.Text, @"Download\t- (\S+) (\wiB)");
                            if (downloadSpeedMatch.Length >= 2)
                            {
                                string downloadSpeed = CommonHelpers.FormatSize(CommonHelpers.ToDouble(downloadSpeedMatch.Groups[1].Value), downloadSpeedMatch.Groups[2].Value, downloadSpeedInBits);
                                DownloadSpeedTB.Text = downloadSpeed + "/s";
                            }
                            var diskSpeedMatch = Regex.Match(stdErr.Text, @"Disk\t- (\S+) (\wiB)");
                            if (diskSpeedMatch.Length >= 2)
                            {
                                string diskSpeed = CommonHelpers.FormatSize(CommonHelpers.ToDouble(diskSpeedMatch.Groups[1].Value), diskSpeedMatch.Groups[2].Value, downloadSpeedInBits);
                                DiskSpeedTB.Text = diskSpeed + "/s";
                            }
                            var errorMessage = stdErr.Text;
                            if (errorMessage.Contains("Progress: 100"))
                            {
                                successDisplayed = true;
                            }
                            else if (errorMessage.Contains("WARNING") && !errorMessage.Contains("exit requested") && !errorMessage.Contains("PermissionError"))
                            {
                                logger.Warn($"{errorMessage}");
                            }
                            else if (errorMessage.Contains("ERROR") || errorMessage.Contains("CRITICAL") || errorMessage.Contains("Error") || errorMessage.Contains("Failure"))
                            {
                                logger.Error($"{errorMessage}");
                                if (errorMessage.Contains("Failed to establish a new connection")
                                    || errorMessage.Contains("Log in failed")
                                    || errorMessage.Contains("Login failed")
                                    || errorMessage.Contains("No saved credentials"))
                                {
                                    loginErrorDisplayed = true;
                                }
                                else if (errorMessage.Contains("PermissionError"))
                                {
                                    permissionErrorDisplayed = true;
                                }
                                else if (errorMessage.Contains("Not enough available disk space"))
                                {
                                    diskSpaceErrorDisplayed = true;
                                }
                                if (!errorMessage.Contains("old manifest"))
                                {
                                    errorDisplayed = true;
                                    if (errorMessage.Contains("multiprocessing.queues.Queue"))
                                    {
                                        var installDirSize = FileSystem.GetDirectorySize(taskData.fullInstallPath, false);
                                        if (installDirSize == taskData.installSizeNumber && downloadProperties.downloadAction != DownloadAction.Repair)
                                        {
                                            taskData.downloadedNumber = taskData.downloadSizeNumber;
                                        }
                                        errorDisplayed = false;
                                    }
                                }
                            }
                            break;
                        case ExitedCommandEvent exited:
                            if ((!successDisplayed && errorDisplayed) || exited.ExitCode != 0)
                            {
                                if (loginErrorDisplayed)
                                {
                                    playniteAPI.Dialogs.ShowErrorMessage(ResourceProvider.GetString(LOC.GogOss3P_PlayniteGameInstallError).Format(ResourceProvider.GetString(LOC.GogOss3P_PlayniteLoginRequired)));
                                }
                                else if (permissionErrorDisplayed)
                                {
                                    playniteAPI.Dialogs.ShowErrorMessage(string.Format(ResourceProvider.GetString(LOC.GogOss3P_PlayniteGameInstallError), ResourceProvider.GetString(LOC.GogOssPermissionError)));
                                }
                                else if (diskSpaceErrorDisplayed)
                                {
                                    playniteAPI.Dialogs.ShowErrorMessage(string.Format(ResourceProvider.GetString(LOC.GogOss3P_PlayniteGameInstallError), ResourceProvider.GetString(LOC.GogOssNotEnoughSpace)));
                                }
                                else
                                {
                                    playniteAPI.Dialogs.ShowErrorMessage(string.Format(ResourceProvider.GetString(LOC.GogOss3P_PlayniteGameInstallError), ResourceProvider.GetString(LOC.GogOssCheckLog)));
                                }
                                wantedItem.status = DownloadStatus.Paused;
                            }
                            else
                            {
                                var installedAppList = GogOssLibrary.GetInstalledAppList();
                                var installedGameInfo = new Installed
                                {
                                    build_id = downloadProperties.buildId,
                                    version = downloadProperties.version,
                                    title = gameTitle,
                                    platform = downloadProperties.os,
                                    install_path = taskData.fullInstallPath,
                                    language = downloadProperties.language,
                                    installed_DLCs = downloadProperties.extraContent
                                };
                                if (installedAppList.ContainsKey(gameID))
                                {
                                    installedAppList.Remove(gameID);
                                }

                                if (taskData.downloadItemType != DownloadItemType.Dependency)
                                {
                                    var gameMetaManifest = Gogdl.GetGameMetaManifest(gameID);
                                    var dependencies = installedGameInfo.Dependencies;
                                    if (taskData.depends.Count > 0)
                                    {
                                        foreach (var depend in taskData.depends)
                                        {
                                            dependencies.Add(depend);
                                        }
                                    }
                                    Game game = new Game();
                                    {
                                        game = playniteAPI.Database.Games.FirstOrDefault(item => item.PluginId == GogOssLibrary.Instance.Id
                                                                                                 && item.GameId == gameID);
                                        game.InstallDirectory = installedGameInfo.install_path;
                                        game.Version = installedGameInfo.version;
                                        game.IsInstalled = true;
                                        ObservableCollection<GameAction> gameActions = new ObservableCollection<GameAction>(GogOssLibrary.GetOtherTasks(game.GameId, game.InstallDirectory));
                                        game.GameActions = gameActions;
                                        playniteAPI.Database.Games.Update(game);
                                    }
                                    installedAppList.Add(gameID, installedGameInfo);
                                    var heroicInstalledPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "heroic", "gog_store", "installed.json");
                                    if (File.Exists(heroicInstalledPath))
                                    {
                                        var heroicInstalledContent = FileSystem.ReadFileAsStringSafe(heroicInstalledPath);
                                        if (!heroicInstalledContent.IsNullOrWhiteSpace())
                                        {
                                            var heroicInstallInfo = new HeroicInstalled.HeroicInstalledSingle
                                            {
                                                appName = gameID,
                                                build_id = installedGameInfo.build_id,
                                                title = installedGameInfo.title,
                                                version = installedGameInfo.version,
                                                platform = installedGameInfo.platform,
                                                install_path = installedGameInfo.install_path,
                                                language = installedGameInfo.language,
                                                installed_DLCs = installedGameInfo.installed_DLCs,
                                                install_size = CommonHelpers.FormatSize(taskData.installSizeNumber)
                                            };
                                            var heroicInstalledJson = Serialization.FromJson<HeroicInstalled>(heroicInstalledContent);
                                            var wantedHeroicItem = heroicInstalledJson.installed.FirstOrDefault(i => i.appName == taskData.gameID);
                                            if (wantedHeroicItem != null)
                                            {
                                                heroicInstalledJson.installed.Remove(wantedHeroicItem);
                                            }
                                            heroicInstalledJson.installed.Add(heroicInstallInfo);
                                            var strConf = Serialization.ToJson(heroicInstalledJson, true);
                                            File.WriteAllText(heroicInstalledPath, strConf);
                                        }
                                    }
                                }

                                GogOssLibrary.Instance.installedAppListModified = true;

                                wantedItem.status = DownloadStatus.Completed;
                                wantedItem.progress = 100.0;
                                DateTimeOffset now = DateTime.UtcNow;
                                wantedItem.completedTime = now.ToUnixTimeSeconds();
                                if (settings.DisplayDownloadTaskFinishedNotifications)
                                {
                                    var notificationMessage = LOC.GogOssInstallationFinished;
                                    switch (downloadProperties.downloadAction)
                                    {
                                        case DownloadAction.Repair:
                                            notificationMessage = LOC.GogOssRepairFinished;
                                            break;
                                        case DownloadAction.Update:
                                            notificationMessage = LOC.GogOssUpdateFinished;
                                            break;
                                        default:
                                            break;
                                    }
                                    var bitmap = new System.Drawing.Bitmap(GogOss.Icon);
                                    var iconHandle = bitmap.GetHicon();
                                    Playnite.WindowsNotifyIconManager.Notify(System.Drawing.Icon.FromHandle(iconHandle), gameTitle, ResourceProvider.GetString(notificationMessage), null);
                                    bitmap.Dispose();
                                }
                            }
                            gracefulInstallerCTS?.Dispose();
                            forcefulInstallerCTS?.Dispose();
                            break;
                        default:
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Command was canceled
            }
            finally
            {
                downloadsChanged = true;
                await DoNextJobInQueue();
            }
        }

        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadsDG.SelectedIndex != -1)
            {
                var runningOrQueuedDownloads = DownloadsDG.SelectedItems.Cast<DownloadManagerData.Download>().Where(i => i.status == DownloadStatus.Running || i.status == DownloadStatus.Queued).ToList();
                if (runningOrQueuedDownloads.Count > 0)
                {
                    foreach (var selectedRow in runningOrQueuedDownloads)
                    {
                        if (selectedRow.status == DownloadStatus.Running)
                        {
                            gracefulInstallerCTS?.Cancel();
                            gracefulInstallerCTS?.Dispose();
                            forcefulInstallerCTS?.Dispose();
                        }
                        selectedRow.status = DownloadStatus.Paused;
                    }
                }
            }
        }

        private async void ResumeDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadsDG.SelectedIndex != -1)
            {
                var downloadsToResume = DownloadsDG.SelectedItems.Cast<DownloadManagerData.Download>()
                                                                 .Where(i => i.status == DownloadStatus.Canceled || i.status == DownloadStatus.Paused)
                                                                 .ToList();
                await EnqueueMultipleJobs(downloadsToResume, true);
            }
        }

        private void CancelDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadsDG.SelectedIndex != -1)
            {
                var cancelableDownloads = DownloadsDG.SelectedItems.Cast<DownloadManagerData.Download>()
                                                                   .Where(i => i.status == DownloadStatus.Running || i.status == DownloadStatus.Queued || i.status == DownloadStatus.Paused)
                                                                   .ToList();
                if (cancelableDownloads.Count > 0)
                {
                    foreach (var selectedRow in cancelableDownloads)
                    {
                        if (selectedRow.status == DownloadStatus.Running)
                        {
                            // Gogdl can't properly gracefully cancel, so we need to force it :-)
                            forcefulInstallerCTS?.Cancel();
                            gracefulInstallerCTS?.Dispose();
                            forcefulInstallerCTS?.Dispose();
                        }
                        if (selectedRow.fullInstallPath != null && selectedRow.downloadProperties.downloadAction == DownloadAction.Install)
                        {
                            if (Directory.Exists(selectedRow.fullInstallPath))
                            {
                                Directory.Delete(selectedRow.fullInstallPath, true);
                            }
                        }
                        selectedRow.status = DownloadStatus.Canceled;
                        selectedRow.downloadedNumber = 0;
                        selectedRow.progress = 0;
                    }
                    DownloadSpeedTB.Text = "";
                    gogPanel.ProgressValue = 0;
                    ElapsedTB.Text = "";
                    EtaTB.Text = "";
                    DescriptionTB.Text = "";
                    GameTitleTB.Text = "";
                    DiskSpeedTB.Text = "";
                }
            }
        }

        private void RemoveDownloadEntry(DownloadManagerData.Download selectedEntry)
        {
            if (selectedEntry.status != DownloadStatus.Completed && selectedEntry.status != DownloadStatus.Canceled)
            {
                if (selectedEntry.status == DownloadStatus.Running)
                {
                    gracefulInstallerCTS?.Cancel();
                    gracefulInstallerCTS?.Dispose();
                    forcefulInstallerCTS?.Dispose();
                }
                selectedEntry.status = DownloadStatus.Canceled;
            }
            if (selectedEntry.fullInstallPath != null && selectedEntry.status != DownloadStatus.Completed
                && selectedEntry.downloadProperties.downloadAction == DownloadAction.Install)
            {
                if (Directory.Exists(selectedEntry.fullInstallPath))
                {
                    Directory.Delete(selectedEntry.fullInstallPath, true);
                }
            }
            downloadManagerData.downloads.Remove(selectedEntry);
        }

        private void RemoveDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadsDG.SelectedIndex != -1)
            {
                string messageText;
                if (DownloadsDG.SelectedItems.Count == 1)
                {
                    var selectedRow = (DownloadManagerData.Download)DownloadsDG.SelectedItem;
                    messageText = string.Format(ResourceProvider.GetString(LOC.GogOssRemoveEntryConfirm), selectedRow.name);
                }
                else
                {
                    messageText = ResourceProvider.GetString(LOC.GogOssRemoveSelectedEntriesConfirm);
                }
                var result = playniteAPI.Dialogs.ShowMessage(messageText, ResourceProvider.GetString(LOC.GogOssRemoveEntry), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    foreach (var selectedRow in DownloadsDG.SelectedItems.Cast<DownloadManagerData.Download>().ToList())
                    {
                        RemoveDownloadEntry(selectedRow);
                    }
                }
            }
        }

        private void RemoveCompletedDownloadsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadsDG.Items.Count > 0)
            {
                var result = playniteAPI.Dialogs.ShowMessage(ResourceProvider.GetString(LOC.GogOssRemoveCompletedDownloadsConfirm), ResourceProvider.GetString(LOC.GogOssRemoveCompletedDownloads), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    foreach (var row in DownloadsDG.Items.Cast<DownloadManagerData.Download>().ToList())
                    {
                        if (row.status == DownloadStatus.Completed)
                        {
                            RemoveDownloadEntry(row);
                        }
                    }
                }
            }
        }

        private void FilterDownloadBtn_Checked(object sender, RoutedEventArgs e)
        {
            FilterPop.IsOpen = true;
        }

        private void FilterDownloadBtn_Unchecked(object sender, RoutedEventArgs e)
        {
            FilterPop.IsOpen = false;
        }

        private void DownloadFiltersChk_IsCheckedChanged(object sender, RoutedEventArgs e)
        {
            ICollectionView downloadsView = CollectionViewSource.GetDefaultView(downloadManagerData.downloads);
            var checkedStatus = new List<DownloadStatus>();
            foreach (CheckBox checkBox in FilterStatusSP.Children)
            {
                var downloadStatus = (DownloadStatus)Enum.Parse(typeof(DownloadStatus), checkBox.Name.Replace("Chk", ""));
                if (checkBox.IsChecked == true)
                {
                    checkedStatus.Add(downloadStatus);
                }
                else
                {
                    checkedStatus.Remove(downloadStatus);
                }
            }
            if (checkedStatus.Count > 0)
            {
                downloadsView.Filter = item => checkedStatus.Contains((item as DownloadManagerData.Download).status);
                FilterDownloadBtn.Content = "\uef29 " + ResourceProvider.GetString(LOC.GogOss3P_PlayniteFilterActiveLabel);
            }
            else
            {
                downloadsView.Filter = null;
                FilterDownloadBtn.Content = "\uef29";
            }
        }

        private void DownloadPropertiesBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadsDG.SelectedIndex != -1)
            {
                var window = playniteAPI.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMaximizeButton = false,
                });
                var selectedItem = DownloadsDG.SelectedItems[0] as DownloadManagerData.Download;
                window.Title = selectedItem.name + " — " + ResourceProvider.GetString(LOC.GogOssDownloadProperties);
                window.DataContext = selectedItem;
                window.Content = new GogOssDownloadPropertiesView();
                window.Owner = playniteAPI.Dialogs.GetCurrentAppWindow();
                window.SizeToContent = SizeToContent.WidthAndHeight;
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.ShowDialog();
            }
        }

        private void DownloadsDG_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DownloadsDG.SelectedIndex != -1)
            {
                ResumeDownloadBtn.IsEnabled = true;
                PauseBtn.IsEnabled = true;
                CancelDownloadBtn.IsEnabled = true;
                RemoveDownloadBtn.IsEnabled = true;
                MoveBottomBtn.IsEnabled = true;
                MoveDownBtn.IsEnabled = true;
                MoveTopBtn.IsEnabled = true;
                MoveUpBtn.IsEnabled = true;
                if (DownloadsDG.SelectedItems.Count == 1)
                {
                    DownloadPropertiesBtn.IsEnabled = true;
                    OpenDownloadDirectoryBtn.IsEnabled = true;
                }
                else
                {
                    DownloadPropertiesBtn.IsEnabled = false;
                    OpenDownloadDirectoryBtn.IsEnabled = false;
                }
            }
            else
            {
                ResumeDownloadBtn.IsEnabled = false;
                PauseBtn.IsEnabled = false;
                CancelDownloadBtn.IsEnabled = false;
                RemoveDownloadBtn.IsEnabled = false;
                DownloadPropertiesBtn.IsEnabled = false;
                OpenDownloadDirectoryBtn.IsEnabled = false;
                MoveBottomBtn.IsEnabled = false;
                MoveDownBtn.IsEnabled = false;
                MoveTopBtn.IsEnabled = false;
                MoveUpBtn.IsEnabled = false;
            }
        }

        private void SelectAllBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadsDG.Items.Count > 0)
            {
                DownloadsDG.SelectAll();
            }
        }

        private void OpenDownloadDirectoryBtn_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = DownloadsDG.SelectedItems[0] as DownloadManagerData.Download;
            var fullInstallPath = selectedItem.fullInstallPath;
            if (fullInstallPath != "" && Directory.Exists(fullInstallPath))
            {
                ProcessStarter.StartProcess("explorer.exe", selectedItem.fullInstallPath);
            }
            else
            {
                playniteAPI.Dialogs.ShowErrorMessage($"{selectedItem.fullInstallPath}\n{ResourceProvider.GetString(LOC.GogOssPathNotExistsError)}");
            }
        }

        private enum EntryPosition
        {
            Up,
            Down,
            Top,
            Bottom
        }

        private void MoveEntries(EntryPosition entryPosition, bool moveFocus = false)
        {
            if (DownloadsDG.SelectedIndex != -1)
            {
                var selectedIndexes = new List<int>();
                var allItems = DownloadsDG.Items;
                foreach (var selectedRow in DownloadsDG.SelectedItems.Cast<DownloadManagerData.Download>().ToList())
                {
                    var selectedIndex = allItems.IndexOf(selectedRow);
                    selectedIndexes.Add(selectedIndex);
                }
                selectedIndexes.Sort();
                if (entryPosition == EntryPosition.Down || entryPosition == EntryPosition.Top)
                {
                    selectedIndexes.Reverse();
                }
                var lastIndex = downloadManagerData.downloads.Count - 1;
                int loopIndex = 0;
                foreach (int selectedIndex in selectedIndexes)
                {
                    int newIndex = selectedIndex;
                    int newSelectedIndex = selectedIndex;
                    switch (entryPosition)
                    {
                        case EntryPosition.Up:
                            if (selectedIndex != 0)
                            {
                                newIndex = selectedIndex - 1;
                            }
                            else
                            {
                                return;
                            }
                            break;
                        case EntryPosition.Down:
                            if (selectedIndex != lastIndex)
                            {
                                newIndex = selectedIndex + 1;
                            }
                            else
                            {
                                return;
                            }
                            break;
                        case EntryPosition.Top:
                            newSelectedIndex += loopIndex;
                            newIndex = 0;
                            break;
                        case EntryPosition.Bottom:
                            newIndex = lastIndex;
                            newSelectedIndex -= loopIndex;
                            break;
                    }
                    downloadManagerData.downloads.Move(newSelectedIndex, newIndex);
                    loopIndex++;
                }
                if (moveFocus)
                {
                    DownloadsDG.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                }
            }
        }

        private void MoveUpBtn_Click(object sender, RoutedEventArgs e)
        {
            MoveEntries(EntryPosition.Up);
        }
        private void MoveTopBtn_Click(object sender, RoutedEventArgs e)
        {
            MoveEntries(EntryPosition.Top);
        }

        private void MoveDownBtn_Click(object sender, RoutedEventArgs e)
        {
            MoveEntries(EntryPosition.Down);
        }

        private void MoveBottomBtn_Click(object sender, RoutedEventArgs e)
        {
            MoveEntries(EntryPosition.Bottom);
        }

        private void GogOssDownloadManagerUC_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveDownloadBtn_Click(sender, e);
            }
            else if (Keyboard.IsKeyDown(Key.LeftAlt) && Keyboard.IsKeyDown(Key.Home))
            {
                MoveEntries(EntryPosition.Top, true);
            }
            else if (Keyboard.IsKeyDown(Key.LeftAlt) && Keyboard.IsKeyDown(Key.Up))
            {
                MoveEntries(EntryPosition.Up, true);
            }
            else if (Keyboard.IsKeyDown(Key.LeftAlt) && Keyboard.IsKeyDown(Key.Down))
            {
                MoveEntries(EntryPosition.Down, true);
            }
            else if (Keyboard.IsKeyDown(Key.LeftAlt) && Keyboard.IsKeyDown(Key.End))
            {
                MoveEntries(EntryPosition.Bottom, true);
            }
            else if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && e.Key == Key.P)
            {
                DownloadPropertiesBtn_Click(sender, e);
            }
            else if ((Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) && e.Key == Key.O)
            {
                OpenDownloadDirectoryBtn_Click(sender, e);
            }
        }

        private void GogOssDownloadManagerUC_Loaded(object sender, RoutedEventArgs e)
        {
            CommonHelpers.SetControlBackground(this);
        }
    }
}
