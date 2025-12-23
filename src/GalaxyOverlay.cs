using GogOssLibraryNS.Models;
using Playnite.SDK.Data;
using System;
using System.IO;

namespace GogOssLibraryNS
{
    public class GalaxyOverlay
    {
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
    }

}
