using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GogOssLibraryNS.Models
{
    public class LauncherVersion
    {
        public string Tag_name { get; set; }
        public List<Asset> Assets { get; set; }
        public class Asset
        {
            public long Size { get; set; }
            public string Digest { get; set; }
            public string Browser_download_url { get; set; }
        }
    }
}
