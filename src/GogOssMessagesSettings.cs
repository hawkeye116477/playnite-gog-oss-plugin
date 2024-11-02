using Playnite.Common;
using Playnite.SDK.Data;
using System;
using System.IO;

namespace GogOssLibraryNS
{
    public class GogOssMessagesSettingsModel
    {
        public bool DontShowDownloadManagerWhatsUpMsg { get; set; } = false;
    }

    public class GogOssMessagesSettings
    {
        public static GogOssMessagesSettingsModel LoadSettings()
        {
            GogOssMessagesSettingsModel messagesSettings = null;
            var dataDir = GogOssLibrary.Instance.GetPluginUserDataPath();
            var dataFile = Path.Combine(dataDir, "messages.json");
            bool correctJson = false;
            if (File.Exists(dataFile))
            {
                var content = FileSystem.ReadFileAsStringSafe(dataFile);
                if (!content.IsNullOrWhiteSpace() && Serialization.TryFromJson(content, out messagesSettings))
                {
                    correctJson = true;
                }
            }
            if (!correctJson)
            {
                messagesSettings = new GogOssMessagesSettingsModel { };
            }
            return messagesSettings;
        }

        public static void SaveSettings(GogOssMessagesSettingsModel messagesSettings)
        {
            var commonHelpers = GogOssLibrary.Instance.commonHelpers;
            commonHelpers.SaveJsonSettingsToFile(messagesSettings, "", "messages", true);
        }
    }
}
