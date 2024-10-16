using System.Collections.Generic;

namespace GogOssLibraryNS.Models
{
    public class GogRemoteConfig
    {
        public string version { get; set; } = "";
        public Content content { get; set; } = new Content();
        public object[] bases { get; set; }

        public class Content
        {
            public Macos MacOS { get; set; } = new Macos();
            public Windows Windows { get; set; } = new Windows();
            public CloudStorage cloudStorage { get; set; } = new CloudStorage();
        }

        public class Macos
        {
            public Overlay overlay { get; set; } = new Overlay();
            public PlatformCloudStorage cloudStorage { get; set; } = new PlatformCloudStorage();
        }

        public class Windows
        {
            public Overlay overlay { get; set; } = new Overlay();
            public PlatformCloudStorage cloudStorage { get; set; } = new PlatformCloudStorage();
        }

        public class CloudStorage
        {
            public int quota { get; set; } = 0;
        }

        public class Overlay
        {
            public bool supported { get; set; } = false;
        }

        public class PlatformCloudStorage
        {
            public bool enabled { get; set; } = false;
            public List<CloudLocation> locations { get; set; } = new List<CloudLocation>();
        }

        public class CloudLocation
        {
            public string name { get; set; } = "";
            public string location { get; set; } = "";
        }

    }
}
