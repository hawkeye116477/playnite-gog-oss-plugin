using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GogOssLibraryNS.Models
{
    public class GameSettings
    {
        public bool? AutoSyncSaves { get; set; }
        public string CloudSaveFolder { get; set; } = "";
        public bool? AutoSyncPlaytime { get; set; }
        public List<string> Dependencies { get; set; } = new List<string>();
        public List<string> StartupArguments { get; set; } = new List<string>();
        public bool? EnableCometSupport { get; set; }
        public string OverrideExe { get; set; } = "";
    }
}
