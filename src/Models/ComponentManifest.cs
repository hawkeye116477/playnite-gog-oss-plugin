using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GogOssLibraryNS.Models
{
    public enum ComponentChoice
    {
        Web,
        Overlay
    }

    public class ComponentManifest
    {
        public string applicationType { get; set; }
        public string baseURI { get; set; }
        public List<File> files { get; set; }
        public bool forceUpdate { get; set; }
        public string projectName { get; set; }
        public object[] symlinks { get; set; }
        public string timestamp { get; set; }
        public string version { get; set; }

        public class File
        {
            public bool exists { get; set; }
            public string hash { get; set; }
            public string path { get; set; }
            public string resource { get; set; }
            public string sha256 { get; set; }
            public int size { get; set; }
            public string unsignedHash { get; set; }
        }

    }
}
