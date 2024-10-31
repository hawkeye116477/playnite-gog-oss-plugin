using System.Collections.Generic;

namespace GogOssLibraryNS.Models
{
    public class HeroicInstalled
    {
        public List<HeroicInstalledSingle> installed { get; set; } = new List<HeroicInstalledSingle>();

        public class HeroicInstalledSingle
        {
            public string title { get; set; } = "";
            public string platform { get; set; } = "";
            public string executable { get; set; } = "";
            public string install_path { get; set; } = "";
            public string install_size { get; set; } = "";
            public bool is_dlc { get; set; } = false;
            public string version { get; set; } = "";
            public string appName { get; set; } = "";
            public List<string> installed_DLCs { get; set; } = new List<string>();
            public string language { get; set; } = "";
            public string build_id { get; set; } = "";
        }

    }
}
