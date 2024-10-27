using CliWrap;
using CliWrap.EventStream;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using System.Net.Http;
using GogOssLibraryNS.Services;
using Downloader;
using System.Net;
using SharpCompress.Compressors.Deflate;
using SharpCompress.IO;
using SharpCompress.Compressors;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Interaction logic for GogOssDownloadManagerView.xaml
    /// </summary>
    public partial class GogOssDownloadManagerView : UserControl
    {
        public CancellationTokenSource gracefulInstallerCTS;
        private ILogger logger = LogManager.GetLogger();
        private IPlayniteAPI playniteAPI = API.Instance;
        public DownloadManagerData.Rootobject downloadManagerData;
        public SidebarItem gogPanel = GogOssLibrary.GetPanel();
        public bool downloadsChanged = false;
        private static readonly HttpClient httpClient = new HttpClient();
        private GogAccountClient gogAccountClient = new GogAccountClient();
        private Stopwatch stopWatch;

        public GogOssDownloadManagerView()
        {
            InitializeComponent();
            SetControlTextBlockStyle();

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
            var runningAndQueuedDownloads = downloadManagerData.downloads.Where(i => i.status == Enums.DownloadStatus.Running
                                                                                     || i.status == Enums.DownloadStatus.Queued).ToList();
            if (runningAndQueuedDownloads.Count > 0)
            {
                foreach (var download in runningAndQueuedDownloads)
                {
                    download.status = Enums.DownloadStatus.Paused;
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
                playniteAPI.MainView.SwitchToLibraryView();
            });
        }

        public string GetToolTipWithKey(string description, string shortcut)
        {
            return $"{ResourceProvider.GetString(description)} [{shortcut}]";
        }

        public DownloadManagerData.Rootobject LoadSavedData()
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
                downloadManagerData = new DownloadManagerData.Rootobject
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
                Helpers.SaveJsonSettingsToFile(downloadManagerData, "downloadManager");
            }
        }

        public async Task DoNextJobInQueue()
        {
            var running = downloadManagerData.downloads.Any(item => item.status == Enums.DownloadStatus.Running);
            var queuedList = downloadManagerData.downloads.Where(i => i.status == Enums.DownloadStatus.Queued).ToList();
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
                    downloadJob.status = Enums.DownloadStatus.Queued;
                    downloadJob.addedTime = now.ToUnixTimeSeconds();
                    downloadManagerData.downloads.Add(downloadJob);
                }
                else
                {
                    wantedItem.status = Enums.DownloadStatus.Queued;
                }
            }
            await DoNextJobInQueue();
        }

        public string GetGalaxyPath(string manifestHash)
        {
            var galaxyPath = manifestHash;
            if (galaxyPath.IndexOf("/") == -1)
            {
                galaxyPath = manifestHash.Substring(0, 2) + "/" + manifestHash.Substring(2, 2) + "/" + galaxyPath;
            }
            return galaxyPath;
        }

        public async Task<List<string>> GetSecureLinks(DownloadManagerData.Download taskData, string path = "/")
        {
            List<string> directUrls = new List<string>();
            List<string> urls = new List<string>();
            var url = "";
            var metaManifest = await GogOss.GetGameMetaManifest(taskData);
            if (metaManifest.version == 2)
            {
                url = $"https://content-system.gog.com/products/{taskData.gameID}/secure_link?generation=2&_version=2&path={path}";
            }
            else
            {
                url = $"https://content-system.gog.com/products/{taskData.gameID}/secure_link?_version=2&type=depot%path={path}";
            }
            if (await gogAccountClient.GetIsUserLoggedIn())
            {
                var tokens = gogAccountClient.LoadTokens();
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("User-Agent", GogOss.UserAgent);
                httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + tokens.access_token);
                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (!content.IsNullOrWhiteSpace())
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
                                logger.Debug(newUrl);
                                urls.Add(newUrl);
                            }
                        }
                    }
                }
            }
            else
            {
                playniteAPI.Dialogs.ShowErrorMessage(playniteAPI.Resources.GetString(LOC.GogOss3P_GOGNotLoggedInError), "");
                logger.Error($"Can't get secure links, cuz user is not authenticated.");
            }
            //if (urls.Count > 0)
            //{
            //    foreach (var depot in metaManifest.depots)
            //    {
            //        var chosenLanguage = taskData.downloadProperties.language;
            //        if (chosenLanguage.IsNullOrEmpty())
            //        {
            //            chosenLanguage = metaManifest.languages.First();
            //        }
            //        if (depot.languages.Contains(chosenLanguage))
            //        {
            //            var productIds = new List<string>
            //            {
            //                taskData.gameID
            //            };
            //            if (taskData.downloadProperties.extraContent.Count > 0)
            //            {
            //                foreach (var dlc in taskData.downloadProperties.extraContent)
            //                {
            //                    productIds.Add(dlc);
            //                }
            //            }
            //            if (productIds.Contains(depot.productId))
            //            {
            //                var manifestHash = depot.manifest;
            //                foreach (var newUrl in urls)
            //                {
            //                    var formatedUrl = newUrl.Replace("{GALAXY_PATH}", GetGalaxyPath(manifestHash));
            //                    directUrls.Add(formatedUrl);
            //                }
            //            }
            //        }
            //    }
            //}
            return urls;
        }

        private async Task<GogDepot> GetDepotInfo(string manifest, int version = 2)
        {
            var depotManifest = new GogDepot();
            var logger = LogManager.GetLogger();
            var cacheInfoPath = GogOssLibrary.Instance.GetCachePath("depotcache");
            var cacheInfoFileName = $"{manifest}.json";
            var cacheInfoFile = Path.Combine(cacheInfoPath, cacheInfoFileName);
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
                var content = FileSystem.ReadFileAsStringSafe(cacheInfoFile);
                if (!content.IsNullOrWhiteSpace() && Serialization.TryFromJson<GogDepot>(content, out var newManifest))
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
                if (!Directory.Exists(cacheInfoPath))
                {
                    Directory.CreateDirectory(cacheInfoPath);
                }
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("User-Agent", GogOss.UserAgent);
                var response = await httpClient.GetAsync($"https://gog-cdn-fastly.gog.com/content-system/v2/meta/{GetGalaxyPath(manifest)}");
                Stream content;
                if (response.IsSuccessStatusCode)
                {
                    content = await response.Content.ReadAsStreamAsync();
                }
                else
                {
                    logger.Error($"An error occurred while downloading depot manifest {manifest}.");
                    return depotManifest;
                }
                if (!Directory.Exists(cacheInfoPath))
                {
                    Directory.CreateDirectory(cacheInfoPath);
                }
                var result = Helpers.DecompressZlib(content);
                File.WriteAllText(cacheInfoFile, result);
                depotManifest = Serialization.FromJson<GogDepot>(result);
            }
            return depotManifest;
        }

        public async Task<List<string>> GetNeededDepotManifestHashes(DownloadManagerData.Download taskData)
        {
            List<string> depotHashes = new List<string>();
            var metaManifest = await GogOss.GetGameMetaManifest(taskData);
            foreach (var depot in metaManifest.depots)
            {
                var chosenlanguage = taskData.downloadProperties.language;
                if (chosenlanguage.IsNullOrEmpty())
                {
                    chosenlanguage = metaManifest.languages.First();
                }
                if (depot.languages.Contains(chosenlanguage))
                {
                    var productids = new List<string>
                    {
                        taskData.gameID
                    };
                    if (taskData.downloadProperties.extraContent.Count > 0)
                    {
                        foreach (var dlc in taskData.downloadProperties.extraContent)
                        {
                            productids.Add(dlc);
                        }
                    }
                    if (productids.Contains(depot.productId))
                    {
                        var manifestHash = depot.manifest;
                        depotHashes.Add(manifestHash);
                    }
                }
            }
            return depotHashes;
        }

        public async Task Install(DownloadManagerData.Download taskData)
        {
            var installCommand = new List<string>();
            var settings = GogOssLibrary.GetSettings();
            var gameID = taskData.gameID;
            var downloadProperties = taskData.downloadProperties;
            var gameTitle = taskData.name;
            double cachedDownloadSizeNumber = taskData.downloadSizeNumber;
            //double downloadCache = 0;
            bool downloadSpeedInBits = false;
            if (settings.DisplayDownloadSpeedInBits)
            {
                downloadSpeedInBits = true;
            }


            List<string> depotHashes = await GetNeededDepotManifestHashes(taskData);
            List<GogDepot.Item> depotItems = new List<GogDepot.Item>();
            List<string> chunksToDownload = new List<string>();

            foreach (var depotHash in depotHashes)
            {
                var depotManifest = await GetDepotInfo(depotHash);
                if (depotManifest.depot.items.Count > 0)
                {
                    foreach (var depotItem in depotManifest.depot.items)
                    {
                        depotItems.Add(depotItem);
                    }
                }
            }

            foreach (var depotItem in depotItems)
            {
                foreach (var chunk in depotItem.chunks)
                {
                    chunksToDownload.Add(chunk.compressedMd5);
                }
            }

            DirectoryInfo installPath = new DirectoryInfo(Path.Combine(taskData.fullInstallPath, ".chunks"));

            //List<string> finalLinks = new List<string>();
            //var secureLinks = await GetSecureLinks(taskData);
            //foreach (var secureLink in secureLinks)
            //{
            //    var newLink = secureLink.Replace("{GALAXY_PATH}", GetGalaxyPath(chunksToDownload[0]));
            //    finalLinks.Add(newLink);
            //}

            List<Dictionary<string, List<string>>> finalLinksList = new List<Dictionary<string, List<string>>>();
            var secureLinks = await GetSecureLinks(taskData);
            foreach (var chunk in chunksToDownload)
            {
                Dictionary<string, List<string>> finalLinks = new Dictionary<string, List<string>>();
                foreach (var secureLink in secureLinks)
                {
                    var newLink = secureLink.Replace("{GALAXY_PATH}", GetGalaxyPath(chunk));
                    if (!finalLinks.ContainsKey(chunk))
                    {
                        finalLinks.Add(chunk, new List<string>());
                        finalLinks[chunk].Add(newLink);
                    }
                }
                finalLinksList.Add(finalLinks);
            }

            var wantedItem = downloadManagerData.downloads.FirstOrDefault(item => item.gameID == taskData.gameID);
            stopWatch = new Stopwatch();
            gracefulInstallerCTS = new CancellationTokenSource();

            var downloadOpt = new DownloadConfiguration()
            {
                // usually, hosts support max to 8000 bytes, default value is 8000
                BufferBlockSize = 10000,
                // file parts to download, the default value is 1
                ChunkCount = 10,
                // download speed limited to 2MB/s, default values is zero or unlimited
                //MaximumBytesPerSecond = 1024 * 1024 * 2,
                // the maximum number of times to fail
                MaxTryAgainOnFailover = 5,
                //// release memory buffer after each 50 MB
                MaximumMemoryBufferBytes = 1024 * 1024 * 50,
                //// download parts of the file as parallel or not. The default value is false
                //ParallelDownload = true,
                //// number of parallel downloads. The default value is the same as the chunk count
                //ParallelCount = 4,
                // timeout (millisecond) per stream block reader, default values is 1000
                Timeout = 1000,
                // clear package chunks data when download completed with failure, default value is false
                ClearPackageOnCompletionWithFailure = false,
                // minimum size of chunking to download a file in multiple parts, the default value is 512
                MinimumSizeOfChunking = 512,
                // Before starting the download, reserve the storage space of the file as file size, the default value is false
                ReserveStorageSpaceBeforeStartingDownload = true,
                // config and customize request headers
                RequestConfiguration =
                {
                    Accept = "*/*",
                    // your custom user agent or your_app_name/app_version.
                    UserAgent = GogOss.DownloaderUserAgent,
                }
            };

            wantedItem.chunksData = new Dictionary<string, DownloadManagerData.ChunkData>();
            foreach (var chunk in chunksToDownload)
            {
                if (!wantedItem.chunksData.ContainsKey(chunk))
                {
                    wantedItem.chunksData.Add(chunk, new DownloadManagerData.ChunkData());
                }
            }

            stopWatch.Start();
            wantedItem.status = Enums.DownloadStatus.Running;
            GameTitleTB.Text = gameTitle;
            gogPanel.ProgressValue = 0;

            if (downloadProperties.downloadAction != DownloadAction.Update)
            {
                DescriptionTB.Text = ResourceProvider.GetString(LOC.GogOss3P_PlayniteDownloadingLabel);
            }
            else
            {
                DescriptionTB.Text = ResourceProvider.GetString(LOC.GogOssDownloadingUpdate);
            }

            var allTasks = new List<Task>();
            var options = new ParallelOptions { MaxDegreeOfParallelism = Helpers.CpuThreadsNumber };

            var throttler = new SemaphoreSlim(Helpers.CpuThreadsNumber /2, Helpers.CpuThreadsNumber -1);
            foreach (var finalLinks in finalLinksList)
            {
                var chunk = finalLinks.First().Key;
                var finalLinksRealList = finalLinks.First().Value;
                await throttler.WaitAsync();
                allTasks.Add(
                    Task.Run(async () =>
                    {
                        await DownloadChunk(chunk, finalLinksRealList, installPath, downloadOpt, wantedItem, downloadSpeedInBits);
                        await ReportProgress(wantedItem, downloadSpeedInBits);
                        throttler.Release();
                    }));
            }
            await Task.WhenAll(allTasks);
            await Task.Run(() =>
            {
                if (wantedItem.status == Enums.DownloadStatus.Running)
                {
                    if (Directory.Exists(installPath.FullName))
                    {
                        Dispatcher.Invoke(() =>
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
                        });
                        foreach (var depotItem in depotItems)
                        {
                            if (depotItem.chunks.Count > 0)
                            {
                                var finalFile = Path.GetFullPath(Path.Combine(wantedItem.fullInstallPath, depotItem.path));
                                bool notEnoughChunks = false;
                                Stream decompressedData = new MemoryStream();
                                foreach (var chunk in depotItem.chunks)
                                {
                                    var chunkFile = Path.Combine(installPath.FullName, chunk.compressedMd5);
                                    if (File.Exists(chunkFile))
                                    {
                                        var fileBytes = File.ReadAllBytes(chunkFile);
                                        var decompressedStream = Helpers.DecompressZlibToByte(fileBytes);
                                        decompressedData.Write(decompressedStream, 0, decompressedStream.Length);
                                        File.Delete(chunkFile);
                                    }
                                    else
                                    {
                                        notEnoughChunks = true;
                                    }
                                }
                                if (!notEnoughChunks)
                                {
                                    var finalDir = Path.GetDirectoryName(finalFile);
                                    if (!Directory.Exists(finalDir))
                                    {
                                        Directory.CreateDirectory(finalDir);
                                    }
                                    var finalFileStream = File.Create(finalFile);
                                    decompressedData.Seek(0, SeekOrigin.Begin);
                                    decompressedData.CopyTo(finalFileStream);
                                    finalFileStream.Dispose();
                                }
                                else
                                {
                                    logger.Debug($"Not enough chunks downloaded for {depotItem.path} file");
                                }
                            }
                        }
                        Directory.Delete(installPath.FullName);
                    }
                }
            });

            await Dispatcher.Invoke(async () =>
            {
                stopWatch.Stop();
                if (wantedItem.status == Enums.DownloadStatus.Running)
                {
                    ElapsedTB.Text = stopWatch.Elapsed.ToString(@"dd\:hh\:mm\:ss");
                    DateTimeOffset now = DateTime.UtcNow;
                    wantedItem.completedTime = now.ToUnixTimeSeconds();
                    gogPanel.ProgressValue = 100.0;
                    wantedItem.progress = 100.0;
                    wantedItem.status = Enums.DownloadStatus.Completed;
                    wantedItem.chunksData = null;
                    gracefulInstallerCTS?.Dispose();
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
                await DoNextJobInQueue();
            });
        }

        public async Task ReportProgress(DownloadManagerData.Download wantedItem, bool downloadSpeedInBits)
        {
            await Dispatcher.BeginInvoke((Action)(() =>
            {
                double downloadedNumber = 0;
                foreach (var chunk in wantedItem.chunksData)
                {
                    downloadedNumber += chunk.Value.downloadedNumber;
                }
                wantedItem.downloadedNumber = downloadedNumber;
                double newProgress = downloadedNumber / wantedItem.downloadSizeNumber * 100;
                if (newProgress < 99.0)
                {
                    wantedItem.progress = newProgress;
                    gogPanel.ProgressValue = newProgress;
                }
                double downloadSpeedNumber = downloadedNumber / stopWatch.Elapsed.TotalSeconds;
                string downloadSpeed = Helpers.FormatSize(downloadSpeedNumber, "B", downloadSpeedInBits);
                DownloadSpeedTB.Text = downloadSpeed + "/s";
                ElapsedTB.Text = stopWatch.Elapsed.ToString(@"dd\:hh\:mm\:ss");
                TimeSpan totalEstimatedTimeSpan = TimeSpan.FromSeconds((wantedItem.downloadSizeNumber - downloadedNumber) / downloadSpeedNumber);
                EtaTB.Text = totalEstimatedTimeSpan.ToString(@"dd\:hh\:mm\:ss");
            }));
        }

        public async Task DownloadChunk(string chunk, List<string> finalLinks, DirectoryInfo installPath, DownloadConfiguration downloadConfiguration, DownloadManagerData.Download wantedItem, bool downloadSpeedInBits)
        {
            var downloader = new DownloadService(downloadConfiguration);

            downloader.DownloadProgressChanged += (sender, args) =>
            {
                var wantedChunk = chunk;
                wantedItem.chunksData[wantedChunk].downloadedNumber = args.ReceivedBytesSize;
            };

            downloader.DownloadFileCompleted += (sender, args) =>
            {
                if (args.Error != null && !args.Cancelled)
                {
                    logger.Debug(args.Error.Message);
                    wantedItem.status = Enums.DownloadStatus.Paused;
                }
            };
            await downloader.DownloadFileTaskAsync(finalLinks.ToArray(), installPath, gracefulInstallerCTS.Token);
        }

        private void PauseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadsDG.SelectedIndex != -1)
            {
                var runningOrQueuedDownloads = DownloadsDG.SelectedItems.Cast<DownloadManagerData.Download>().Where(i => i.status == Enums.DownloadStatus.Running || i.status == Enums.DownloadStatus.Queued).ToList();
                if (runningOrQueuedDownloads.Count > 0)
                {
                    foreach (var selectedRow in runningOrQueuedDownloads)
                    {
                        if (selectedRow.status == Enums.DownloadStatus.Running)
                        {
                            gracefulInstallerCTS?.Cancel();
                            gracefulInstallerCTS?.Dispose();
                        }
                        selectedRow.status = Enums.DownloadStatus.Paused;
                    }
                }
            }
        }

        private async void ResumeDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadsDG.SelectedIndex != -1)
            {
                var downloadsToResume = DownloadsDG.SelectedItems.Cast<DownloadManagerData.Download>()
                                                                 .Where(i => i.status == Enums.DownloadStatus.Canceled || i.status == Enums.DownloadStatus.Paused)
                                                                 .ToList();
                await EnqueueMultipleJobs(downloadsToResume, true);
            }
        }

        public async Task WaitUntilDownloaderCloses(string fullInstallPath)
        {
            bool locked = Helpers.IsDirectoryLocked(fullInstallPath);
            if (locked)
            {
                await Task.Delay(1000);
                await WaitUntilDownloaderCloses(fullInstallPath);
            }
        }

        private async void CancelDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadsDG.SelectedIndex != -1)
            {
                var cancelableDownloads = DownloadsDG.SelectedItems.Cast<DownloadManagerData.Download>()
                                                                   .Where(i => i.status == Enums.DownloadStatus.Running || i.status == Enums.DownloadStatus.Queued || i.status == Enums.DownloadStatus.Paused)
                                                                   .ToList();
                if (cancelableDownloads.Count > 0)
                {
                    foreach (var selectedRow in cancelableDownloads)
                    {
                        if (selectedRow.status == Enums.DownloadStatus.Running)
                        {
                            gracefulInstallerCTS?.Cancel();
                            gracefulInstallerCTS?.Dispose();
                            await WaitUntilDownloaderCloses(Path.GetFullPath(selectedRow.fullInstallPath));
                        }
                        if (selectedRow.fullInstallPath != null && selectedRow.downloadProperties.downloadAction == DownloadAction.Install)
                        {
                            var mainDir = Path.Combine(selectedRow.fullInstallPath);
                            if (Directory.Exists(mainDir))
                            {
                                try
                                {
                                    if (Directory.Exists(mainDir))
                                    {
                                        Directory.Delete(mainDir, true);
                                    }
                                }
                                catch
                                {
                                    await Task.Delay(2000);
                                    if (Directory.Exists(mainDir))
                                    {
                                        Directory.Delete(mainDir, true);
                                    }
                                }
                            }
                        }
                        selectedRow.status = Enums.DownloadStatus.Canceled;
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
            if (selectedEntry.status != Enums.DownloadStatus.Completed && selectedEntry.status != Enums.DownloadStatus.Canceled)
            {
                if (selectedEntry.status == Enums.DownloadStatus.Running)
                {
                    gracefulInstallerCTS?.Cancel();
                    gracefulInstallerCTS?.Dispose();
                }
                selectedEntry.status = Enums.DownloadStatus.Canceled;
            }
            if (selectedEntry.fullInstallPath != null && selectedEntry.status != Enums.DownloadStatus.Completed
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
                        if (row.status == Enums.DownloadStatus.Completed)
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
            var checkedStatus = new List<Enums.DownloadStatus>();
            foreach (CheckBox checkBox in FilterStatusSP.Children)
            {
                var downloadStatus = (Enums.DownloadStatus)Enum.Parse(typeof(Enums.DownloadStatus), checkBox.Name.Replace("Chk", ""));
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

        private void SetControlTextBlockStyle()
        {
            var baseStyleName = "BaseTextBlockStyle";
            if (playniteAPI.ApplicationInfo.Mode == ApplicationMode.Fullscreen)
            {
                baseStyleName = "TextBlockBaseStyle";
            }

            if (ResourceProvider.GetResource(baseStyleName) is Style baseStyle && baseStyle.TargetType == typeof(TextBlock))
            {
                var implicitStyle = new Style(typeof(TextBlock), baseStyle);
                Resources.Add(typeof(TextBlock), implicitStyle);
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
    }
}
