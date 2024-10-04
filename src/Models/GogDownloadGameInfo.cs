using System;
using System.Collections.Generic;

namespace GogOssLibraryNS.Models
{
    public class GogDownloadGameInfo
    {
        public bool errorDisplayed = false;
        public Dictionary<string, SizeType> size { get; set; } = new Dictionary<string, SizeType>();
        public List<Dlc> dlcs { get; set; } = new List<Dlc>();
        public string buildId { get; set; }
        public List<string> languages { get; set; } = new List<string>();
        public string folder_name { get; set; } = "";
        public List<string> dependencies { get; set; } = new List<string>();
        public string versionEtag { get; set; }
        public string versionName { get; set; }
        public List<string> available_branches { get; set; } = new List<string>();
        public Builds builds { get; set; } = new Builds();
        public GogDownloadRedistManifest.Executable executable { get; set; } = new GogDownloadRedistManifest.Executable();

        public class Dlc
        {
            public string title { get; set; }
            public string id { get; set; }
            public Dictionary<string, SizeType> size { get; set; } = new Dictionary<string, SizeType>();
        }

        public class SizeType
        {
            public double download_size { get; set; }
            public double disk_size { get; set; }
        }

        public class Builds
        {
            public int total_count { get; set; }
            public int count { get; set; }
            public List<Item> items { get; set; } = new List<Item>();
            public bool has_private_branches { get; set; }
        }

        public class Item
        {
            public string build_id { get; set; }
            public string product_id { get; set; }
            public string os { get; set; }
            public string branch { get; set; }
            public string version_name { get; set; }
            public string[] tags { get; set; }
            public bool _public { get; set; }
            public DateTime date_published { get; set; }
            public int generation { get; set; }
            public string link { get; set; }
        }

    }
}
