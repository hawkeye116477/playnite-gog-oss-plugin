using CometLibraryNS.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CometLibraryNS.Models
{
    public class Installed
    {
        public string Title { get; set; }
        public string Platform { get; set; }
        public string Executable { get; set; } = "";
        public string Install_path { get; set; }
        public bool Is_dlc { get; set; } = false;
        public string Version { get; set; }
        public string Build_id { get; set; }
        public List<string> Installed_DLCs { get; set; } = default;
        public string Language { get; set; }
        public DownloadItemType Download_item_type { get; set; } = DownloadItemType.Game;
    }
}
