using System.Collections.Generic;

namespace GogOssLibraryNS.Models
{
    public class GameSettings
    {
        public bool? DisableGameVersionCheck { get; set; }
        public bool? AutoSyncSaves { get; set; }
        public string CloudSaveFolder { get; set; } = "";
        public bool? AutoSyncPlaytime { get; set; }
        public List<string> StartupArguments { get; set; } = new List<string>();
        public bool? EnableCometSupport { get; set; }
        public string OverrideExe { get; set; } = "";
        public long LastCloudSavesDownloadAttempt { get; set; } = 0;
    }
}
