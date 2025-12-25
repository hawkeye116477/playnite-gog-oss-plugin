using CliWrap;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
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
using System.Windows.Threading;

namespace GogOssLibraryNS
{
    /// <summary>
    /// Interaction logic for GogOssDownloadManagerView.xaml
    /// </summary>
    public partial class GogOssDownloadManagerView : UserControl
    {
        public CancellationTokenSource gracefulInstallerCTS;
        public CancellationTokenSource userCancelCTS;

        private ILogger logger = LogManager.GetLogger();
        private IPlayniteAPI playniteAPI = API.Instance;
        public DownloadManagerData downloadManagerData;
        public SidebarItem gogPanel = GogOssLibrary.GetPanel();
        public bool downloadsChanged = false;
        private static readonly RetryHandler retryHandler = new(new HttpClientHandler());
        private static readonly HttpClient client = new HttpClient(retryHandler);
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

        private async Task RentAndUsePool(int size, Func<byte[], Task> action)
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

        private async Task DownloadNonGames(CancellationToken token,
                                            GogDepot.Depot bigDepot,
                                            string fullInstallPath,
                                            int maxParallel,
                                            DownloadItemType downloadItemType,
                                            int bufferSize = 512 * 1024,
                                            long maxMemoryBytes = 1L * 1024 * 1024 * 1024)
        {
            // STEP 0: Initial Setup
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, maxParallel * 2);

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", GogDownloadApi.UserAgent);

            using var downloadSemaphore = new SemaphoreSlim(maxParallel);
            using var memoryLimiter = new MemoryLimiter(maxMemoryBytes);
            var writeSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

            // Producer-consumer channel
            var channel = Channel.CreateUnbounded<(string filePath, long length, string tempFilePath, long allocatedBytes, bool isCompressed, string hash, byte[] chunkBuffer)>(
                );

            var jobs = new HashSet<(string filePath, long size, string url, string hash)>();

            long totalCompressedSize = 0;
            long initialNetworkBytesLocal = 0;
            var fileExpectedSizes = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            var tempDir = Path.Combine(fullInstallPath, ".Downloader_temp");
            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            var resumeState = new ResumeState();
            string resumeStatePath = Path.Combine(tempDir, "resume-state.json");
            resumeState.Load(resumeStatePath);

            //
            // STEP 1: JOB CREATION
            //
            foreach (var file in bigDepot.files)
            {
                var depotFilePath = file.path.TrimStart('/', '\\');
                var filePath = Path.Combine(fullInstallPath, depotFilePath);
                var targetDirectory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }
                if (downloadItemType != DownloadItemType.Extra)
                {
                    writeSemaphores.TryAdd(depotFilePath, new SemaphoreSlim(1));
                }

                long expectedFileSize = file.size;
                totalCompressedSize += expectedFileSize;

                if (expectedFileSize == 0)
                {
                    if (!File.Exists(filePath) || new FileInfo(filePath).Length != 0)
                    {
                        File.WriteAllBytes(filePath, Array.Empty<byte>());
                    }
                    fileExpectedSizes.TryAdd(filePath, 0);
                    continue;
                }

                if (!resumeState.IsCompleted(filePath, file.hash))
                {
                    jobs.Add((depotFilePath, file.size, file.url, file.hash));
                }
                else
                {
                    initialNetworkBytesLocal += expectedFileSize;
                }

                fileExpectedSizes.TryAdd(depotFilePath, expectedFileSize);
            }

            jobs = jobs.OrderBy(job => job.size).ToHashSet();

            Interlocked.Exchange(ref resumeInitialNetworkBytes, initialNetworkBytesLocal);

            long totalNetworkBytes = initialNetworkBytesLocal;

            int activeDownloaders = 0, activeDiskers = 0;
            bool isFinalReport = false;

