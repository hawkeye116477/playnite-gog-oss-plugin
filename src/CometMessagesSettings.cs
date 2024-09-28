using Playnite.Common;
using Playnite.SDK.Data;
using System;
using System.IO;

namespace CometLibraryNS
{
    public class CometMessagesSettingsModel
    {
        public bool DontShowDownloadManagerWhatsUpMsg { get; set; } = false;
    }

    public class CometMessagesSettings
    {
        public static CometMessagesSettingsModel LoadSettings()
        {
            CometMessagesSettingsModel messagesSettings = null;
            var dataDir = CometLibrary.Instance.GetPluginUserDataPath();
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
                messagesSettings = new CometMessagesSettingsModel { };
            }
            return messagesSettings;
        }

        public static void SaveSettings(CometMessagesSettingsModel messagesSettings)
        {
            Helpers.SaveJsonSettingsToFile(messagesSettings, "messages");
        }
    }
}
