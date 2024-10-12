using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GogOssLibraryNS.Models
{
    public class UpdateInfo
    {
        public string Title { get; set; }
        public string Version { get; set; }
        public string Install_path { get; set; }
        public string Build_id { get; set; }
        public string Language { get; set; }
        public double Disk_size { get; set; } = 0;
        public double Download_size { get; set; } = 0;
        public string Title_for_updater { get; set; }
        public bool Success { get; set; } = true;
        public List<string> ExtraContent { get; set; } = new List<string>();
        public string Os { get; set; } = "windows";
        public List<string> Depends { get; set; } = new List<string>();
    }
}
