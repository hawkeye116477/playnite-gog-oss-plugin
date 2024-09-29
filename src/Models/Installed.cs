using GogOssLibraryNS.Enums;
using System.Collections.Generic;

namespace GogOssLibraryNS.Models
{
    public class Installed
    {
        public string title { get; set; }
        public string platform { get; set; }
        public string executable { get; set; } = "";
        public string install_path { get; set; }
        public bool is_dlc { get; set; } = false;
        public string version { get; set; }
        public string build_id { get; set; }
        public List<string> installed_DLCs { get; set; } = default;
        public string language { get; set; }
        public DownloadItemType item_type { get; set; } = DownloadItemType.Game;
    }
}
