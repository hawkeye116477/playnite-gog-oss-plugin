using GogOssLibraryNS.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public DownloadItemType download_item_type { get; set; } = DownloadItemType.Game;
    }
}
