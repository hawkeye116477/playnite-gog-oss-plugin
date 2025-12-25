using Galaxy.Protocols.CommunicationService;
using GogOssLibraryNS.Models;
using Google.Protobuf;
using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GogOssLibraryNS
{
    public class GalaxyOverlay
    {
        private static ILogger logger = LogManager.GetLogger();
        public static OverlayInstalled GetInstalledInfo()
        {
            var overlayInstalledFilePath = Path.Combine(GogOssLibrary.Instance.GetPluginUserDataPath(), "overlay_installed.json");

            OverlayInstalled overlayInstalledInfo = new();
            if (File.Exists(overlayInstalledFilePath))
            {
                var overlayFileContent = File.ReadAllText(overlayInstalledFilePath);
                if (!overlayFileContent.IsNullOrWhiteSpace() && Serialization.TryFromJson<OverlayInstalled>(overlayFileContent, out var newOverlayInstalledJson))
                {
                    overlayInstalledInfo = newOverlayInstalledJson;
                }
            }
            return overlayInstalledInfo;
        }

        public static bool IsInstalled
        {
            get
            {
                var overlayInstalledInfo = GetInstalledInfo();
                if (!overlayInstalledInfo.install_path.IsNullOrEmpty() && Directory.Exists(overlayInstalledInfo.install_path))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public void CreateNeededDirectories()
        {
            string programDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var neededDirs = new List<string>
            {
                Path.Combine(programDataPath, "logs"),
                Path.Combine(programDataPath, "webcache")
            };
            foreach (var neededDir in neededDirs)
            {
                try
                {
                    Directory.CreateDirectory(neededDir);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "An error occured while creating needed directories for Galaxy Overlay");
                }
            }
        }


        public async Task<bool> NotifyComet(int gameProcessId)
        {
            try
            {
                var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", 9977);

                var request = new StartGameSessionRequest
                {
                    GamePid = (uint)gameProcessId,
                    OverlaySupport = StartGameSessionRequest.Types.OverlaySupport.Enabled,
                };
                byte[] payload = request.ToByteArray();

                var header = new Gog.Protocols.Pb.Header
                {
                    Size = (uint)payload.Length,
                    Oseq = 1,
                    Type = (uint)MessageType.StartGameSessionRequest,
                    Sort = (uint)MessageSort.MessageSort
                };
                var headerBytes = header.ToByteArray();

                byte[] frame = new byte[2 + headerBytes.Length + payload.Length];
                BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0, 2), (ushort)headerBytes.Length);

                Buffer.BlockCopy(headerBytes, 0, frame, 2, headerBytes.Length);
                Buffer.BlockCopy(payload, 0, frame, 2 + headerBytes.Length, payload.Length);

                NetworkStream stream = client.GetStream();
                await stream.WriteAsync(frame, 0, frame.Length);
                await stream.FlushAsync();
                client.Client.Shutdown(SocketShutdown.Send);
                client.Dispose();
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to notify Comet");
                return false;
            }

        }

        public async Task InitAndForwardPipes(int gamePid, Process overlayProcess, CancellationToken cancellationToken, int maxRetries = 10)
        {
            string pipeName = $"Galaxy-{gamePid}-CommunicationService-Overlay";

            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            bool connected = false;
            for (int i = 0; i < maxRetries; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    logger.Debug($"Waiting for overlay connection (try {i + 1}/{maxRetries})...");
                    await pipe.WaitForConnectionAsync(cancellationToken);
                    connected = true;
                    break;
                }
                catch (OperationCanceledException)
                {
                    logger.Warn("Pipe connection cancelled.");
                    return;
                }
                catch
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }

            if (!connected)
            {
                logger.Error("Failed to connect overlay to pipe.");
                return;
            }

            logger.Debug("Overlay connected to pipe.");

            byte[] buffer = new byte[1024];
            while (!overlayProcess.HasExited && pipe.IsConnected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int bytesRead;
                try
                {
                    bytesRead = await pipe.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    logger.Warn("Forwarding cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Error while reading from pipe.");
                    break;
                }
                if (bytesRead <= 0)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }
            logger.Debug("Overlay exited or pipe closed.");
        }
    }

}