            void ReportProgress()
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                progress?.Report(new ProgressData
                {
                    TotalBytes = totalCompressedSize,
                    TotalCompressedBytes = totalCompressedSize,
                    NetworkBytes = Interlocked.Read(ref totalNetworkBytes),
                    DiskBytes = Interlocked.Read(ref totalNetworkBytes),
                    ActiveDownloadWorkers = activeDownloaders,
                    ActiveDiskWorkers = activeDiskers,
                    FinalReport = isFinalReport
                });
            }

            ReportProgress();

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

                    long compressedSize = job.size;
                    bool memoryReserved = false;

                    bool isCompressed = false;
                    if (downloadItemType == DownloadItemType.Overlay)
                    {
                        isCompressed = true;
                    }
                    string effectiveFilePath = job.filePath;
                    try
                    {
                        if (!memoryReserved && allocatedBytes == 0)
                        {
                            memoryReserved = memoryLimiter.TryReserve(compressedSize);
                            if (memoryReserved)
                            {
                                allocatedBytes = compressedSize;
                            }
                        }

                        long resumeStartByte = 0;

                        var gogAccountClient = new GogAccountClient();

                        var tokens = new TokenResponse.TokenResponsePart();
                        if (await gogAccountClient.GetIsUserLoggedIn())
                        {
                            tokens = gogAccountClient.LoadTokens();
                        }

                        if (downloadItemType == DownloadItemType.Extra)
                        {
                            using var headRequest = new HttpRequestMessage(HttpMethod.Head, job.url);
                            headRequest.Headers.Add("Authorization", $"Bearer {tokens.access_token}");
                            using var headResponse = await client.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                            headResponse.EnsureSuccessStatusCode();
                            var contentDisposition = headResponse.Content.Headers.ContentDisposition;
                            var serverFileName =
                                contentDisposition?.FileNameStar ??
                                contentDisposition?.FileName;
                            if (serverFileName.IsNullOrEmpty())
                            {
                                var finalUrl = headResponse.RequestMessage.RequestUri;
                                serverFileName = Path.GetFileName(finalUrl.LocalPath);
                            }
                            if (!string.IsNullOrWhiteSpace(serverFileName))
                            {
                                effectiveFilePath = Path.Combine(job.filePath, serverFileName.Trim('"'));
                            }
                            writeSemaphores.TryAdd(effectiveFilePath, new SemaphoreSlim(1));
                        }

                        if (downloadItemType == DownloadItemType.Overlay && job.url.Contains(".zip"))
                        {
                            tempFilePath = Path.Combine(tempDir, job.filePath + ".zip");
                        }
                        else
                        {
                            tempFilePath = Path.Combine(tempDir, effectiveFilePath);
                        }

                        using var request = new HttpRequestMessage(HttpMethod.Get, job.url);
                        request.Headers.Add("Authorization", $"Bearer {tokens.access_token}");

                        if (File.Exists(tempFilePath))
                        {
                            resumeStartByte = new FileInfo(tempFilePath).Length;
                            if (resumeStartByte >= compressedSize)
                            {
                                await channel.Writer.WriteAsync((job.filePath, resumeStartByte, tempFilePath, 0, isCompressed, job.hash, null), token).ConfigureAwait(false);
                                return;
                            }
                            else if (resumeStartByte > 0)
                            {
                                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeStartByte, compressedSize - 1);
                            }
                        }
                        var tempFileDir = Path.GetDirectoryName(tempFilePath);
                        if (!string.IsNullOrEmpty(tempFileDir))
                        {
                            Directory.CreateDirectory(tempFileDir);
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

                            await channel.Writer.WriteAsync((effectiveFilePath, offset, tempFilePath, allocatedBytes, isCompressed, job.hash, chunkBuffer), token).ConfigureAwait(false);
                            chunkBuffer = null;
                            allocatedBytes = 0;
                            return;
                        }
                        else
                        {
                            long actualFileSize = 0;
                            await RentAndUsePool(bufferSize, async buffer =>
                            {
                                using var networkStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                                using var progressStream = new ProgressStream.ProgressStream(networkStream,
                                    new Progress<int>(bytesRead =>
                                    {
                                        Interlocked.Add(ref totalNetworkBytes, bytesRead);
                                        ReportProgress();
                                    }), null);

                                FileMode fileMode = resumeStartByte > 0 ? FileMode.Append : FileMode.Create;

                                using (var tempFs = new FileStream(tempFilePath, fileMode, FileAccess.Write, FileShare.Read, bufferSize, FileOptions.Asynchronous))
                                {
                                    await progressStream.CopyToAsync(tempFs, bufferSize, token).ConfigureAwait(false);
                                    await tempFs.FlushAsync(token).ConfigureAwait(false);
                                    actualFileSize = tempFs.Length;
                                }
                            }).ConfigureAwait(false);

                            await channel.Writer.WriteAsync((effectiveFilePath, actualFileSize, tempFilePath, 0, isCompressed, job.hash, chunkBuffer), token).ConfigureAwait(false);
                        }
                        return;
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
                        logger.Error(ex, $"Failed to download file {effectiveFilePath}.");
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
                        tempFilePath = null;
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
                await RentAndUsePool(bufferSize, async consumerBuffer =>
                {
                    while (await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                    {
                        while (channel.Reader.TryRead(out var item))
                        {
                            var fileWriteSemaphore = writeSemaphores[item.filePath];
                            await fileWriteSemaphore.WaitAsync(token).ConfigureAwait(false);
                            Interlocked.Increment(ref activeDiskers);

                            try
                            {
                                Stream sourceStream;
                                if (item.chunkBuffer == null)
                                {
                                    sourceStream = new FileStream(item.tempFilePath!, FileMode.Open, FileAccess.Read,
                                                                     FileShare.Read, bufferSize,
                                                                     FileOptions.SequentialScan);
                                }
                                else
                                {
                                    sourceStream = new MemoryStream(item.chunkBuffer, 0, (int)item.length, writable: false);
                                }

                                var finalPath = Path.Combine(fullInstallPath, item.filePath);
                                using (sourceStream)
                                using (var outFs = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous))
                                {
                                    int bytesRead;
                                    long totalWritten = 0;
                                    if (item.isCompressed)
                                    {
                                        using (var zip = SharpCompress.Archives.Zip.ZipArchive.Open(sourceStream))
                                        {
                                            var entry = zip.Entries.FirstOrDefault(e => !e.IsDirectory);
                                            if (entry != null)
                                            {
                                                using (var entryStream = entry.OpenEntryStream())
                                                {
                                                    await entryStream.CopyToAsync(outFs, bufferSize, token).ConfigureAwait(false);
                                                }
                                            }
                                            ReportProgress();
                                        }
                                    }
                                    else
                                    {
                                        while ((bytesRead = await sourceStream.ReadAsync(consumerBuffer, 0, consumerBuffer.Length, token).ConfigureAwait(false)) > 0)
                                        {
                                            await outFs.WriteAsync(consumerBuffer, 0, bytesRead, token).ConfigureAwait(false);
                                            ReportProgress();
                                            totalWritten += bytesRead;
                                        }
                                    }
                                    if (!item.isCompressed && fileExpectedSizes.TryGetValue(item.filePath, out long expectedSize))
                                    {
                                        if (outFs.Length != expectedSize)
                                        {
                                            outFs.SetLength(expectedSize);
                                        }
                                    }
                                    resumeState.MarkCompleted(finalPath, item.hash);
                                }
                                if (item.chunkBuffer == null && File.Exists(item.tempFilePath))
                                {
                                    try
                                    {
                                        File.Delete(item.tempFilePath);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.Warn(ex, $"Couldn't delete temp file {item.tempFilePath}.");
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                logger.Error(ex, $"Error occurred during writing to file {item.filePath}.");
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
                try
                {
                    await Task.WhenAll(ioWorkers).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {

                }
                try
                {
                    resumeState.Save(resumeStatePath);
                }
                catch (Exception)
                {

                }
            }

            token.ThrowIfCancellationRequested();

            //
            // STEP 4: Cleanup
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

                if (Directory.Exists(tempDir))
                {
                    if (!token.IsCancellationRequested)
                    {
                        try
                        {
                            Directory.Delete(tempDir, true);
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, $"Failed to recursively delete temporary directory {tempDir} after successful download.");
                        }
                    }
                    else
                    {
                        logger.Info($"Download was canceled. Retaining temporary directory: {tempDir} for resume.");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"An error occurred during final cleanup.");
            }
        }


        private async Task DownloadGamesAndDepends(CancellationToken token,
                                                   GogDepot.Depot bigDepot,
                                                   string fullInstallPath,
                                                   GogSecureLinks allSecureLinks,
                                                   int maxParallel,
                                                   int bufferSize = 512 * 1024,
                                                   long maxMemoryBytes = 1L * 1024 * 1024 * 1024,
                                                   string preferredCdn = "")
        {
            // STEP 0: Initial Setup
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, maxParallel * 2);
            var secureLinks = allSecureLinks.mainSecureLinks;
            var dependencyLinks = allSecureLinks.inGameDependsSecureLinks;
            var patchesLinks = allSecureLinks.patchSecureLinks;

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", GogDownloadApi.UserAgent);

            using var downloadSemaphore = new SemaphoreSlim(maxParallel);
            using var sfcExtractSemaphore = new SemaphoreSlim(Math.Min(maxParallel, Environment.ProcessorCount * 2));
            using var memoryLimiter = new MemoryLimiter(maxMemoryBytes);

            const string sfcContainerBaseName = "smallFilesContainer";
            const string chunkTempBaseName = "temp_chunk_";
            var sfcFilePathsByHash = new ConcurrentDictionary<string, string>();
            var sfcExtractionJobs = new ConcurrentDictionary<string, List<(string filePath, GogDepot.sfcRef sfcRef)>>();

            var sfcFiles = new List<GogDepot.Item>();

            var sfcHashesToDownload = new HashSet<string>();

            var writeSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();

            // Producer-consumer channel
            var channel = Channel.CreateUnbounded<(string filePath, long offset, byte[] chunkBuffer, int length, string tempFilePath, long allocatedBytes, bool isRedist, bool isCompressed, string chunkId)>(
                );

            var jobs = new HashSet<(string filePath, long offset, GogDepot.Chunk chunk, bool isRedist, string productId)>();

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

            var resumeState = new ResumeState();
            string resumeStatePath = Path.Combine(tempDir, "resume-state.json");
            resumeState.Load(resumeStatePath);

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
                        filePath = Path.Combine(GogOss.DependenciesInstallationPath, depotFilePath);
                    }

                    var targetDirectory = Path.GetDirectoryName(filePath);
                    if (!Directory.Exists(targetDirectory))
                    {
                        Directory.CreateDirectory(targetDirectory);
                    }
                    writeSemaphores.TryAdd(filePath, new SemaphoreSlim(1));

                    long expectedFileSize = file.size;

                    if (expectedFileSize == 0)
                    {
                        if (!File.Exists(filePath) || new FileInfo(filePath).Length != 0)
                        {
                            File.WriteAllBytes(filePath, Array.Empty<byte>());
                        }
                        fileExpectedSizes.TryAdd(filePath, 0);
                        continue;
                    }

                    long currentFileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;

                    totalSize += expectedFileSize;
                    totalCompressedSize += expectedFileSize;

                    var v1Chunk = new GogDepot.Chunk
                    {
                        offset = file.offset,
                        size = expectedFileSize,
                        compressedSize = expectedFileSize,
                        compressedMd5 = file.url, // Probably main.bin always?
                    };
                    if (currentFileSize != expectedFileSize && !resumeState.IsCompleted(filePath, v1Chunk.compressedMd5))
                    {
                        jobs.Add((filePath, 0, v1Chunk, isRedist, file.product_id));
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
                    depotItem.path = depotItem.path.TrimStart('/', '\\');
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
                        List<GogDepot.Chunk> chunks = kvp.Value.chunks;

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

                            totalSize += chunkSize;
                            totalCompressedSize += compressedSize;

                            if (foundFirstIncompleteChunk)
                            {
                                chunk.offset = sfcPos;
                                if (!resumeState.IsCompleted(sfcFilePath, chunk.compressedMd5))
                                {
                                    jobs.Add((sfcFilePath, sfcPos, chunk, false, kvp.Value.product_id));
                                }
                            }
                            else if (sfcPos + chunkSize <= currentSfcSize)
                            {
                                initialDiskBytesLocal += chunkSize;
                                initialNetworkBytesLocal += compressedSize;
                            }
                            else
                            {
                                chunk.offset = sfcPos;
                                if (!resumeState.IsCompleted(sfcFilePath, chunk.compressedMd5))
                                {
                                    jobs.Add((sfcFilePath, sfcPos, chunk, false, kvp.Value.product_id));
                                }
                                else
                                {
                                    initialDiskBytesLocal += chunkSize;
                                    initialNetworkBytesLocal += compressedSize;
                                }
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
                    depot.path = depot.path.TrimStart('/', '\\');
                    bool isRedist = false;

                    string filePath;
                    if (string.Equals(depot.type ?? "", "DepotDiff", StringComparison.OrdinalIgnoreCase))
                    {
                        string md5part = depot.md5 ?? Guid.NewGuid().ToString();
                        string patchFileName = $"{md5part}.patch";
                        filePath = Path.Combine(tempDir, patchFileName);
                    }
                    else
                    {
                        filePath = Path.Combine(fullInstallPath, depot.path);
                    }

                    if (filePath.Contains("__redist"))
                    {
                        filePath = Path.Combine(GogOss.DependenciesInstallationPath, depot.path);
                    }

                    if (depot.redistTargetDir != "")
                    {
                        filePath = Path.Combine(fullInstallPath, depot.path);
                        isRedist = true;
                    }

                    if (depot.type == "DepotDirectory")
                    {
                        Directory.CreateDirectory(filePath);
                        continue;
                    }
                    else
                    {
                        var knownTypes = new List<string> { "DepotDirectory", "DepotFile", "DepotDiff" };
                        if (!knownTypes.Contains(depot.type))
                        {
                            logger.Warn($"Depot type {depot.type} isn't supported. Please report that.");
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                    }

                    if (depot.sfcRef == null || !shouldDownloadSfc)
                    {
                        writeSemaphores.TryAdd(filePath, new SemaphoreSlim(1));
                    }

                    long expectedFileSize = depot.sfcRef != null && shouldDownloadSfc ? (long)depot.sfcRef.size : depot.chunks.Sum(c => (long)c.size);

                    if (expectedFileSize == 0)
                    {
                        if (!File.Exists(filePath) || new FileInfo(filePath).Length != 0)
                        {
                            File.WriteAllBytes(filePath, Array.Empty<byte>());
                        }
                        fileExpectedSizes.TryAdd(filePath, 0);
                        continue;
                    }

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

                    if (File.Exists(filePath))
                    {
                        if (currentFileSize > expectedFileSize)
                        {
                            try
                            {
                                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
                                fs.SetLength(expectedFileSize);
                                logger.Info($"Trimmed file {filePath} from {currentFileSize} to {expectedFileSize} bytes.");
                            }
                            catch (Exception ex)
                            {
                                logger.Warn(ex, $"Failed to trim file {filePath}.");
                            }
                        }
                    }


                    if (depot.sfcRef == null || !shouldDownloadSfc)
                    {
                        foreach (var chunk in depot.chunks)
                        {
                            long chunkSize = (long)chunk.size;
                            long compressedSize = (long)chunk.compressedSize;

                            if (foundFirstIncompleteChunk)
                            {
                                chunk.offset = pos;
                                if (!resumeState.IsCompleted(filePath, chunk.compressedMd5))
                                {
                                    jobs.Add((filePath, pos, chunk, isRedist, depot.product_id));
                                }
                                else
                                {
                                    initialDiskBytesLocal += chunkSize;
                                    initialNetworkBytesLocal += compressedSize;
                                }
                            }
                            else if (pos + chunkSize <= currentFileSize)
                            {
                                initialDiskBytesLocal += chunkSize;
                                initialNetworkBytesLocal += compressedSize;
                            }
                            else
                            {
                                chunk.offset = pos;
                                if (!resumeState.IsCompleted(filePath, chunk.compressedMd5))
                                {
                                    jobs.Add((filePath, pos, chunk, isRedist, depot.product_id));
                                }
                                else
                                {
                                    initialDiskBytesLocal += chunkSize;
                                    initialNetworkBytesLocal += compressedSize;
                                }
                                foundFirstIncompleteChunk = true;
                            }
                            pos += chunkSize;

                            string chunkTempPath = Path.Combine(tempDir, $"{chunkTempBaseName}{chunk.compressedMd5}");
                            if (File.Exists(chunkTempPath))
                            {
                                initialNetworkBytesLocal += Math.Min(new FileInfo(chunkTempPath).Length, compressedSize);
                            }
                        }
                    }
                }
            }


            jobs = jobs.OrderBy(job => job.offset)
                       .ToHashSet();


            Interlocked.Exchange(ref resumeInitialDiskBytes, initialDiskBytesLocal);
            Interlocked.Exchange(ref resumeInitialNetworkBytes, initialNetworkBytesLocal);

            long totalNetworkBytes = initialNetworkBytesLocal;
            long totalDiskBytes = initialDiskBytesLocal;

            int activeDownloaders = 0, activeDiskers = 0;
            bool isFinalReport = false;

            ReportProgress();
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

                var chunk = job.chunk;

                string chunkTempPath = Path.Combine(tempDir, $"{chunkTempBaseName}{chunk.compressedMd5}");

                try
                {
                    await downloadSemaphore.WaitAsync(token).ConfigureAwait(false);
                    slotAcquired = true;
                    Interlocked.Increment(ref activeDownloaders);
                    ReportProgress();

                    long compressedSize = (long)chunk.compressedSize;

                    bool isV1 = bigDepot.version == 1;
                    bool isCompressed = !isV1 || job.isRedist;

                    bool isPatchFile = Path.GetFileName(job.filePath).IndexOf(".patch", StringComparison.OrdinalIgnoreCase) >= 0;
                    var currentSecureLinksDict = new Dictionary<string, List<GogSecureLinks.FinalUrl>>();

                    if (isPatchFile)
                    {
                        currentSecureLinksDict = patchesLinks;
                    }
                    else if (job.isRedist)
                    {
                        currentSecureLinksDict = dependencyLinks;
                    }
                    else
                    {
                        currentSecureLinksDict = secureLinks;
                    }
                    var productId = job.productId;
                    if (job.isRedist)
                    {
                        productId = "redist_v2";
                    }
                    var currentSecureLinks = currentSecureLinksDict[productId];

                    var availableCdns = new List<GogSecureLinks.FinalUrl>(currentSecureLinks);

                    if (preferredCdn != "")
                    {
                        var preferredLink = availableCdns.FirstOrDefault(l => l.endpoint_name.Contains(preferredCdn.ToLower()));
                        if (!preferredLink.formatted_url.IsNullOrEmpty())
                        {
                            availableCdns.Remove(preferredLink);
                            availableCdns.Insert(0, preferredLink);
                        }
                    }

                    foreach (var currentSecureLink in availableCdns)
                    {
                        bool memoryReserved = false;

                        try
                        {
                            if (!memoryReserved && allocatedBytes == 0)
                            {
                                memoryReserved = memoryLimiter.TryReserve(compressedSize);
                                if (memoryReserved)
                                {
                                    allocatedBytes = compressedSize;
                                }
                            }

                            string url;
                            if (isV1 && !job.isRedist)
                            {
                                url = currentSecureLink.formatted_url.Replace("{GALAXY_PATH}", Path.GetFileName(chunk.compressedMd5));
                            }
                            else
                            {
                                url = currentSecureLink.formatted_url.Replace("{GALAXY_PATH}", gogDownloadApi.GetGalaxyPath(chunk.compressedMd5));
                            }

                            using var request = new HttpRequestMessage(HttpMethod.Get, url);

                            if (isV1)
                            {
                                long start = job.chunk.offset;
                                long end = job.chunk.offset + (long)job.chunk.size - 1;
                                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);
                            }

                            long resumeStartByte = 0;
                            if (!memoryReserved)
                            {
                                tempFilePath = chunkTempPath;

                                if (File.Exists(tempFilePath))
                                {
                                    resumeStartByte = new FileInfo(tempFilePath).Length;
                                    if (resumeStartByte >= compressedSize)
                                    {
                                        await channel.Writer.WriteAsync((job.filePath, job.offset, null, (int)compressedSize, tempFilePath, 0, job.isRedist, isCompressed, chunk.compressedMd5), token).ConfigureAwait(false);
                                        tempFilePath = null;
                                        return;
                                    }
                                    else if (resumeStartByte > 0)
                                    {
                                        if (!isV1)
                                        {
                                            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeStartByte, compressedSize - 1);
                                        }
                                        else
                                        {
                                            resumeStartByte = 0;
                                        }
                                    }
                                }
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

                                await channel.Writer.WriteAsync((job.filePath, job.offset, chunkBuffer, offset, null, allocatedBytes, job.isRedist, isCompressed, chunk.compressedMd5), token).ConfigureAwait(false);

                                chunkBuffer = null;
                                allocatedBytes = 0;
                                return;
                            }
                            else
                            {
                                await RentAndUsePool(bufferSize, async buffer =>
                                {
                                    using var networkStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                                    using var progressStream = new ProgressStream.ProgressStream(networkStream,
                                        new Progress<int>(bytesRead =>
                                        {
                                            Interlocked.Add(ref totalNetworkBytes, bytesRead);
                                            ReportProgress();
                                        }), null);

                                    FileMode fileMode = resumeStartByte > 0 ? FileMode.Append : FileMode.Create;

                                    using (var tempFs = new FileStream(tempFilePath, fileMode, FileAccess.Write, FileShare.Read, bufferSize, FileOptions.Asynchronous))
                                    {
                                        await progressStream.CopyToAsync(tempFs, bufferSize, token).ConfigureAwait(false);
                                        await tempFs.FlushAsync(token).ConfigureAwait(false);
                                    }
                                }).ConfigureAwait(false);
                                await channel.Writer.WriteAsync((job.filePath, job.offset, null, (int)compressedSize, tempFilePath, 0, job.isRedist, isCompressed, chunk.compressedMd5), token).ConfigureAwait(false);
                            }

                            return;
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
                            logger.Warn(ex, $"Download failed for chunk {job.chunk.compressedMd5} using {currentSecureLink.endpoint_name} CDN. Trying next CDN...");

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

                            tempFilePath = null;
                        }
                    }

                    throw new Exception($"Failed to download chunk {job.chunk.compressedMd5} of file {job.filePath} after trying all available CDNs.");
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
                await RentAndUsePool(bufferSize, async consumerBuffer =>
                {
                    while (await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                    {
                        while (channel.Reader.TryRead(out var item))
                        {
                            var fileWriteSemaphore = writeSemaphores[item.filePath];
                            await fileWriteSemaphore.WaitAsync(token).ConfigureAwait(false);
                            Interlocked.Increment(ref activeDiskers);

                            try
                            {
                                Stream sourceStream;
                                if (item.chunkBuffer != null)
                                {
                                    sourceStream = new MemoryStream(item.chunkBuffer, 0, item.length, writable: false);
                                }
                                else
                                {
                                    sourceStream = new FileStream(item.tempFilePath!,
                                                                    FileMode.Open,
                                                                    FileAccess.Read,
                                                                    FileShare.Read,
                                                                    bufferSize,
                                                                    FileOptions.SequentialScan);
                                }

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
                                    resumeState.MarkCompleted(item.filePath, item.chunkId);
                                }
                                if (item.chunkBuffer == null && File.Exists(item.tempFilePath))
                                {
                                    try
                                    {
                                        File.Delete(item.tempFilePath);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.Warn(ex, $"Couldn't delete temp file {item.tempFilePath}.");
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                logger.Error(ex, $"Error occurred during writing chunk to file {item.filePath}");
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
                try
                {
                    await Task.WhenAll(ioWorkers).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {

                }
                try
                {
                    resumeState.Save(resumeStatePath);
                }
                catch (Exception)
                {

                }
            }

            // STEP 4: SFC File Extraction (V2 only)
            if (bigDepot.version == 2 && sfcExtractionJobs.Any())
            {
                var extractionWorkers = sfcExtractionJobs.Where(kvp => sfcHashesToDownload.Contains(kvp.Key))
                    .SelectMany(kvp =>
                    {
                        string depotHash = kvp.Key;
                        string containerFilePath = sfcFilePathsByHash[depotHash];
                        var orderedJobs = kvp.Value.OrderBy(job => job.sfcRef.offset).ToHashSet();

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

                                    await RentAndUsePool(bufferSize, async consumerBuffer =>
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
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                logger.Error(ex, $"An error occurred during extraction of {smallFilePath} from container {containerFilePath}");
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

                try
                {
                    await Task.WhenAll(extractionWorkers).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                }
            }


            // STEP 5: Patching
            try
            {
                var allItems = bigDepot.items.Concat(sfcFiles).ToList();
                if (allItems != null && allItems.Count > 0)
                {
                    var depotDiffItems = allItems.Where(i => string.Equals(i.type ?? "", "DepotDiff", StringComparison.OrdinalIgnoreCase)).ToList();

                    if (depotDiffItems != null && depotDiffItems.Count > 0)
                    {
                        logger.Info($"Found {depotDiffItems.Count} patches to apply concurrently (max {sfcExtractSemaphore.CurrentCount} at a time).");
                        var patchingTasks = new List<Task>();

                        using var reportingCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                        var reportingToken = reportingCts.Token;

                        var reportingTask = Task.Run(async () =>
                        {
                            while (!reportingToken.IsCancellationRequested)
                            {
                                try
                                {
                                    await Task.Delay(500, reportingToken).ConfigureAwait(false);
                                    ReportProgress();
                                }
                                catch (OperationCanceledException)
                                {
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    logger.Error(ex, $"Error in patching reporting task.");
                                    await Task.Delay(1000, reportingToken).ConfigureAwait(false);
                                }
                            }
                        }, reportingToken);

                        var targetFileSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>();

                        try
                        {
                            foreach (var diff in depotDiffItems)
                            {
                                token.ThrowIfCancellationRequested();

                                var patchTask = Task.Run(async () =>
                                {
                                    string sourceRel = diff.path_source ?? "";
                                    string targetRel = diff.path_target ?? diff.path ?? sourceRel;

                                    string sourcePath = Path.Combine(fullInstallPath, sourceRel.TrimStart('\\', '/'));
                                    string finalTargetPath = Path.Combine(fullInstallPath, targetRel.TrimStart('\\', '/'));


                                    var targetLock = targetFileSemaphores.GetOrAdd(finalTargetPath, _ => new SemaphoreSlim(1, 1));

                                    string patchedTempPath = "";

                                    await sfcExtractSemaphore.WaitAsync(token).ConfigureAwait(false);
                                    await targetLock.WaitAsync(token).ConfigureAwait(false);

                                    try
                                    {
                                        string md5part = diff.md5 ?? Guid.NewGuid().ToString();
                                        string deltaTempPath = Path.Combine(tempDir, $"{md5part}.patch");

                                        if (!File.Exists(deltaTempPath))
                                        {
                                            throw new FileNotFoundException($"Expected delta not found in temp: {deltaTempPath}");
                                        }

                                        if (!File.Exists(sourcePath))
                                        {
                                            throw new FileNotFoundException($"Source file required for DepotDiff patch not found: {sourcePath}");
                                        }

                                        patchedTempPath = Path.Combine(tempDir, Guid.NewGuid().ToString() + ".patched");

                                        try
                                        {
                                            var result = await Cli.Wrap(Xdelta.InstallationPath)
                                                                  .WithArguments(new[] { "-d", "-s", sourcePath, deltaTempPath, patchedTempPath })
                                                                  .AddCommandToLog()
                                                                  .ExecuteAsync();

                                            Directory.CreateDirectory(Path.GetDirectoryName(finalTargetPath) ?? fullInstallPath);

                                            if (File.Exists(finalTargetPath))
                                            {
                                                string backupPath = finalTargetPath + ".bak";
                                                File.Replace(patchedTempPath, finalTargetPath, backupPath, ignoreMetadataErrors: true);
                                                if (File.Exists(backupPath))
                                                {
                                                    try
                                                    {
                                                        File.Delete(backupPath);
                                                    }
                                                    catch { }
                                                }
                                            }
                                            else
                                            {
                                                File.Move(patchedTempPath, finalTargetPath);
                                            }
                                        }
                                        catch (Exception ex)
                                        {
                                            logger.Error(ex, $"Failed to apply xdelta3 patch for delta '{deltaTempPath}'.");
                                            throw;
                                        }
                                    }
                                    finally
                                    {
                                        try
                                        {
                                            if (File.Exists(patchedTempPath))
                                            {
                                                File.Delete(patchedTempPath);
                                            }
                                        }
                                        catch { }
                                        sfcExtractSemaphore.Release();
                                        targetLock.Release();

                                        ReportProgress();
                                    }
                                }, token);

                                patchingTasks.Add(patchTask);
                            }

                            await Task.WhenAll(patchingTasks).ConfigureAwait(false);
                        }
                        finally
                        {
                            foreach (var semaphore in targetFileSemaphores.Values)
                            {
                                try { semaphore.Dispose(); } catch { }
                            }

                            reportingCts.Cancel();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            token.ThrowIfCancellationRequested();


            //
            // STEP 6: Cleanup
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

                if (Directory.Exists(tempDir))
                {
                    if (!token.IsCancellationRequested)
                    {
                        try
                        {
                            Directory.Delete(tempDir, true);
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(ex, $"Failed to recursively delete temporary directory {tempDir} after successful download.");
                        }
                    }
                    else
                    {
                        logger.Info($"Download was canceled. Retaining temporary directory: {tempDir} for resume.");
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"An error occurred during final cleanup.");
            }
        }

        public async Task Install(DownloadManagerData.Download taskData)
        {
            gracefulInstallerCTS = new CancellationTokenSource();
            userCancelCTS = new();
            var linkedCTS = CancellationTokenSource.CreateLinkedTokenSource(
                gracefulInstallerCTS.Token,
                userCancelCTS.Token
            );

            var sw = Stopwatch.StartNew();
            var gameTitle = taskData.name;
            GameTitleTB.Text = gameTitle;
            DescriptionTB.Text = $"{LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteLoadingLabel)}";
            taskData.status = DownloadStatus.Running;

            var tempReporterCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCTS.Token);
            var tempReporter = Task.Run(async () =>
            {
                while (!tempReporterCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(500, tempReporterCts.Token);
                        if (!tempReporterCts.Token.IsCancellationRequested)
                        {
                            _ = Application.Current.Dispatcher?.BeginInvoke((Action)(() =>
                            {
                                ElapsedTB.Text = sw.Elapsed.ToString(@"hh\:mm\:ss");
                            }));
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            }, tempReporterCts.Token);

            var settings = GogOssLibrary.GetSettings();
            var gameID = taskData.gameID;
            var downloadProperties = taskData.downloadProperties;
            bool downloadSpeedInBits = false;
            if (settings.DisplayDownloadSpeedInBits)
            {
                downloadSpeedInBits = true;
            }

            var allSecureLinks = new GogSecureLinks
            {
                mainSecureLinks = new(),
                inGameDependsSecureLinks = new()
            };

            if (taskData.downloadItemType == DownloadItemType.Game || taskData.downloadItemType == DownloadItemType.Dependency)
            {
                allSecureLinks.mainSecureLinks = await gogDownloadApi.GetSecureLinksForAllProducts(taskData);
            }

            var bigDepot = await GogOss.CreateNewBigDepot(taskData);
            var dependFoundInDepot = bigDepot.items.FirstOrDefault(i => !i.redistTargetDir.IsNullOrEmpty());
            if (dependFoundInDepot != null)
            {
                var dependData = new DownloadManagerData.Download
                {
                    gameID = "redist_v2",
                    downloadItemType = DownloadItemType.Dependency
                };
                dependData.downloadProperties = new DownloadProperties
                {
                    os = taskData.downloadProperties.os,
                };
                var dependsSecureLinks = await gogDownloadApi.GetSecureLinks(dependData);
                if (allSecureLinks.inGameDependsSecureLinks.Count == 0)
                {
                    if (dependsSecureLinks.Count > 0)
                    {
                        allSecureLinks.inGameDependsSecureLinks.Add("redist_v2", dependsSecureLinks);
                    }
                }
            }

            var originalDepotJson = Serialization.ToJson(bigDepot);

            bool foundPatch = false;
            GogDepot.Depot patchesDepot = new();
            if (taskData.downloadProperties.downloadAction == DownloadAction.Update && Xdelta.IsInstalled && taskData.downloadItemType == DownloadItemType.Game)
            {
                var metaManifest = await gogDownloadApi.GetGameMetaManifest(taskData);
                var installedAppList = GogOssLibrary.GetInstalledAppList();
                if (installedAppList.ContainsKey(gameID))
                {
                    var oldBuild = installedAppList[gameID].build_id;
                    var newBuild = taskData.downloadProperties.buildId;
                    if (oldBuild != newBuild)
                    {
                        var patchManifest = await gogDownloadApi.GetGogPatchMetaManifest(gameID, oldBuild, newBuild);
                        if (!patchManifest.errorDisplayed)
                        {
                            foundPatch = true;
                            var productIds = new List<string> { taskData.gameID };
                            if (taskData.downloadProperties.extraContent != null
                                && taskData.downloadProperties.extraContent.Count > 0)
                            {
                                productIds.AddRange(taskData.downloadProperties.extraContent);
                            }
                            var chosenlanguage = taskData.downloadProperties.language;
                            if (string.IsNullOrEmpty(chosenlanguage))
                            {
                                chosenlanguage = patchManifest.languages.FirstOrDefault();
                            }
                            foreach (var depot in patchManifest.depots)
                            {
                                if (depot.languages.Count == 0 || depot.languages.Contains(chosenlanguage) || depot.languages.Contains("*"))
                                {
                                    if (productIds.Contains(depot.productId))
                                    {
                                        var depotManifest = await gogDownloadApi.GetDepotInfo(depot.manifest, taskData, metaManifest.version,
                                                                                              true);
                                        if (depotManifest.depot.items.Count > 0)
                                        {
                                            foreach (var depotItem in depotManifest.depot.items)
                                            {
                                                depotItem.product_id = depot.productId;
                                                patchesDepot.items.Add(depotItem);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (!Xdelta.IsInstalled)
                {
                    logger.Warn("Xdelta3 isn't installed, so patching isn't possible.");
                }
            }

            if (foundPatch)
            {
                allSecureLinks.patchSecureLinks = await gogDownloadApi.GetSecureLinksForAllProducts(taskData, true);
            }

            long lastNetworkBytes = Interlocked.Read(ref resumeInitialNetworkBytes);
            long lastDiskBytes = Interlocked.Read(ref resumeInitialDiskBytes);
            TimeSpan lastStopwatchElapsed = TimeSpan.Zero;


            // Remove duplicates (sometimes can appear with dlcs, but who knows if accidently can happen...)
            if (bigDepot.items.Count > 0)
            {
                var seenPathsItems = new HashSet<string>();
                bigDepot.items.RemoveAll(i => !string.IsNullOrEmpty(i.path) && !seenPathsItems.Add(i.path));
            }
            if (bigDepot.files.Count > 0)
            {
                var seenPathsFiles = new HashSet<string>();
                bigDepot.files.RemoveAll(i => !string.IsNullOrEmpty(i.path) && !seenPathsFiles.Add(i.path));
            }


            // Remove old files not available in new update
            string tempPath = Path.Combine(taskData.fullInstallPath, ".Downloader_temp");
            string resumeStatePath = Path.Combine(tempPath, "resume-state.json");
            if (Directory.Exists(taskData.fullInstallPath) && !foundPatch && taskData.downloadProperties.downloadAction == DownloadAction.Update && !File.Exists(resumeStatePath))
            {
                var installedAppList = GogOssLibrary.GetInstalledAppList();
                var oldBigDepot = await GogOss.GetInstalledBigDepot(installedAppList[gameID], gameID);

                var allGameFiles = Directory.EnumerateFiles(taskData.fullInstallPath, "*.*", SearchOption.AllDirectories);

                var newItemsMap = bigDepot.items
                    .Where(i => !string.IsNullOrEmpty(i.path))
                    .ToDictionary(i => i.path, i => i);

                var oldItemsMap = oldBigDepot.items
                    .Where(i => !string.IsNullOrEmpty(i.path))
                    .ToDictionary(i => i.path, i => i);

                var options = new ParallelOptions { MaxDegreeOfParallelism = CommonHelpers.CpuThreadsNumber - 1 };

                Parallel.ForEach(allGameFiles, options, gameFile =>
                {
                    var newGameFile = gameFile;
                    if (taskData.downloadItemType == DownloadItemType.Overlay)
                    {
                        newGameFile = gameFile + ".zip";
                    }
                    string relativePath = RelativePath.Get(taskData.fullInstallPath, newGameFile);
                    if (oldItemsMap.ContainsKey(relativePath) && !newItemsMap.ContainsKey(relativePath))
                    {
                        if (File.Exists(gameFile))
                        {
                            File.Delete(gameFile);
                        }
                    }
                });
            }

            if (taskData.downloadProperties.downloadAction == DownloadAction.Repair)
            {
                if (File.Exists(resumeStatePath))
                {
                    File.Delete(resumeStatePath);
                }
            }


            // Search for depends
            if (taskData.downloadItemType == DownloadItemType.Game && taskData.depends.Count == 0)
            {
                var depends = new List<string>();
                var gameMetaManifest = await gogDownloadApi.GetGameMetaManifest(taskData);
                if (gameMetaManifest.scriptInterpreter)
                {
                    depends.Add("ISI");
                }
                if (gameMetaManifest.dependencies.Count > 0)
                {
                    taskData.depends = gameMetaManifest.dependencies;
                }
                if (gameMetaManifest.version == 1)
                {
                    foreach (var dependv1 in gameMetaManifest.depots)
                    {
                        if (!dependv1.redist.IsNullOrEmpty() && dependv1.targetDir.IsNullOrEmpty())
                        {
                            taskData.depends.Add(dependv1.redist);
                        }
                    }
                }
                foreach (var depend in taskData.depends)
                {
                    depends.AddMissing(depend);
                }
                if (depends.Count > 0)
                {
                    foreach (var depend in depends.ToList())
                    {
                        var dependManifest = await GogDownloadApi.GetRedistInfo(depend);
                        var dependInstallData = new DownloadManagerData.Download
                        {
                            gameID = depend,
                            downloadItemType = DownloadItemType.Dependency,
                            name = dependManifest.readableName,
                            status = DownloadStatus.Queued
                        };
                        var dependDownloadPath = Path.Combine(GogOss.DependenciesInstallationPath, "__redist", depend);
                        if (Directory.Exists(dependDownloadPath))
                        {
                            var dependExePath = Path.Combine(GogOss.DependenciesInstallationPath, dependManifest.executable.path);
                            if (File.Exists(dependExePath))
                            {
                                depends.Remove(depend);
                                continue;
                            }
                        }
                        var dependInfo = await gogDownloadApi.GetGameMetaManifest(dependInstallData);
                        if (dependInfo.executable.path.IsNullOrEmpty())
                        {
                            depends.Remove(depend);
                            continue;
                        }
                        var dependSize = await GogOss.CalculateGameSize(dependInstallData);
                        dependInstallData.downloadSizeNumber = dependSize.download_size;
                        dependInstallData.installSizeNumber = dependSize.disk_size;
                        if (dependInstallData.downloadSizeNumber != 0)
                        {
                            var wantedDependItem = downloadManagerData.downloads.FirstOrDefault(item => item.gameID == depend);
                            if (wantedDependItem == null)
                            {
                                downloadManagerData.downloads.Insert(0, dependInstallData);
                            }
                        }
                    }
                    SaveData();
                }
            }
            tempReporterCts.Cancel();
            try
            {
                await tempReporter;
            }
            catch (OperationCanceledException) { }
            DescriptionTB.Text = "";


            bool stopContinue = false;
            // Stop continuing if no links or files
            if (taskData.downloadItemType == DownloadItemType.Game || taskData.downloadItemType == DownloadItemType.Dependency)
            {
                if (allSecureLinks.mainSecureLinks.Count == 0
                    || (foundPatch && allSecureLinks.patchSecureLinks.Count == 0)
                    || (dependFoundInDepot != null && allSecureLinks.inGameDependsSecureLinks.Count == 0))
                {
                    stopContinue = true;
                }
            }

            if (bigDepot.files.Count == 0 && bigDepot.items.Count == 0)
            {
                stopContinue = true;
            }

            if (stopContinue)
            {
                taskData.status = DownloadStatus.Error;
                downloadsChanged = true;
                await DoNextJobInQueue();
                return;
            }

            // Verify and repair files
            if (Directory.Exists(taskData.fullInstallPath) && !File.Exists(resumeStatePath) && taskData.downloadItemType == DownloadItemType.Game)
            {
                if (taskData.downloadProperties.downloadAction != DownloadAction.Install)
                {
                    var allFiles = Directory.EnumerateFiles(taskData.fullInstallPath, "*.*", SearchOption.AllDirectories).ToList();
                    int countFiles = allFiles.Count;

                    if (countFiles > 0)
                    {
                        DescriptionTB.Text = LocalizationManager.Instance.GetString(LOC.CommonVerifying);
                        int verifiedFiles = 0;
                        long totalBytesRead = 0;

                        if (bigDepot.items.Count > 0 || bigDepot.files.Count > 0 || patchesDepot.items.Count > 0)
                        {
                            var itemsMap = bigDepot.items.Where(i => !string.IsNullOrEmpty(i.path) && i.chunks?.Sum(c => (long)c.size) > 0)
                                                         .ToDictionary(i => i.path, i => i);

                            var filesMap = bigDepot.files.Where(f => !string.IsNullOrEmpty(f.path) && f.size > 0)
                                                         .ToDictionary(f => f.path, f => f);

                            var patchesMap = patchesDepot.items.Where(p => !string.IsNullOrEmpty(p.path_source))
                                                               .ToDictionary(p => p.path_source, p => p);

                            var perFileProgress = new Progress<int>(bytes =>
                            {
                                Interlocked.Add(ref totalBytesRead, bytes);
                            });

                            var reporterCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCTS.Token);

                            var reporter = Task.Run(async () =>
                            {
                                long lastUiUpdate = 0;
                                long previousBytes = 0;
                                var swDelta = Stopwatch.StartNew();

                                while (!reporterCts.Token.IsCancellationRequested)
                                {
                                    try
                                    {
                                        await Task.Delay(500, reporterCts.Token);

                                        long currentBytes = Interlocked.Read(ref totalBytesRead);
                                        long deltaBytes = currentBytes - previousBytes;
                                        previousBytes = currentBytes;

                                        double elapsedSec = swDelta.Elapsed.TotalSeconds;
                                        swDelta.Restart();

                                        long now = Stopwatch.GetTimestamp();

                                        if (now - lastUiUpdate >= TimeSpan.FromMilliseconds(500).Ticks)
                                        {
                                            lastUiUpdate = now;
                                            _ = Application.Current.Dispatcher?.BeginInvoke((Action)(() =>
                                            {
                                                if (reporterCts.Token.IsCancellationRequested || linkedCTS.Token.IsCancellationRequested)
                                                    return;

                                                DescriptionTB.Text = $"{LocalizationManager.Instance.GetString(LOC.CommonVerifying)} ({verifiedFiles}/{countFiles})";
                                                ElapsedTB.Text = sw.Elapsed.ToString(@"hh\:mm\:ss");
                                                DiskSpeedTB.Text = CommonHelpers.FormatSize(deltaBytes / elapsedSec, "B") + "/s";
                                            }));
                                        }
                                    }
                                    catch (TaskCanceledException)
                                    {
                                        break;
                                    }
                                }
                            }, reporterCts.Token);

                            try
                            {
                                await Task.Run(() =>
                                {
                                    foreach (var file in allFiles)
                                    {
                                        linkedCTS.Token.ThrowIfCancellationRequested();

                                        try
                                        {
                                            var newFile = file;
                                            string relativePath = RelativePath.Get(taskData.fullInstallPath, newFile);

                                            if (patchesDepot.items.Count > 0)
                                            {
                                                if (patchesDepot.items.Count > 0 && patchesMap.TryGetValue(relativePath, out var searchedItem))
                                                {
                                                    string correctChecksum = searchedItem.md5_source;
                                                    string calculatedChecksum = Helpers.GetMD5(file, perFileProgress);
                                                    if (calculatedChecksum != null &&
                                                                !string.Equals(calculatedChecksum, correctChecksum,
                                                                               StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        try
                                                        {
                                                            File.Delete(file);
                                                        }
                                                        catch (Exception) { }
                                                        patchesDepot.items.Remove(searchedItem);
                                                        GogDepot.Item bigDepotItem = bigDepot.items.FirstOrDefault(i =>
                                                        i.path == searchedItem.path_source);
                                                        if (bigDepotItem != null)
                                                        {
                                                            patchesDepot.items.Add(bigDepotItem);
                                                        }
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                if (itemsMap.TryGetValue(relativePath, out var searchedItem))
                                                {
                                                    string checksumType = "md5";
                                                    string correctChecksum = "";

                                                    if (searchedItem.chunks != null && searchedItem.chunks.Count == 1)
                                                    {
                                                        correctChecksum = searchedItem.chunks[0].md5;
                                                    }

                                                    if (correctChecksum.IsNullOrEmpty())
                                                    {
                                                        correctChecksum = searchedItem.md5;
                                                    }

                                                    if (correctChecksum.IsNullOrEmpty())
                                                    {
                                                        correctChecksum = searchedItem.sha256;
                                                        checksumType = "sha256";
                                                    }

                                                    if (!string.IsNullOrEmpty(correctChecksum))
                                                    {
                                                        try
                                                        {
                                                            string calculatedChecksum = checksumType switch
                                                            {
                                                                "md5" => Helpers.GetMD5(file, perFileProgress),
                                                                "sha256" => Helpers.GetSHA256(file, perFileProgress),
                                                                _ => null
                                                            };

                                                            if (calculatedChecksum != null &&
                                                                !string.Equals(calculatedChecksum, correctChecksum, StringComparison.OrdinalIgnoreCase))
                                                            {
                                                                try
                                                                {
                                                                    File.Delete(file);
                                                                }
                                                                catch (Exception) { }
                                                            }
                                                            else
                                                            {
                                                                bigDepot.items.Remove(searchedItem);
                                                            }
                                                        }
                                                        catch (Exception hashEx)
                                                        {
                                                            logger.Warn(hashEx, "");
                                                        }
                                                    }
                                                }

                                                if (filesMap.TryGetValue(relativePath, out var depotFile))
                                                {
                                                    string correctMd5 = depotFile.hash;
                                                    if (!string.IsNullOrEmpty(correctMd5))
                                                    {
                                                        try
                                                        {
                                                            string calculatedMd5 = Helpers.GetMD5(file, perFileProgress);

                                                            if (calculatedMd5 != null &&
                                                                !string.Equals(calculatedMd5, correctMd5, StringComparison.OrdinalIgnoreCase))
                                                            {
                                                                try
                                                                {
                                                                    File.Delete(file);
                                                                }
                                                                catch (Exception) { }
                                                            }
                                                            else
                                                            {
                                                                bigDepot.files.Remove(depotFile);
                                                            }
                                                        }
                                                        catch (Exception hashEx)
                                                        {
                                                            logger.Warn(hashEx, "");
                                                        }
                                                    }
                                                }
                                            }
                                            Interlocked.Increment(ref verifiedFiles);
                                        }
                                        catch (OperationCanceledException) { throw; }
                                        catch (Exception)
                                        {
                                            Interlocked.Increment(ref verifiedFiles);
                                        }
                                    }
                                }, linkedCTS.Token);
                            }
                            catch (OperationCanceledException)
                            {

                            }
                            finally
                            {
                                Interlocked.Exchange(ref verifiedFiles, countFiles);
                                reporterCts.Cancel();
                                try
                                {
                                    await reporter;
                                }
                                catch (OperationCanceledException) { }
                                DescriptionTB.Text = $"{LocalizationManager.Instance.GetString(LOC.CommonVerifying)} ({verifiedFiles}/{countFiles})";
                                ElapsedTB.Text = sw.Elapsed.ToString(@"hh\:mm\:ss");
                            }
                        }
                    }
                }
            }

            DescriptionTB.Text = "";
            if (patchesDepot.items.Count > 0)
            {
                bigDepot.items = patchesDepot.items;
            }

            progress = new Progress<ProgressData>(p =>
            {
                taskData.downloadSizeNumber = p.TotalCompressedBytes;
                taskData.installSizeNumber = p.TotalBytes;
                double dt = (sw.Elapsed - lastStopwatchElapsed).TotalSeconds;
                if (dt < 1 && !p.FinalReport)
                {
                    return;
                }

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
                DiskSpeedTB.Text = CommonHelpers.FormatSize(rawDiskSpeed, "B") + "/s";
                EtaTB.Text = eta;
                ElapsedTB.Text = sw.Elapsed.ToString(@"hh\:mm\:ss");

                var item = downloadManagerData.downloads.FirstOrDefault(x => x.gameID == gameID);
                if (item != null)
                {
                    item.progress = p.TotalBytes > 0 ? (double)p.DiskBytes / p.TotalBytes * 100 : 0;
                    if (item.status != DownloadStatus.Running)
                    {
                        item.status = DownloadStatus.Running;
                    }
                    if (p.TotalCompressedBytes == p.NetworkBytes)
                    {
                        switch (downloadProperties.downloadAction)
                        {
                            case DownloadAction.Install:
                                DescriptionTB.Text = LocalizationManager.Instance.GetString(LOC.CommonFinishingInstallation);
                                break;
                            case DownloadAction.Update:
                                DescriptionTB.Text = LocalizationManager.Instance.GetString(LOC.CommonFinishingUpdate);
                                break;
                            case DownloadAction.Repair:
                                DescriptionTB.Text = LocalizationManager.Instance.GetString(LOC.CommonFinishingRepair);
                                break;
                            default:
                                break;
                        }
                    }
                    item.downloadedNumber = p.NetworkBytes;
                }
            });
            try
            {
                if (downloadProperties.maxWorkers == 0)
                {
                    downloadProperties.maxWorkers = CommonHelpers.CpuThreadsNumber;
                }
                var preferredCdn = settings.PreferredCdn;
                var preferredCdnString = PreferredCdn.GetCdnDict()[preferredCdn];

                if (downloadProperties.downloadAction != DownloadAction.Update)
                {
                    DescriptionTB.Text = LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteDownloadingLabel);
                }
                else
                {
                    DescriptionTB.Text = LocalizationManager.Instance.GetString(LOC.CommonDownloadingUpdate);
                }

                logger.Debug($"Downloading {taskData.name} ({taskData.gameID}) to {taskData.downloadProperties.installPath} ...");
                if (taskData.downloadItemType == DownloadItemType.Game || taskData.downloadItemType == DownloadItemType.Dependency)
                {
                    await DownloadGamesAndDepends(linkedCTS.Token, bigDepot, taskData.fullInstallPath, allSecureLinks, downloadProperties.maxWorkers, preferredCdn: preferredCdnString);
                }
                else
                {
                    await DownloadNonGames(linkedCTS.Token, bigDepot, taskData.fullInstallPath, downloadProperties.maxWorkers, taskData.downloadItemType);
                }


                if (taskData.downloadItemType == DownloadItemType.Game)
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
                    var dependencies = installedGameInfo.Dependencies;
                    if (taskData.depends.Count > 0)
                    {
                        foreach (var depend in taskData.depends)
                        {
                            dependencies.Add(depend);
                        }
                    }
                    Game game = new();
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
                    var installedDepotPath = Path.Combine(installedGameInfo.install_path, ".manifest_oss");
                    Directory.CreateDirectory(installedDepotPath);
                    var installedDepotFile = Path.Combine(installedDepotPath, "bigDepot.json");
                    if (File.Exists(installedDepotFile))
                    {
                        File.Delete(installedDepotFile);
                    }
                    File.WriteAllText(installedDepotFile, originalDepotJson);
                    GogOss.AddToHeroicInstalledList(installedGameInfo, gameID, taskData.installSizeNumber);
                    GogOssLibrary.Instance.installedAppListModified = true;
                }
                else if (taskData.downloadItemType == DownloadItemType.Overlay)
                {
                    var overlayInstalledInfo = new OverlayInstalled()
                    {
                        install_path = taskData.fullInstallPath,
                        platform = downloadProperties.os,
                        overlay_version = bigDepot.overlayVersion,
                        web_version = bigDepot.webVersion
                    };
                    var installedDepotPath = Path.Combine(overlayInstalledInfo.install_path, ".manifest_oss");
                    Directory.CreateDirectory(installedDepotPath);
                    var installedDepotFile = Path.Combine(installedDepotPath, "bigDepot.json");
                    if (File.Exists(installedDepotFile))
                    {
                        File.Delete(installedDepotFile);
                    }
                    File.WriteAllText(installedDepotFile, originalDepotJson);
                    var overlayInstalledFilePath = Path.Combine(GogOssLibrary.Instance.GetPluginUserDataPath(), "overlay_installed.json");
                    File.WriteAllText(overlayInstalledFilePath, Serialization.ToJson(overlayInstalledInfo, true));
                }

                taskData.status = DownloadStatus.Completed;
                taskData.progress = 100.0;
                DateTimeOffset now = DateTime.UtcNow;
                taskData.completedTime = now.ToUnixTimeSeconds();
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
                if (ex is not OperationCanceledException)
                {
                    logger.Error(ex, "");
                    taskData.status = DownloadStatus.Error;
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

        private async void CancelDownloadBtn_Click(object sender, RoutedEventArgs e)
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
                            userCancelCTS?.Cancel();
                            userCancelCTS?.Dispose();
                        }

                        const int maxRetries = 5;
                        int delayMs = 500;
                        var tempDir = Path.Combine(selectedRow.fullInstallPath, ".Downloader_temp");
                        string resumeStatePath = Path.Combine(tempDir, "resume-state.json");
                        for (int i = 0; i < maxRetries; i++)
                        {
                            try
                            {
                                if (selectedRow.downloadProperties.downloadAction == DownloadAction.Install)
                                {
                                    if (Directory.Exists(selectedRow.fullInstallPath))
                                    {
                                        Directory.Delete(selectedRow.fullInstallPath, true);
                                    }
                                }
                                else
                                {
                                    if (File.Exists(resumeStatePath))
                                    {
                                        File.Delete(resumeStatePath);
                                    }
                                }
                                break;
                            }
                            catch (Exception rex)
                            {
                                if (i < maxRetries - 1)
                                {
                                    await Task.Delay(delayMs);
                                    delayMs *= 2;
                                }
                                else
                                {
                                    var itemToRemove = selectedRow.fullInstallPath;
                                    if (selectedRow.downloadProperties.downloadAction != DownloadAction.Install)
                                    {
                                        itemToRemove = resumeStatePath;
                                    }
                                    logger.Warn(rex, $"Can't remove {itemToRemove}. Please try removing manually.");
                                    break;
                                }
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
