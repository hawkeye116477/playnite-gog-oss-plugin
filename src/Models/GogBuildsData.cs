using System;
using System.Collections.Generic;

namespace GogOssLibraryNS.Models
{
    public class GogBuildsData
    {
        public bool errorDisplayed = false;
        public int total_count { get; set; }
        public int count { get; set; }
        public List<Item> items { get; set; } = new List<Item>();
        public bool has_private_branches { get; set; }
        public List<string> available_branches { get; set; } = new List<string>();
        public class Item
        {
            public string legacy_build_id { get; set; }
            public string build_id { get; set; }
            public string product_id { get; set; }
            public string os { get; set; }
            public string branch { get; set; } = "";
            public string version_name { get; set; }
            public string[] tags { get; set; }
            public bool _public { get; set; }

            public DateTime date_published { get; set; }
            public int generation { get; set; }
            public string link { get; set; }
        }
    }
}
