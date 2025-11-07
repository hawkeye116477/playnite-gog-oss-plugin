using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GogOssLibraryNS.Models
{
    public class GogSecureLinks
    {
        public List<string> mainSecureLinks = new();
        public List<string> inGameDependsSecureLinks = new();
        public List<string> patchSecureLinks = new();

        public int product_id { get; set; }
        public string type { get; set; }
        public List<Url> urls { get; set; }
        public class Url
        {
            public string endpoint_name { get; set; }
            public string url_format { get; set; }
            public Dictionary<string, object> parameters { get; set; }
            public int priority { get; set; }
            public int max_fails { get; set; }
            public int[] supports_generation { get; set; }
            public bool fallback_only = false;
        }
    }
}
