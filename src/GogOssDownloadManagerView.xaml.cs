using CliWrap;
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
using Linguini.Shared.Types.Bundle;
using GogOssLibraryNS.Services;
using System.Diagnostics;
using System.Net.Http;
using System.Net;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using System.Collections.Concurrent;
using System.Buffers;
using System.Threading.Channels;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Interaction logic for GogOssDownloadManagerView.xaml
    /// </summary>
    public partial class GogOssDownloadManagerView : UserControl
    {
        public CancellationTokenSource forcefulInstallerCTS;
        public CancellationTokenSource gracefulInstallerCTS;
        public CancellationTokenSource userCancelCTS;

        private ILogger logger = LogManager.GetLogger();
        private IPlayniteAPI playniteAPI = API.Instance;
        public DownloadManagerData downloadManagerData;
        public SidebarItem gogPanel = GogOssLibrary.GetPanel();
        public bool downloadsChanged = false;
        public static readonly HttpClientHandler handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
        };
        private static readonly HttpClient client = new HttpClient(handler);
        public GogDownloadApi gogDownloadApi = new GogDownloadApi();
        public IProgress<ProgressData> progress { get; set; }
        private long resumeInitialDiskBytes = 0;
        private long resumeInitialNetworkBytes = 0;

        public GogOssDownloadManagerView()
        {
            InitializeComponent();

            SelectAllBtn.ToolTip = GetToolTipWithKey(LOC.CommonSelectAllEntries, "Ctrl+A");
            RemoveDownloadBtn.ToolTip = GetToolTipWithKey(LOC.CommonRemoveEntry, "Delete");
            MoveTopBtn.ToolTip = GetToolTipWithKey(LOC.CommonMoveEntryTop, "Alt+Home");
            MoveUpBtn.ToolTip = GetToolTipWithKey(LOC.CommonMoveEntryUp, "Alt+Up");
            MoveDownBtn.ToolTip = GetToolTipWithKey(LOC.CommonMoveEntryDown, "Alt+Down");
            MoveBottomBtn.ToolTip = GetToolTipWithKey(LOC.CommonMoveEntryBottom, "Alt+End");
            DownloadPropertiesBtn.ToolTip = GetToolTipWithKey(LOC.CommonEditSelectedDownloadProperties, "Ctrl+P");
            OpenDownloadDirectoryBtn.ToolTip = GetToolTipWithKey(LOC.CommonOpenDownloadDirectory, "Ctrl+O");
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
            return $"{LocalizationManager.Instance.GetString(description)} [{shortcut}]";
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
                var result = MessageCheckBoxDialog.ShowMessage("", LocalizationManager.Instance.GetString(LOC.CommonDownloadManagerWhatsUp), LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteDontShowAgainTitle), MessageBoxButton.OK, MessageBoxImage.Information);
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

        private async Task DownloadFilesAsync(
            CancellationToken token,
            GogDepot.Depot bigDepot,
            string fullInstallPath,
            SecureLinks allSecureLinks,
            int maxParallel = 40,
            int bufferSize = 512 * 1024,
            long maxMemoryBytes = 1L * 1024 * 1024 * 1024,
            int maxRetries = 3)
        {
            // STEP 0: Service and Initial Setup
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, maxParallel * 2);
            var secureLinks = allSecureLinks.mainSecureLinks.secureLinks;
            var dependencyLinks = allSecureLinks.inGameDependsSecureLinks.secureLinks;

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", GogDownloadApi.UserAgent);

            using var downloadSemaphore = new SemaphoreSlim(maxParallel);
            using var sfcExtractSemaphore = new SemaphoreSlim(Math.Min(maxParallel, Environment.ProcessorCount * 2));
            using var memoryLimiter = new MemoryLimiter(maxMemoryBytes);

            const string sfcContainerBaseName = "smallFilesContainer";
            var sfcFilePathsByHash = new ConcurrentDictionary<string, string>();
            var sfcExtractionJobs = new ConcurrentDictionary<string, List<(string filePath, GogDepot.sfcRef sfcRef)>>();

            var sfcFiles = new List<GogDepot.Item>();

            var sfcHashesToDownload = new HashSet<string>();

            var writeSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();
            int channelCapacity = Math.Min(maxParallel * 2, 64);

            // Producer-consumer channel
            var channel = Channel.CreateBounded<(string filePath, long offset, byte[] chunkBuffer, int length, string tempFilePath, long allocatedBytes, bool isRedist, bool isCompressed)>(
                new BoundedChannelOptions(channelCapacity)
                {
                    SingleReader = false,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait
                });

            var jobs = new List<(string filePath, long offset, GogDepot.Chunk chunk, bool isRedist)>();

            long totalSize = 0;
            long totalCompressedSize = 0;
            long initialDiskBytesLocal = 0;
            long initialNetworkBytesLocal = 0;

            var fileExpectedSizes = new ConcurrentDictionary<string, long>();

            var tempDir = Path.Combine(fullInstallPath, ".Downloader_temp");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            //
            // STEP 1.1 / 1.2 / 1.3 - JOB CREATION (UNIFIED LOGIC)
            //

            // --- V1 LOGIC (Primary files for V1 depot)
            if (bigDepot.version == 1 && bigDepot.files.Any())
            {
                foreach (var file in bigDepot.files)
                {
                    var depotFilePath = file.path.TrimStart('/', '\\');
                    var filePath = Path.Combine(fullInstallPath, depotFilePath);
                    bool isRedist = false;

                    if (file.support)
                    {
                        filePath = Path.Combine(fullInstallPath, "gog-support", depotFilePath);
                    }

                    if (filePath.Contains("__redist"))
                    {
                        filePath = Path.Combine(GogOss.DependenciesInstallationPath, file.path);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                    writeSemaphores.TryAdd(filePath, new SemaphoreSlim(1));

                    long expectedFileSize = file.size;
                    long currentFileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;

                    totalSize += expectedFileSize;

                    if (currentFileSize != expectedFileSize)
                    {
                        var v1Chunk = new GogDepot.Chunk
                        {
                            offset = file.offset,
                            size = expectedFileSize,
                            compressedSize = expectedFileSize,
                            compressedMd5 = file.url, // Probably main.bin always?
                        };

                        jobs.Add((filePath, 0, v1Chunk, isRedist));
                        totalCompressedSize += expectedFileSize;
                    }
                    else
                    {
                        initialDiskBytesLocal += expectedFileSize;
                        initialNetworkBytesLocal += expectedFileSize;
                    }

                    fileExpectedSizes.TryAdd(filePath, expectedFileSize);
                }
            }

            // --- V2/DEPENDENCIES LOGIC
            bool shouldDownloadSfc = false;
            if (bigDepot.version == 2 || bigDepot.items.Any())
            {
                //
                // STEP 1.1: SFC Files Separation and Filtering (V2 only)
                //
                var sfcItemsToCheck = bigDepot.items.Where(d => d.sfcRef != null && d.sfcRef.size > 0).ToList();

                foreach (var depotItem in sfcItemsToCheck)
                {
                    string fullSmallFilePath = Path.Combine(fullInstallPath, depotItem.path);
                    string depotHash = depotItem.sfcRef.depotHash;

                    bigDepot.items.Remove(depotItem);
                    sfcFiles.Add(depotItem);

                    if (!File.Exists(fullSmallFilePath) || new FileInfo(fullSmallFilePath).Length != depotItem.sfcRef.size)
                    {
                        sfcHashesToDownload.Add(depotHash);

                        sfcExtractionJobs.GetOrAdd(depotHash, new List<(string filePath, GogDepot.sfcRef sfcRef)>())
                            .Add((fullSmallFilePath, depotItem.sfcRef));

                        Directory.CreateDirectory(Path.GetDirectoryName(fullSmallFilePath)!);
                        writeSemaphores.TryAdd(fullSmallFilePath, new SemaphoreSlim(1));
                    }
                    else
                    {
                        initialDiskBytesLocal += (long)depotItem.sfcRef.size;
                    }
                }

                shouldDownloadSfc = sfcHashesToDownload.Any();


                //
                // STEP 1.2: SFC Container Initialization (V2 only)
                //
                if (shouldDownloadSfc)
                {
                    foreach (var kvp in bigDepot.sfcContainersByHash.Where(kvp => sfcHashesToDownload.Contains(kvp.Key)))
                    {
                        string depotHash = kvp.Key;
                        List<GogDepot.Chunk> chunks = kvp.Value;

                        string sfcFilePath = Path.Combine(tempDir, $"{sfcContainerBaseName}_{depotHash}.bin");
                        sfcFilePathsByHash.TryAdd(depotHash, sfcFilePath);

                        long sfcTotalDecSize = chunks.Sum(c => (long)c.size);

                        long currentSfcSize = File.Exists(sfcFilePath) ? new FileInfo(sfcFilePath).Length : 0;

                        long sfcPos = 0;
                        bool foundFirstIncompleteChunk = false;

                        foreach (var chunk in chunks)
                        {
                            long chunkSize = (long)chunk.size;
                            long compressedSize = (long)chunk.compressedSize;

                            if (foundFirstIncompleteChunk)
                            {
                                chunk.offset = sfcPos;
                                jobs.Add((sfcFilePath, sfcPos, chunk, false));
                                totalCompressedSize += compressedSize;
                            }
                            else if (sfcPos + chunkSize <= currentSfcSize)
                            {
                                initialDiskBytesLocal += chunkSize;
                                initialNetworkBytesLocal += compressedSize;
                            }
                            else
                            {
                                chunk.offset = sfcPos;
                                jobs.Add((sfcFilePath, sfcPos, chunk, false));
                                totalCompressedSize += compressedSize;
                                foundFirstIncompleteChunk = true;
                            }
                            sfcPos += chunkSize;
                        }

                        fileExpectedSizes.TryAdd(sfcFilePath, sfcTotalDecSize);
                        writeSemaphores.TryAdd(sfcFilePath, new SemaphoreSlim(1));
                    }
                }

                //
                // STEP 1.3: Chunk Job Creation for regular V2/Dependency files
                //
                var allDepotItems = bigDepot.items.Concat(sfcFiles).ToList();

                foreach (var depot in allDepotItems)
                {
                    bool isRedist = false;
                    var filePath = Path.Combine(fullInstallPath, depot.path);

                    if (filePath.Contains("__redist"))
                    {
                        filePath = Path.Combine(GogOss.DependenciesInstallationPath, depot.path);
                    }

                    if (depot.redistTargetDir != "")
                    {
                        filePath = Path.Combine(fullInstallPath, depot.path);
                        isRedist = true;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                    if (depot.sfcRef == null || !shouldDownloadSfc)
                    {
                        writeSemaphores.TryAdd(filePath, new SemaphoreSlim(1));
                    }

                    long expectedFileSize = depot.sfcRef != null && shouldDownloadSfc ? (long)depot.sfcRef.size : depot.chunks.Sum(c => (long)c.size);
                    long expectedFileCompressedSize = depot.sfcRef != null && shouldDownloadSfc ? 0 : depot.chunks.Sum(c => (long)c.compressedSize);

                    totalSize += expectedFileSize;

                    if (depot.sfcRef == null || !shouldDownloadSfc)
                    {
                        totalCompressedSize += expectedFileCompressedSize;
                    }

                    fileExpectedSizes.TryAdd(filePath, expectedFileSize);

                    long currentFileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
                    long pos = 0;
                    bool foundFirstIncompleteChunk = false;

                    if (depot.sfcRef == null || !shouldDownloadSfc)
                    {
                        foreach (var chunk in depot.chunks)
                        {
                            long chunkSize = (long)chunk.size;
                            long compressedSize = (long)chunk.compressedSize;

                            if (foundFirstIncompleteChunk)
                            {
                                chunk.offset = pos;
                                jobs.Add((filePath, pos, chunk, isRedist));
                            }
                            else if (pos + chunkSize <= currentFileSize)
                            {
                                initialDiskBytesLocal += chunkSize;
                                initialNetworkBytesLocal += compressedSize;
                            }
                            else
                            {
                                chunk.offset = pos;
                                jobs.Add((filePath, pos, chunk, isRedist));
                                foundFirstIncompleteChunk = true;
                            }
                            pos += chunkSize;
                        }
                    }
                }
            }


            jobs = jobs.OrderBy(job => job.filePath)
                       .ThenBy(job => job.offset)
                       .ToList();

            Interlocked.Exchange(ref resumeInitialDiskBytes, initialDiskBytesLocal);
            Interlocked.Exchange(ref resumeInitialNetworkBytes, initialNetworkBytesLocal);

            long totalNetworkBytes = initialNetworkBytesLocal;
            long totalDiskBytes = initialDiskBytesLocal;

            int activeDownloaders = 0, activeDiskers = 0;
            bool isFinalReport = false;

            ReportProgress();

            async Task RentAndUseAsync(int size, Func<byte[], Task> action)
            {
                var buffer = ArrayPool<byte>.Shared.Rent(size);
                try
                {
                    await action(buffer).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                    catch { }
                }
            }

            void ReportProgress()
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                progress?.Report(new ProgressData
                {
                    TotalBytes = totalSize,
                    TotalCompressedBytes = totalCompressedSize,
                    NetworkBytes = Interlocked.Read(ref totalNetworkBytes),
                    DiskBytes = Interlocked.Read(ref totalDiskBytes),
                    ActiveDownloadWorkers = activeDownloaders,
                    ActiveDiskWorkers = activeDiskers,
                    FinalReport = isFinalReport
                });
            }

            //
            // STEP 2: Producer – Downloader (Fetch and Decompress to Channel)
            //
            var downloadTasks = jobs.Select(job => Task.Run(async () =>
            {
                bool slotAcquired = false;
                long allocatedBytes = 0;
                byte[] chunkBuffer = null;
                string tempFilePath = null;

                try
                {
                    await downloadSemaphore.WaitAsync(token).ConfigureAwait(false);
                    slotAcquired = true;
                    Interlocked.Increment(ref activeDownloaders);
                    ReportProgress();

                    var chunk = job.chunk;
                    long compressedSize = (long)chunk.compressedSize;

                    bool memoryReserved = memoryLimiter.TryReserve(compressedSize);
                    if (memoryReserved)
                    {
                        allocatedBytes = compressedSize;
                    }

                    bool isV1 = bigDepot.version == 1;

                    bool isCompressed = !isV1 || job.isRedist;
                    var currentSecureLinks = job.isRedist ? dependencyLinks : secureLinks;

                    int attempt = 0;
                    int delayMs = 500;

                    while (attempt < maxRetries)
                    {
                        attempt++;
                        try
                        {
                            string url;
                            if (isV1 && !job.isRedist)
                            {
                                url = currentSecureLinks[0].Replace("{GALAXY_PATH}", Path.GetFileName(chunk.compressedMd5));
                            }
                            else
                            {
                                url = currentSecureLinks[0].Replace("{GALAXY_PATH}", gogDownloadApi.GetGalaxyPath(chunk.compressedMd5));
                            }

                            using var request = new HttpRequestMessage(HttpMethod.Get, url);

                            if (isV1)
                            {
                                long start = job.chunk.offset;
                                long end = job.chunk.offset + (long)job.chunk.size - 1;
                                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);
                            }

                            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                            response.EnsureSuccessStatusCode();

                            if (memoryReserved)
                            {
                                chunkBuffer = ArrayPool<byte>.Shared.Rent((int)compressedSize);

                                using var networkStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                                using var progressStream = new ProgressStream.ProgressStream(networkStream,
                                    new Progress<int>(bytesRead =>
                                    {
                                        Interlocked.Add(ref totalNetworkBytes, bytesRead);
                                        ReportProgress();
                                    }), null);
                                int offset = 0;
                                int read;
                                while ((read = await progressStream.ReadAsync(chunkBuffer, offset, (int)compressedSize - offset, token).ConfigureAwait(false)) > 0)
                                {
                                    offset += read;
                                }

                                await channel.Writer.WriteAsync((job.filePath, job.offset, chunkBuffer, offset, null, allocatedBytes, job.isRedist, isCompressed), token).ConfigureAwait(false);

                                chunkBuffer = null;
                                allocatedBytes = 0;
                                memoryReserved = false;
                            }
                            else
                            {
                                tempFilePath = Path.Combine(tempDir, $"{job.chunk.compressedMd5}.{Guid.NewGuid():N}.tmp");
                                await RentAndUseAsync(bufferSize, async buffer =>
                                {
                                    using var networkStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                                    using var progressStream = new ProgressStream.ProgressStream(networkStream,
                                        new Progress<int>(bytesRead =>
                                        {
                                            Interlocked.Add(ref totalNetworkBytes, bytesRead);
                                            ReportProgress();
                                        }), null);
                                    using var tempFs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous);
                                    await progressStream.CopyToAsync(tempFs, bufferSize, token).ConfigureAwait(false);
                                    await tempFs.FlushAsync(token).ConfigureAwait(false);
                                }).ConfigureAwait(false);

                                await channel.Writer.WriteAsync((job.filePath, job.offset, null, 0, tempFilePath, 0, job.isRedist, isCompressed), token).ConfigureAwait(false);
                                tempFilePath = null;
                            }

                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (ObjectDisposedException ex) when (token.IsCancellationRequested)
                        {
                            throw new OperationCanceledException("Download canceled (stream closed)", ex, token);
                        }
                        catch (Exception ex)
                        {
                            logger.Debug($"Download attempt {attempt} failed for chunk {job.chunk.compressedMd5} of file {job.filePath}: {ex.Message}");

                            if (chunkBuffer != null)
                            {
                                try
                                {
                                    ArrayPool<byte>.Shared.Return(chunkBuffer);
                                }
                                catch { }
                                chunkBuffer = null;
                            }
                            if (memoryReserved && allocatedBytes > 0)
                            {
                                try
                                {
                                    memoryLimiter.Release(allocatedBytes);
                                }
                                catch { }
                                allocatedBytes = 0;
                                memoryReserved = false;
                            }
                            if (!string.IsNullOrEmpty(tempFilePath))
                            {
                                try
                                {
                                    File.Delete(tempFilePath);
                                }
                                catch { }
                                tempFilePath = null;
                            }

                            if (attempt < maxRetries)
                            {
                                await Task.Delay(delayMs, token).ConfigureAwait(false);
                            }
                            else
                            {
                                logger.Error($"Failed to download chunk {job.chunk.compressedMd5} of file {job.filePath} after {maxRetries} attempts: {ex}.");
                                throw;
                            }
                            delayMs *= 2;
                        }
                    }
                }
                finally
                {
                    try
                    {
                        if (allocatedBytes > 0)
                        {
                            memoryLimiter.Release(allocatedBytes);
                        }
                        if (chunkBuffer != null)
                        {
                            ArrayPool<byte>.Shared.Return(chunkBuffer);
                        }
                        if (slotAcquired)
                        {
                            downloadSemaphore.Release();
                        }
                        Interlocked.Decrement(ref activeDownloaders);
                        if (!string.IsNullOrEmpty(tempFilePath))
                        {
                            try
                            {
                                File.Delete(tempFilePath);
                            }
                            catch { }
                        }
                    }
                    catch { }
                    ReportProgress();
                }
            }, token)).ToList();

            //
            // STEP 3: Consumer – Writer (Decompress and Write to File)
            //
            int ioWorkerCount = Math.Min(maxParallel, Environment.ProcessorCount * 2);
            var ioWorkers = Enumerable.Range(0, ioWorkerCount).Select(_ => Task.Run(async () =>
            {
                await RentAndUseAsync(bufferSize, async consumerBuffer =>
                {
                    while (await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                    {
                        while (channel.Reader.TryRead(out var item))
                        {
                            token.ThrowIfCancellationRequested();

                            var fileWriteSemaphore = writeSemaphores[item.filePath];
                            await fileWriteSemaphore.WaitAsync(token).ConfigureAwait(false);
                            Interlocked.Increment(ref activeDiskers);

                            try
                            {
                                Stream sourceStream = item.chunkBuffer != null
                                    ? new MemoryStream(item.chunkBuffer, 0, item.length, writable: false)
                                    : new FileStream(item.tempFilePath!, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan | FileOptions.DeleteOnClose);

                                bool isSfcContainer = item.filePath.Contains(sfcContainerBaseName) && item.filePath.StartsWith(tempDir) && bigDepot.version == 2;


                                using (sourceStream)
                                using (var outFs = new FileStream(item.filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous))
                                {
                                    outFs.Seek(item.offset, SeekOrigin.Begin);

                                    int bytesRead;
                                    long totalWritten = 0;

                                    if (item.isCompressed)
                                    {
                                        using (var zlib = new ZlibStream(sourceStream, CompressionMode.Decompress))
                                        {
                                            while ((bytesRead = await zlib.ReadAsync(consumerBuffer, 0, consumerBuffer.Length, token).ConfigureAwait(false)) > 0)
                                            {
                                                await outFs.WriteAsync(consumerBuffer, 0, bytesRead, token).ConfigureAwait(false);
                                                if (!isSfcContainer)
                                                {
                                                    Interlocked.Add(ref totalDiskBytes, bytesRead);
                                                    ReportProgress();
                                                }
                                                totalWritten += bytesRead;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        while ((bytesRead = await sourceStream.ReadAsync(consumerBuffer, 0, consumerBuffer.Length, token).ConfigureAwait(false)) > 0)
                                        {
                                            await outFs.WriteAsync(consumerBuffer, 0, bytesRead, token).ConfigureAwait(false);

                                            if (!isSfcContainer)
                                            {
                                                Interlocked.Add(ref totalDiskBytes, bytesRead);
                                                ReportProgress();
                                            }
                                            totalWritten += bytesRead;
                                        }
                                    }

                                    if (fileExpectedSizes.ContainsKey(item.filePath) && item.offset + totalWritten == fileExpectedSizes[item.filePath])
                                    {
                                        if (outFs.Length != fileExpectedSizes[item.filePath])
                                        {
                                            outFs.SetLength(fileExpectedSizes[item.filePath]);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Error($"Error occurred during writing chunk to file {item.filePath}: {ex.Message}");
                                throw;
                            }
                            finally
                            {
                                try
                                {
                                    fileWriteSemaphore.Release();
                                }
                                catch { }

                                try
                                {
                                    if (item.chunkBuffer != null)
                                    {
                                        ArrayPool<byte>.Shared.Return(item.chunkBuffer);
                                    }
                                    if (item.allocatedBytes > 0)
                                    {
                                        memoryLimiter.Release(item.allocatedBytes);
                                    }
                                }
                                catch { }

                                Interlocked.Decrement(ref activeDiskers);
                                ReportProgress();
                            }
                        }
                    }
                }).ConfigureAwait(false);
            }, token)).ToList();


            try
            {
                await Task.WhenAll(downloadTasks).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.TryComplete();
            }

            await Task.WhenAll(ioWorkers).ConfigureAwait(false);

            // STEP 4: SFC File Extraction (V2 only)
            if (bigDepot.version == 2 && sfcExtractionJobs.Any())
            {
                var extractionWorkers = sfcExtractionJobs.Where(kvp => sfcHashesToDownload.Contains(kvp.Key))
                    .SelectMany(kvp =>
                    {
                        string depotHash = kvp.Key;
                        string containerFilePath = sfcFilePathsByHash[depotHash];
                        var orderedJobs = kvp.Value.OrderBy(job => job.sfcRef.offset).ToList();

                        return orderedJobs.Select(job => Task.Run(async () =>
                        {
                            token.ThrowIfCancellationRequested();
                            string smallFilePath = job.filePath;
                            GogDepot.sfcRef sfcRef = job.sfcRef;

                            if (File.Exists(smallFilePath) && new FileInfo(smallFilePath).Length == sfcRef.size)
                            {
                                return;
                            }

                            await sfcExtractSemaphore.WaitAsync(token).ConfigureAwait(false);
                            SemaphoreSlim fileWriteSemaphore = writeSemaphores[smallFilePath];

                            await fileWriteSemaphore.WaitAsync(token).ConfigureAwait(false);
                            Interlocked.Increment(ref activeDiskers);

                            try
                            {
                                if (!File.Exists(containerFilePath))
                                {
                                    throw new FileNotFoundException($"Missing SFC container file: {containerFilePath}.");
                                }

                                using (var inFs = new FileStream(containerFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
                                using (var outFs = new FileStream(smallFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous))
                                {
                                    inFs.Seek(sfcRef.offset, SeekOrigin.Begin);

                                    long expectedSize = (long)sfcRef.size;
                                    long remaining = expectedSize;

                                    await RentAndUseAsync(bufferSize, async consumerBuffer =>
                                    {
                                        while (remaining > 0)
                                        {
                                            int read = await inFs.ReadAsync(consumerBuffer, 0, (int)Math.Min(remaining, consumerBuffer.Length), token).ConfigureAwait(false);

                                            if (read == 0)
                                            {
                                                throw new EndOfStreamException($"Unexpected end of container file: Premature EOF encountered.");
                                            }

                                            await outFs.WriteAsync(consumerBuffer, 0, read, token).ConfigureAwait(false);

                                            Interlocked.Add(ref totalDiskBytes, read);
                                            ReportProgress();
                                            remaining -= read;
                                        }

                                        outFs.SetLength(expectedSize);

                                    }).ConfigureAwait(false);
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Error($"An error occurred during extraction of {smallFilePath} from container {containerFilePath}: {ex.Message}");
                                throw;
                            }
                            finally
                            {
                                try
                                {
                                    fileWriteSemaphore.Release();
                                }
                                catch { }
                                try
                                {
                                    sfcExtractSemaphore.Release();
                                }
                                catch { }
                                Interlocked.Decrement(ref activeDiskers);
                                ReportProgress();
                            }
                        }));
                    }).ToList();

                await Task.WhenAll(extractionWorkers).ConfigureAwait(false);
            }

            //
            // STEP 5: Cleanup
            //
            foreach (var s in writeSemaphores.Values)
            {
                try
                {
                    s.Dispose();
                }
                catch { }
            }

            try
            {
                isFinalReport = true;
                ReportProgress();

                if (bigDepot.version == 2 && sfcHashesToDownload.Any())
                {
                    foreach (var depotHash in sfcHashesToDownload)
                    {
                        if (sfcFilePathsByHash.TryGetValue(depotHash, out var path))
                        {
                            try
                            {
                                File.Delete(path);
                            }
                            catch { }
                        }
                    }
                }

                if (Directory.Exists(tempDir))
                {
                    foreach (var f in Directory.EnumerateFiles(tempDir))
                    {
                        try
                        {
                            File.Delete(f);
                        }
                        catch { }
                    }
                    try
                    {
                        Directory.Delete(tempDir, false);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public async Task Install(DownloadManagerData.Download taskData)
        {
            var installCommand = new List<string>();
            var settings = GogOssLibrary.GetSettings();
            var gameID = taskData.gameID;
            var downloadProperties = taskData.downloadProperties;
            var gameTitle = taskData.name;
            bool downloadSpeedInBits = false;
            if (settings.DisplayDownloadSpeedInBits)
            {
                downloadSpeedInBits = true;
            }

            gracefulInstallerCTS = new CancellationTokenSource();

            List<string> depotHashes = new();

            if (taskData.downloadItemType == DownloadItemType.Game)
            {
                depotHashes = await gogDownloadApi.GetNeededDepotManifestHashes(taskData);
            }
            else
            {
                var dependManifest = await GogDownloadApi.GetRedistInfo(taskData.gameID);
                depotHashes.Add(dependManifest.manifest);
            }

            var metaManifest = await gogDownloadApi.GetGameMetaManifest(taskData);

            var mainSecureLinks = await gogDownloadApi.GetSecureLinks(taskData);
            var allSecureLinks = new SecureLinks
            {
                mainSecureLinks = new(),
                inGameDependsSecureLinks = new()
            };
            allSecureLinks.mainSecureLinks.secureLinks = mainSecureLinks;


            GogDepot.Depot bigDepot = new();
            bigDepot.version = metaManifest.version;
            foreach (var depotHash in depotHashes)
            {
                var depotManifest = await gogDownloadApi.GetDepotInfo(depotHash, taskData, metaManifest.version);
                if (depotManifest.depot.items.Count > 0)
                {
                    foreach (var depotItem in depotManifest.depot.items)
                    {
                        if (depotItem.sfcRef != null)
                        {
                            depotItem.sfcRef.depotHash = depotHash;
                        }
                        bigDepot.items.Add(depotItem);
                    }
                }
                if (depotManifest.depot.smallFilesContainer?.chunks.Count > 0)
                {
                    bigDepot.sfcContainersByHash.Add(depotHash, depotManifest.depot.smallFilesContainer.chunks);
                }
                if (depotManifest.depot.files.Count > 0)
                {
                    foreach (var depotFile in depotManifest.depot.files)
                    {
                        bigDepot.files.Add(depotFile);
                    }
                }
            }

            List<string> inGameDependsHashes = new();
            if (metaManifest.version == 1)
            {
                foreach (var depot in metaManifest.product.depots)
                {
                    if (!depot.redist.IsNullOrEmpty() && !depot.targetDir.IsNullOrEmpty())
                    {
                        var dependManifest = await GogDownloadApi.GetRedistInfo(depot.redist);
                        var dependData = new DownloadManagerData.Download
                        {
                            gameID = depot.redist,
                            downloadItemType = DownloadItemType.Dependency
                        };
                        dependData.downloadProperties = new DownloadProperties
                        {
                            os = taskData.downloadProperties.os,
                        };

                        var depotManifest = await gogDownloadApi.GetDepotInfo(dependManifest.manifest, dependData);
                        if (depotManifest.depot.items.Count > 0)
                        {
                            var dependsSecureLinks = await gogDownloadApi.GetSecureLinks(dependData);
                            if (allSecureLinks.inGameDependsSecureLinks.secureLinks.Count == 0)
                            {

                                allSecureLinks.inGameDependsSecureLinks.secureLinks = dependsSecureLinks;
                            }
                            foreach (var depotItem in depotManifest.depot.items)
                            {
                                depotItem.redistTargetDir = depot.targetDir;
                                bigDepot.items.Add(depotItem);
                            }
                        }
                    }
                }
            }

            var wantedItem = downloadManagerData.downloads.FirstOrDefault(item => item.gameID == gameID);
            var startTime = DateTime.Now;
            var sw = Stopwatch.StartNew();
            long lastNetworkBytes = Interlocked.Read(ref resumeInitialNetworkBytes);
            long lastDiskBytes = Interlocked.Read(ref resumeInitialDiskBytes);
            TimeSpan lastStopwatchElapsed = TimeSpan.Zero;

            progress = new Progress<ProgressData>(p =>
            {
                taskData.downloadSizeNumber = p.TotalCompressedBytes;
                double dt = (sw.Elapsed - lastStopwatchElapsed).TotalSeconds;
                if (dt < 1 && !p.FinalReport) return;

                double rawNetSpeed = (p.NetworkBytes - lastNetworkBytes) / dt;
                double rawDiskSpeed = (p.DiskBytes - lastDiskBytes) / dt;

                rawNetSpeed = Math.Max(rawNetSpeed, 0);
                rawDiskSpeed = Math.Max(rawDiskSpeed, 0);

                lastStopwatchElapsed = sw.Elapsed;
                lastNetworkBytes = p.NetworkBytes;
                lastDiskBytes = p.DiskBytes;

                double initialNet = resumeInitialNetworkBytes;
                double initialDisk = resumeInitialDiskBytes;

                double avgNetSpeed = (p.NetworkBytes - initialNet) / sw.Elapsed.TotalSeconds;
                double avgDiskSpeed = (p.DiskBytes - initialDisk) / sw.Elapsed.TotalSeconds;

                double expansionRatio = p.NetworkBytes > 0 ? (double)p.TotalBytes / p.NetworkBytes : 1.0;
                double avgNetSpeedNormalized = avgNetSpeed * expansionRatio;

                double speedForEta = Math.Max(Math.Min(avgNetSpeedNormalized, avgDiskSpeed), 1.0);
                double remaining = p.TotalBytes - p.DiskBytes;
                string eta = remaining > 0 ? TimeSpan.FromSeconds(remaining / speedForEta).ToString(@"hh\:mm\:ss") : "00:00:00";

                DownloadSpeedTB.Text = CommonHelpers.FormatSize(rawNetSpeed, "B", downloadSpeedInBits) + "/s";
                DiskSpeedTB.Text = CommonHelpers.FormatSize(rawDiskSpeed, "B", downloadSpeedInBits) + "/s";
                EtaTB.Text = eta;
                ElapsedTB.Text = sw.Elapsed.ToString(@"hh\:mm\:ss");

                var item = downloadManagerData.downloads.FirstOrDefault(x => x.gameID == gameID);
                if (item != null)
                {
                    item.progress = p.TotalBytes > 0 ? (double)p.DiskBytes / p.TotalBytes * 100 : 0;
                    item.status = DownloadStatus.Running;
                    item.downloadedNumber = p.NetworkBytes;
                }
            });
            try
            {
                userCancelCTS = new CancellationTokenSource();
                var linkedCTS = CancellationTokenSource.CreateLinkedTokenSource(
                    gracefulInstallerCTS.Token,
                    userCancelCTS.Token
                );

                wantedItem.status = DownloadStatus.Running;
                if (downloadProperties.maxWorkers == 0)
                {
                    downloadProperties.maxWorkers = CommonHelpers.CpuThreadsNumber;
                }
                await DownloadFilesAsync(linkedCTS.Token, bigDepot, wantedItem.fullInstallPath, allSecureLinks, downloadProperties.maxWorkers);

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
                    var gameMetaManifest = await gogDownloadApi.GetGameMetaManifest(gameID);
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
                    var notificationMessage = LOC.CommonInstallationFinished;
                    switch (downloadProperties.downloadAction)
                    {
                        case DownloadAction.Repair:
                            notificationMessage = LOC.CommonRepairFinished;
                            break;
                        case DownloadAction.Update:
                            notificationMessage = LOC.CommonUpdateFinished;
                            break;
                        default:
                            break;
                    }
                    var bitmap = new System.Drawing.Bitmap(GogOss.Icon);
                    var iconHandle = bitmap.GetHicon();
                    Playnite.WindowsNotifyIconManager.Notify(System.Drawing.Icon.FromHandle(iconHandle), gameTitle, LocalizationManager.Instance.GetString(notificationMessage), null);
                    bitmap.Dispose();
                }
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException))
                {
                    logger.Error(ex.Message);
                    wantedItem.status = DownloadStatus.Error;
                }
                else if (userCancelCTS.IsCancellationRequested)
                {
                    if (Directory.Exists(taskData.fullInstallPath))
                        Directory.Delete(taskData.fullInstallPath, true);
                }
            }
            finally
            {
                sw.Stop();
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
                                                                 .Where(i => i.status != DownloadStatus.Completed && i.status != DownloadStatus.Running && i.status != DownloadStatus.Queued)
                                                                 .ToList();
                await EnqueueMultipleJobs(downloadsToResume, true);
            }
        }

        private void CancelDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadsDG.SelectedIndex != -1)
            {
                var cancelableDownloads = DownloadsDG.SelectedItems.Cast<DownloadManagerData.Download>()
                                                                   .Where(i => i.status != DownloadStatus.Completed && i.status != DownloadStatus.Canceled)
                                                                   .ToList();
                if (cancelableDownloads.Count > 0)
                {
                    foreach (var selectedRow in cancelableDownloads)
                    {
                        if (selectedRow.status == DownloadStatus.Running)
                        {
                            // Gogdl can't properly gracefully cancel, so we need to force it :-)
                            userCancelCTS?.Cancel();
                            userCancelCTS?.Dispose();
                            //gracefulInstallerCTS?.Dispose();
                        }
                        //if (selectedRow.fullInstallPath != null && selectedRow.downloadProperties.downloadAction == DownloadAction.Install)
                        //{
                        //	if (Directory.Exists(selectedRow.fullInstallPath))
                        //	{
                        //		Directory.Delete(selectedRow.fullInstallPath, true);
                        //	}
                        //}
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
                    messageText = LocalizationManager.Instance.GetString(LOC.CommonRemoveEntryConfirm, new Dictionary<string, IFluentType> { ["entryName"] = (FluentString)selectedRow.name });
                }
                else
                {
                    messageText = LocalizationManager.Instance.GetString(LOC.CommonRemoveSelectedEntriesConfirm);
                }
                var result = playniteAPI.Dialogs.ShowMessage(messageText, LocalizationManager.Instance.GetString(LOC.CommonRemoveEntry), MessageBoxButton.YesNo, MessageBoxImage.Question);
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
                var result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonRemoveCompletedDownloadsConfirm), LocalizationManager.Instance.GetString(LOC.CommonRemoveCompletedDownloads), MessageBoxButton.YesNo, MessageBoxImage.Question);
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
                FilterDownloadBtn.Content = "\uef29 " + LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteFilterActiveLabel);
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
                window.Title = selectedItem.name + " — " + LocalizationManager.Instance.GetString(LOC.CommonDownloadProperties);
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
                playniteAPI.Dialogs.ShowErrorMessage($"{selectedItem.fullInstallPath}\n{LocalizationManager.Instance.GetString(LOC.CommonPathNotExistsError)}");
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
