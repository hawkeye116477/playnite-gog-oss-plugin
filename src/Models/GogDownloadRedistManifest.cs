using System.Collections.Generic;

namespace CometLibraryNS.Models
{
    public class GogDownloadRedistManifest
    {
        public List<Depot> depots { get; set; } = new List<Depot>();
        public string build_id { get; set; }

        public class Depot
        {
            public int compressedSize { get; set; }
            public string dependencyId { get; set; }
            public Executable executable { get; set; }
            public bool _internal { get; set; }
            public string[] languages { get; set; }
            public string manifest { get; set; }
            public string readableName { get; set; }
            public string signature { get; set; }
            public int size { get; set; }
            public string[] osBitness { get; set; }
            public string build_id { get; set; }
        }

        public class Executable
        {
            public string arguments { get; set; }
            public string path { get; set; }
        }
    }
}
