using GogOssLibraryNS.Enums;
using System.Collections.Generic;

namespace GogOssLibraryNS.Models
{
    public class Installed
    {
        public string title { get; set; } = "";
        public string platform { get; set; } = "";
        public string executable { get; set; } = "";
        public string install_path { get; set; } = "";
        public string version { get; set; } = "";
        public string build_id { get; set; } = "";
        public List<string> installed_DLCs { get; set; } = new List<string>();
        public string language { get; set; } = "";
        public bool is_fully_installed { get; set; } = false;
        public List<string> Dependencies { get; set; } = new List<string>();
    }
}
