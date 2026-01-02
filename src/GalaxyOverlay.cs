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
            string gogDataPath = Path.Combine(programDataPath, "GOG.com", "Galaxy");
            var neededDirs = new List<string>
            {
                Path.Combine(gogDataPath, "logs"),
                Path.Combine(gogDataPath, "webcache")
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
               
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to notify Comet");
                return false;
            }
        }

    }

}
