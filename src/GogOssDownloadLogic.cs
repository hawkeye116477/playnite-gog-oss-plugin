using CliWrap;
using CommonPlugin;
using CommonPlugin.Enums;
using GogOssLibraryNS.Enums;
using GogOssLibraryNS.Models;
using GogOssLibraryNS.Services;
using Linguini.Shared.Types.Bundle;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using UnifiedDownloadManagerApiNS;
using UnifiedDownloadManagerApiNS.Interfaces;
using UnifiedDownloadManagerApiNS.Models;

namespace GogOssLibraryNS
{
    public class GogOssDownloadLogic : IUnifiedDownloadLogic
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private IPlayniteAPI playniteAPI = API.Instance;
        private static readonly RetryHandler retryHandler = new(new HttpClientHandler());
        private static readonly HttpClient client = new HttpClient(retryHandler);
        public GogDownloadApi gogDownloadApi = new();
        public IProgress<ProgressData> progress { get; set; }
        private long resumeInitialDiskBytes = 0;
        private long resumeInitialNetworkBytes = 0;

        public async Task OnCancelDownload(UnifiedDownload downloadTask)
        {
            var gameID = downloadTask.gameID;

            const int maxRetries = 5;
            int delayMs = 500;
            var tempDir = Path.Combine(downloadTask.fullInstallPath, ".Downloader_temp");
            string resumeStatePath = Path.Combine(tempDir, "resume-state.json");
            var matchingPluginTask = GogOssLibrary.Instance.pluginDownloadData.downloads.FirstOrDefault(t => t.gameID == gameID);
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (matchingPluginTask.downloadProperties.downloadAction == DownloadAction.Install)
                    {
                        if (Directory.Exists(downloadTask.fullInstallPath))
                        {
                            Directory.Delete(downloadTask.fullInstallPath, true);
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
                        var itemToRemove = downloadTask.fullInstallPath;
                        if (matchingPluginTask.downloadProperties.downloadAction != DownloadAction.Install)
                        {
                            itemToRemove = resumeStatePath;
                        }
                        logger.Warn(rex, $"Can't remove {itemToRemove}. Please try removing manually.");
                        break;
                    }
                }
            }
        }

        public static bool CheckIfUdmInstalled()
        {
            var playniteAPI = API.Instance;
            bool installed = playniteAPI.Addons.Plugins.Any(plugin => plugin.Id.Equals(UnifiedDownloadManagerSharedProperties.Id));
            if (!installed)
            {
                var options = new List<MessageBoxOption>
                {
                    new MessageBoxOption(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteInstallGame)),
                    new MessageBoxOption(LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteOkLabel)),
                };
                var result = playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonLauncherNotInstalled, new Dictionary<string, IFluentType> { ["launcherName"] = (FluentString)"Unified Download Manager" }), "GOG OSS library integration", MessageBoxImage.Information, options);
                if (result == options[0])
                {
                    Playnite.Commands.GlobalCommands.NavigateUrl("playnite://playnite/installaddon/UnifiedDownloadManager");
                }
            }
            return installed;
        }

        public Task OnRemoveDownloadEntry(UnifiedDownload downloadTask)
        {
            var matchingPluginTask = GogOssLibrary.Instance.pluginDownloadData.downloads.FirstOrDefault(t => t.gameID == downloadTask.gameID);
            if (matchingPluginTask != null)
            {
                GogOssLibrary.Instance.pluginDownloadData.downloads.Remove(matchingPluginTask);
                GogOssLibrary.Instance.SaveDownloadData();
            }
            return Task.CompletedTask;
        }

        public void OpenDownloadPropertiesWindow(UnifiedDownload selectedEntry)
        {
            var window = playniteAPI.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = false,
            });
            var matchingPluginTask = GogOssLibrary.Instance.pluginDownloadData.downloads.FirstOrDefault(t => t.gameID == selectedEntry.gameID);
            window.Title = selectedEntry.name + " — " + LocalizationManager.Instance.GetString(LOC.CommonDownloadProperties);
            window.DataContext = matchingPluginTask;
            window.Content = new GogOssDownloadPropertiesView();
            window.Owner = playniteAPI.Dialogs.GetCurrentAppWindow();
            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.ShowDialog();
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

        public async Task AddTasks(List<DownloadManagerData.Download> downloadTasks, bool silently = false)
        {
            var unifiedTasks = new List<UnifiedDownload>();
            UnifiedDownloadManagerApi unifiedDownloadManagerApi = new();
            var allDownloads = unifiedDownloadManagerApi.GetAllDownloads();
            List<string> downloadItemsAlreadyAdded = new();
            foreach (var downloadTask in downloadTasks)
            {
                bool completedDownload = true;
                var wantedUnifiedItem = unifiedDownloadManagerApi.GetTask(downloadTask.gameID, GogOssLibrary.Instance.Id.ToString());
                if (wantedUnifiedItem != null)
                {
                    if (wantedUnifiedItem.status != UnifiedDownloadStatus.Completed)
                    {
                        completedDownload = false;
                    }
                }
                if (completedDownload)
                {
                    var wantedPluginItem = GogOssLibrary.Instance.pluginDownloadData.downloads.FirstOrDefault(item => item.gameID == downloadTask.gameID);
                    if (wantedPluginItem != null)
                    {
                        GogOssLibrary.Instance.pluginDownloadData.downloads.Remove(wantedPluginItem);
                        wantedPluginItem = GogOssLibrary.Instance.pluginDownloadData.downloads.FirstOrDefault(item => item.gameID == downloadTask.gameID);
                    }
                    if (wantedUnifiedItem != null)
                    {
                        unifiedDownloadManagerApi.RemoveTask(wantedUnifiedItem);
                        wantedUnifiedItem = unifiedDownloadManagerApi.GetTask(downloadTask.gameID, GogOssLibrary.Instance.Id.ToString());
                    }
                }
                if (wantedUnifiedItem != null)
                {
                    downloadItemsAlreadyAdded.Add(wantedUnifiedItem.name);
                    continue;
                }

                // Search for depends
                var matchingPluginTask = downloadTask;
                if (matchingPluginTask.downloadItemType == DownloadItemType.Game && matchingPluginTask.depends.Count == 0)
                {
                    var depends = new List<string>();
                    var gameMetaManifest = await gogDownloadApi.GetGameMetaManifest(matchingPluginTask);
                    if (gameMetaManifest.scriptInterpreter)
                    {
                        depends.Add("ISI");
                    }
                    if (gameMetaManifest.dependencies.Count > 0)
                    {
                        matchingPluginTask.depends = gameMetaManifest.dependencies;
                    }
                    if (gameMetaManifest.version == 1)
                    {
                        foreach (var dependv1 in gameMetaManifest.depots)
                        {
                            if (!dependv1.redist.IsNullOrEmpty() && dependv1.targetDir.IsNullOrEmpty())
                            {
                                matchingPluginTask.depends.Add(dependv1.redist);
                            }
                        }
                    }
                    foreach (var depend in matchingPluginTask.depends)
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
                            dependInstallData.downloadProperties = new();
                            dependInstallData.downloadProperties.maxWorkers = matchingPluginTask.downloadProperties.maxWorkers;
                            dependInstallData.downloadProperties.os = matchingPluginTask.downloadProperties.os;
                            dependInstallData.downloadProperties.installPath = GogOss.DependenciesInstallationPath;
                            dependInstallData.fullInstallPath = Path.Combine(GogOss.DependenciesInstallationPath, "__redist", depend);

                            if (dependInstallData.downloadSizeNumber != 0)
                            {
                                GogOssLibrary.Instance.pluginDownloadData.downloads.Add(dependInstallData);
                                var wantedDependItem = allDownloads.FirstOrDefault(item => item.gameID == depend);
                                var wantedUnifiedTask = unifiedTasks.FirstOrDefault(item => item.gameID == depend);
                                if (wantedDependItem == null && wantedUnifiedTask == null)
                                {
                                    var unifiedDependTask = new UnifiedDownload
                                    {
                                        gameID = dependInstallData.gameID,
                                        name = dependInstallData.name,
                                        downloadSizeBytes = dependInstallData.downloadSizeNumber,
                                        installSizeBytes = dependInstallData.installSizeNumber,
                                        fullInstallPath = dependInstallData.fullInstallPath,
                                        pluginId = GogOssLibrary.Instance.Id.ToString(),
                                        sourceName = "GOG",
                                        addedTime = downloadTask.addedTime,
                                    };
                                    unifiedTasks.Add(unifiedDependTask);
                                }
                            }
                        }
                    }
                }

                // Add main tasks
                GogOssLibrary.Instance.pluginDownloadData.downloads.Add(downloadTask);
                var unifiedTask = new UnifiedDownload
                {
                    gameID = downloadTask.gameID,
                    name = downloadTask.name,
                    downloadSizeBytes = downloadTask.downloadSizeNumber,
                    installSizeBytes = downloadTask.installSizeNumber,
                    fullInstallPath = downloadTask.fullInstallPath,
                    pluginId = GogOssLibrary.Instance.Id.ToString(),
                    sourceName = "GOG",
                    addedTime = downloadTask.addedTime,
                };
                unifiedTasks.Add(unifiedTask);
            }

            await unifiedDownloadManagerApi.AddTasks(unifiedTasks);
            GogOssLibrary.Instance.SaveDownloadData();

            if (!silently && unifiedTasks.Count == 0)
            {
                if (downloadItemsAlreadyAdded.Count > 0)
                {
                    string downloadItemsAlreadyAddedCombined = downloadItemsAlreadyAdded[0];
                    if (downloadItemsAlreadyAdded.Count > 1)
                    {
                        downloadItemsAlreadyAddedCombined = string.Join(", ", downloadItemsAlreadyAdded.Select(item => item.ToString()));
                    }
                    playniteAPI.Dialogs.ShowMessage(LocalizationManager.Instance.GetString(LOC.CommonDownloadAlreadyExists, new Dictionary<string, IFluentType> { ["appName"] = (FluentString)downloadItemsAlreadyAddedCombined, ["count"] = (FluentNumber)downloadItemsAlreadyAdded.Count, ["pluginShortName"] = (FluentString)"Unified Download Manager" }), "", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task DownloadNonGames(CancellationToken token,
                                    GogDepot.Depot bigDepot,
                                    string fullInstallPath,
                                    int maxParallel,
                                    DownloadItemType downloadItemType,
                                    string appId = "",
                                    int bufferSize = 512 * 1024)
        {
            // STEP 0: Initial Setup
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, maxParallel * 2);

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", GogDownloadApi.UserAgent);

            var gogAccountClient = new GogAccountClient();
            var tokens = new TokenResponse.TokenResponsePart();
            if (downloadItemType == DownloadItemType.Extra && await gogAccountClient.GetIsUserLoggedIn())
            {
                tokens = gogAccountClient.LoadTokens();
            }

            using var downloadSemaphore = new SemaphoreSlim(maxParallel);
            var writeSemaphores = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

            // Producer-consumer channel
            var channel = Channel.CreateUnbounded<(string filePath, long length, string tempFilePath, long allocatedBytes, bool isCompressed, string hash)>(
                );

            var jobs = new HashSet<(string filePath, long size, string url, string hash)>();

            long totalCompressedSize = 0;
            long initialNetworkBytesLocal = 0;
            var fileExpectedSizes = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            var tempDir = Path.Combine(fullInstallPath, ".Downloader_temp");
            if (!Directory.Exists(tempDir))
            {
                if (!CommonHelpers.IsDirectoryWritable(tempDir))
                {
                    var tempFolderName = $"{appId}_GogOss";
                    tempDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "temp", tempFolderName);
                }
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
                string tempFilePath = null;

                try
                {
                    await downloadSemaphore.WaitAsync(token).ConfigureAwait(false);
                    slotAcquired = true;
                    Interlocked.Increment(ref activeDownloaders);
                    ReportProgress();

                    long compressedSize = job.size;

                    bool isCompressed = false;
                    if (downloadItemType == DownloadItemType.Overlay)
                    {
                        isCompressed = true;
                    }
                    string effectiveFilePath = job.filePath;
                    try
                    {
                        long resumeStartByte = 0;

                        if (downloadItemType is DownloadItemType.Extra or DownloadItemType.Tools)
                        {
                            using var headRequest = new HttpRequestMessage(HttpMethod.Head, job.url);
                            if (downloadItemType == DownloadItemType.Extra)
                            {
                                headRequest.Headers.Add("Authorization", $"Bearer {tokens.access_token}");
                            }
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
                            if (downloadItemType is DownloadItemType.Extra && headResponse.Content.Headers.ContentLength.HasValue)
                            {
                                long contentSize = headResponse.Content.Headers.ContentLength.Value;
                                totalCompressedSize = contentSize;
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
                                await channel.Writer.WriteAsync((job.filePath, resumeStartByte, tempFilePath, 0, isCompressed, job.hash), token).ConfigureAwait(false);
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

                        await channel.Writer.WriteAsync((effectiveFilePath, actualFileSize, tempFilePath, 0, isCompressed, job.hash), token).ConfigureAwait(false);
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
                        if (allocatedBytes > 0)
                        {
                            allocatedBytes = 0;
                        }
                        tempFilePath = null;
                        throw new Exception($"Failed to download file {effectiveFilePath}.", ex);
                    }
                }
                finally
                {
                    try
                    {
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
            // STEP 3: Consumer – Writer (Decompress and write to final file)
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
                                var finalPath = Path.Combine(fullInstallPath, item.filePath);
                                if (item.isCompressed)
                                {
                                    var tempExtractedPath = Path.Combine(tempDir, item.filePath);
                                    Directory.CreateDirectory(Path.GetDirectoryName(tempExtractedPath));
                                    using (var outFs = new FileStream(tempExtractedPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous))
                                    {
                                        using Stream sourceStream = new FileStream(item.tempFilePath!, FileMode.Open,
                                           FileAccess.Read, FileShare.Read, bufferSize,
                                           FileOptions.SequentialScan);
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
                                    try
                                    {
                                        if (File.Exists(item.tempFilePath))
                                        {
                                            File.Delete(item.tempFilePath);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.Warn(ex, $"Couldn't delete original zip file {item.tempFilePath} after extraction.");
                                    }
                                    item.tempFilePath = tempExtractedPath;
                                }

                                if (!CommonHelpers.IsDirectoryWritable(Path.GetFileName(Path.GetDirectoryName(finalPath))))
                                {
                                    var roboCopyArgs = new List<string>()
                                    {
                                        Path.GetDirectoryName(item.tempFilePath),
                                        Path.GetDirectoryName(finalPath),
                                        Path.GetFileName(item.tempFilePath),
                                        "/R:3",
                                        "/COPYALL"
                                    };
                                    var roboCopyCmd = Cli.Wrap("robocopy")
                                                         .WithArguments(roboCopyArgs);
                                    var proc = ProcessStarter.StartProcess("robocopy", roboCopyCmd.Arguments, true);
                                    proc.WaitForExit();
                                    ReportProgress();
                                }
                                else
                                {
                                    using var sourceStream = new FileStream(item.tempFilePath!, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
                                    using (var outFs = new FileStream(finalPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous))
                                    {
                                        int bytesRead;
                                        long totalWritten = 0;
                                        while ((bytesRead = await sourceStream.ReadAsync(consumerBuffer, 0, consumerBuffer.Length, token).ConfigureAwait(false)) > 0)
                                        {
                                            await outFs.WriteAsync(consumerBuffer, 0, bytesRead, token).ConfigureAwait(false);
                                            ReportProgress();
                                            totalWritten += bytesRead;
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
                                }

                                if (File.Exists(item.tempFilePath))
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
            var channel = Channel.CreateUnbounded<(string filePath, long offset, byte[] chunkBuffer, int length, string tempFilePath, long allocatedBytes, DepotFileType depotFileType, bool isCompressed, string chunkId)>(
                );

            var jobs = new HashSet<(string filePath, long offset, GogDepot.Chunk chunk, DepotFileType depotFileType, string productId)>();

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
                    DepotFileType depotFileType = DepotFileType.Game;

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
                        jobs.Add((filePath, 0, v1Chunk, depotFileType, file.product_id));
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
                    DepotFileType depotFileType = DepotFileType.Game;
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
                                    jobs.Add((sfcFilePath, sfcPos, chunk, depotFileType, kvp.Value.product_id));
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
                                    jobs.Add((sfcFilePath, sfcPos, chunk, depotFileType, kvp.Value.product_id));
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
                    DepotFileType depotFileType = DepotFileType.Game;

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
                        depotFileType = DepotFileType.Redist;
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
                        else if (depot.type == "DepotDiff")
                        {
                            depotFileType = DepotFileType.Patch;
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
                                    jobs.Add((filePath, pos, chunk, depotFileType, depot.product_id));
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
                                    jobs.Add((filePath, pos, chunk, depotFileType, depot.product_id));
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
                    bool isCompressed = !isV1 || job.depotFileType == DepotFileType.Redist;

                    var currentSecureLinksDict = new Dictionary<string, List<GogSecureLinks.FinalUrl>>();

                    if (job.depotFileType == DepotFileType.Patch)
                    {
                        currentSecureLinksDict = patchesLinks;
                    }
                    else if (job.depotFileType == DepotFileType.Redist)
                    {
                        currentSecureLinksDict = dependencyLinks;
                    }
                    else
                    {
                        currentSecureLinksDict = secureLinks;
                    }
                    var productId = job.productId;
                    if (job.depotFileType == DepotFileType.Redist)
                    {
                        productId = "redist_v2";
                    }
                    var currentSecureLinks = currentSecureLinksDict[productId];
                    var availableCdns = new List<GogSecureLinks.FinalUrl>(currentSecureLinks);

                    if (preferredCdn != "")
                    {
                        var preferredLink = availableCdns.FirstOrDefault(l => l.endpoint_name.Equals(preferredCdn, StringComparison.OrdinalIgnoreCase));
                        if (preferredLink != null && !preferredLink.formatted_url.IsNullOrEmpty())
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
                            if (isV1 && job.depotFileType != DepotFileType.Redist)
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
                                        await channel.Writer.WriteAsync((job.filePath, job.offset, null, (int)compressedSize, tempFilePath, 0, job.depotFileType, isCompressed, chunk.compressedMd5), token).ConfigureAwait(false);
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

                                await channel.Writer.WriteAsync((job.filePath, job.offset, chunkBuffer, offset, null, allocatedBytes, job.depotFileType, isCompressed, chunk.compressedMd5), token).ConfigureAwait(false);

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
                                await channel.Writer.WriteAsync((job.filePath, job.offset, null, (int)compressedSize, tempFilePath, 0, job.depotFileType, isCompressed, chunk.compressedMd5), token).ConfigureAwait(false);
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


        public async Task StartDownload(UnifiedDownload downloadTask)
        {
            UnifiedDownloadManagerApi unifiedDownloadManagerApi = new UnifiedDownloadManagerApi();
            var matchingPluginTask = GogOssLibrary.Instance.pluginDownloadData.downloads.FirstOrDefault(t => t.gameID == downloadTask.gameID);
            var wantedUnifiedTask = unifiedDownloadManagerApi.GetTask(downloadTask.gameID, GogOssLibrary.Instance.Id.ToString());
            var userCancelCTS = downloadTask.gracefulCts;
            var linkedCTS = CancellationTokenSource.CreateLinkedTokenSource(
                downloadTask.forcefulCts.Token,
                userCancelCTS.Token
            );

            var sw = Stopwatch.StartNew();
            var gameTitle = downloadTask.name;
            downloadTask.activity = $"{LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteLoadingLabel)}";
            downloadTask.status = UnifiedDownloadStatus.Running;

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
                                downloadTask.elapsed = sw.Elapsed;
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
            var gameID = downloadTask.gameID;
            var downloadProperties = matchingPluginTask.downloadProperties;

            var allSecureLinks = new GogSecureLinks
            {
                mainSecureLinks = new(),
                inGameDependsSecureLinks = new()
            };

            if (matchingPluginTask.downloadItemType == DownloadItemType.Game || matchingPluginTask.downloadItemType == DownloadItemType.Dependency)
            {
                allSecureLinks.mainSecureLinks = await gogDownloadApi.GetSecureLinksForAllProducts(matchingPluginTask);
            }

            var bigDepot = await GogOss.CreateNewBigDepot(matchingPluginTask);
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
                    os = matchingPluginTask.downloadProperties.os,
                };
                var dependsSecureLinks = await gogDownloadApi.GetSecureLinks(dependData);
                if (allSecureLinks.inGameDependsSecureLinks.Count == 0)
                {
                    if (dependsSecureLinks.Count > 0)
                    {
                        if (!allSecureLinks.inGameDependsSecureLinks.ContainsKey("redist_v2"))
                        {
                            allSecureLinks.inGameDependsSecureLinks.Add("redist_v2", dependsSecureLinks);
                        }
                    }
                }
            }

            var originalDepotJson = Serialization.ToJson(bigDepot);

            bool foundPatch = false;
            GogDepot.Depot patchesDepot = new();
            if (matchingPluginTask.downloadProperties.downloadAction == DownloadAction.Update && Xdelta.IsInstalled && matchingPluginTask.downloadItemType == DownloadItemType.Game)
            {
                var metaManifest = await gogDownloadApi.GetGameMetaManifest(matchingPluginTask);
                var installedAppList = GogOssLibrary.GetInstalledAppList();
                if (installedAppList.ContainsKey(gameID))
                {
                    var oldBuild = installedAppList[gameID].build_id;
                    var newBuild = matchingPluginTask.downloadProperties.buildId;
                    if (oldBuild != newBuild)
                    {
                        var patchManifest = await gogDownloadApi.GetGogPatchMetaManifest(gameID, oldBuild, newBuild);
                        if (!patchManifest.errorDisplayed)
                        {
                            foundPatch = true;
                            var productIds = new List<string> { matchingPluginTask.gameID };
                            if (matchingPluginTask.downloadProperties.extraContent != null
                                && matchingPluginTask.downloadProperties.extraContent.Count > 0)
                            {
                                productIds.AddRange(matchingPluginTask.downloadProperties.extraContent);
                            }
                            var chosenlanguage = matchingPluginTask.downloadProperties.language;
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
                                        var depotManifest = await gogDownloadApi.GetDepotInfo(depot.manifest, matchingPluginTask, metaManifest.version,
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
                allSecureLinks.patchSecureLinks = await gogDownloadApi.GetSecureLinksForAllProducts(matchingPluginTask, true);
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
            string tempPath = Path.Combine(matchingPluginTask.fullInstallPath, ".Downloader_temp");
            string resumeStatePath = Path.Combine(tempPath, "resume-state.json");
            if (Directory.Exists(matchingPluginTask.fullInstallPath) && !foundPatch && matchingPluginTask.downloadProperties.downloadAction == DownloadAction.Update && !File.Exists(resumeStatePath))
            {
                var installedAppList = GogOssLibrary.GetInstalledAppList();
                if (installedAppList.ContainsKey(gameID))
                {
                    var oldBigDepot = await GogOss.GetInstalledBigDepot(installedAppList[gameID], gameID);

                    var allGameFiles = Directory.EnumerateFiles(matchingPluginTask.fullInstallPath, "*.*", SearchOption.AllDirectories);

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
                        if (matchingPluginTask.downloadItemType == DownloadItemType.Overlay)
                        {
                            newGameFile = gameFile + ".zip";
                        }
                        string relativePath = RelativePath.Get(matchingPluginTask.fullInstallPath, newGameFile);
                        if (oldItemsMap.ContainsKey(relativePath) && !newItemsMap.ContainsKey(relativePath))
                        {
                            if (File.Exists(gameFile))
                            {
                                File.Delete(gameFile);
                            }
                        }
                    });
                }
            }

            if (matchingPluginTask.downloadProperties.downloadAction == DownloadAction.Repair)
            {
                if (File.Exists(resumeStatePath))
                {
                    File.Delete(resumeStatePath);
                }
            }

            tempReporterCts.Cancel();
            try
            {
                await tempReporter;
            }
            catch (OperationCanceledException) { }
            wantedUnifiedTask.activity = "";


            bool stopContinue = false;
            // Stop continuing if no links or files
            if (matchingPluginTask.downloadItemType == DownloadItemType.Game || matchingPluginTask.downloadItemType == DownloadItemType.Dependency)
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
                logger.Error("No files to download.");
                wantedUnifiedTask.status = UnifiedDownloadStatus.Error;
                return;
            }

            // Verify and repair files
            if (Directory.Exists(matchingPluginTask.fullInstallPath) && !File.Exists(resumeStatePath) && matchingPluginTask.downloadItemType == DownloadItemType.Game)
            {
                if (matchingPluginTask.downloadProperties.downloadAction != DownloadAction.Install)
                {
                    var allFiles = Directory.EnumerateFiles(matchingPluginTask.fullInstallPath, "*.*", SearchOption.AllDirectories).ToList();
                    int countFiles = allFiles.Count;

                    if (countFiles > 0)
                    {
                        wantedUnifiedTask.activity = LocalizationManager.Instance.GetString(LOC.CommonVerifying);
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

                                                wantedUnifiedTask.activity = $"{LocalizationManager.Instance.GetString(LOC.CommonVerifying)} ({verifiedFiles}/{countFiles})";
                                                wantedUnifiedTask.elapsed = sw.Elapsed;
                                                wantedUnifiedTask.diskWriteSpeedBytes = deltaBytes / elapsedSec;
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
                                            string relativePath = RelativePath.Get(wantedUnifiedTask.fullInstallPath, newFile);

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
                                wantedUnifiedTask.activity = $"{LocalizationManager.Instance.GetString(LOC.CommonVerifying)} ({verifiedFiles}/{countFiles})";
                                wantedUnifiedTask.elapsed = sw.Elapsed;
                            }
                        }
                    }
                }
            }

            wantedUnifiedTask.activity = "";
            if (patchesDepot.items.Count > 0)
            {
                bigDepot.items = patchesDepot.items;
            }

            progress = new Progress<ProgressData>(p =>
            {
                wantedUnifiedTask.downloadSizeBytes = p.TotalCompressedBytes;
                wantedUnifiedTask.installSizeBytes = p.TotalBytes;
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

                double eta = remaining > 0 ? remaining / speedForEta : 0;

                wantedUnifiedTask.downloadSpeedBytes = rawNetSpeed;
                wantedUnifiedTask.diskWriteSpeedBytes = rawDiskSpeed;
                wantedUnifiedTask.eta = TimeSpan.FromSeconds(eta);
                wantedUnifiedTask.elapsed = sw.Elapsed;


                var item = wantedUnifiedTask;
                if (item != null)
                {
                    var currentPercentProgress = p.TotalBytes > 0 ? (double)p.DiskBytes / p.TotalBytes * 100 : 0;
                    item.progress = currentPercentProgress;
                    if (item.status != UnifiedDownloadStatus.Running)
                    {
                        item.status = UnifiedDownloadStatus.Running;
                    }
                    if (p.TotalCompressedBytes == p.NetworkBytes)
                    {
                        switch (downloadProperties.downloadAction)
                        {
                            case DownloadAction.Install:
                                wantedUnifiedTask.activity = LocalizationManager.Instance.GetString(LOC.CommonFinishingInstallation);
                                break;
                            case DownloadAction.Update:
                                wantedUnifiedTask.activity = LocalizationManager.Instance.GetString(LOC.CommonFinishingUpdate);
                                break;
                            case DownloadAction.Repair:
                                wantedUnifiedTask.activity = LocalizationManager.Instance.GetString(LOC.CommonFinishingRepair);
                                break;
                            default:
                                break;
                        }
                    }
                    item.downloadedBytes = p.NetworkBytes;
                }
            });
            try
            {
                var maxWorkers = downloadProperties.maxWorkers;
                if (downloadProperties.maxWorkers == 0)
                {
                    maxWorkers = CommonHelpers.CpuThreadsNumber;
                }
                var preferredCdn = settings.PreferredCdn;
                var preferredCdnString = PreferredCdn.GetCdnDict()[preferredCdn];

                if (downloadProperties.downloadAction != DownloadAction.Update)
                {
                    wantedUnifiedTask.activity = LocalizationManager.Instance.GetString(LOC.ThirdPartyPlayniteDownloadingLabel);
                }
                else
                {
                    wantedUnifiedTask.activity = LocalizationManager.Instance.GetString(LOC.CommonDownloadingUpdate);
                }

                logger.Debug($"Downloading {wantedUnifiedTask.name} ({wantedUnifiedTask.gameID}) to {matchingPluginTask.downloadProperties.installPath} ...");
                if (matchingPluginTask.downloadItemType == DownloadItemType.Game || matchingPluginTask.downloadItemType == DownloadItemType.Dependency)
                {
                    await DownloadGamesAndDepends(linkedCTS.Token, bigDepot, wantedUnifiedTask.fullInstallPath, allSecureLinks, maxWorkers, preferredCdn: preferredCdnString);
                }
                else
                {
                    await DownloadNonGames(linkedCTS.Token, bigDepot, wantedUnifiedTask.fullInstallPath, maxWorkers, matchingPluginTask.downloadItemType, matchingPluginTask.gameID);
                }


                if (matchingPluginTask.downloadItemType == DownloadItemType.Game)
                {
                    var installedAppList = GogOssLibrary.GetInstalledAppList();
                    var installedGameInfo = new Installed
                    {
                        build_id = downloadProperties.buildId,
                        version = downloadProperties.version,
                        title = gameTitle,
                        platform = downloadProperties.os,
                        install_path = wantedUnifiedTask.fullInstallPath,
                        language = downloadProperties.language,
                        installed_DLCs = downloadProperties.extraContent
                    };
                    if (installedAppList.ContainsKey(gameID))
                    {
                        installedAppList.Remove(gameID);
                    }
                    var dependencies = installedGameInfo.Dependencies;
                    if (matchingPluginTask.depends.Count > 0)
                    {
                        foreach (var depend in matchingPluginTask.depends)
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
                    GogOssLibrary.Instance.installedAppListModified = true;
                }
                else if (matchingPluginTask.downloadItemType == DownloadItemType.Overlay)
                {
                    var overlayInstalledInfo = new OverlayInstalled()
                    {
                        install_path = wantedUnifiedTask.fullInstallPath,
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

                wantedUnifiedTask.activity = "";
                wantedUnifiedTask.status = UnifiedDownloadStatus.Completed;
                wantedUnifiedTask.progress = 100.0;
                DateTimeOffset now = DateTime.UtcNow;
                wantedUnifiedTask.completedTime = now.ToUnixTimeSeconds();
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException)
                {
                    logger.Error(ex, "");
                    wantedUnifiedTask.status = UnifiedDownloadStatus.Error;
                    wantedUnifiedTask.activity = "";
                }
            }
            finally
            {
                sw.Stop();
            }
        }
    }
}
