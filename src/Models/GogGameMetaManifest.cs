using System.Collections.Generic;

namespace GogOssLibraryNS.Models
{
    public class GogGameMetaManifest
    {
        public string baseProductId { get; set; }
        public string buildId { get; set; } = "";
        public string clientId { get; set; }
        public string clientSecret { get; set; }
        public List<Depot> depots { get; set; } = new List<Depot>();
        public string installDirectory { get; set; }
        public Offlinedepot offlineDepot { get; set; }
        public string platform { get; set; }
        public List<Product> products { get; set; } = new List<Product>();
        public bool scriptInterpreter { get; set; } = false;
        public List<string> tags { get; set; }
        public int version { get; set; } = 2;
        public List<string> dependencies { get; set; } = new List<string>();
        public ProductV1 product { get; set; } = new ProductV1();
        public bool errorDisplayed { get; set; } = false;

        public List<string> languages = new List<string>();
        public Dictionary<string, SizeType> size { get; set; } = new Dictionary<string, SizeType>();
        public string versionName { get; set; } = "";

        public Dictionary<string, Dlc> dlcs { get; set; } = new Dictionary<string, Dlc>();

        public GogDownloadRedistManifest.Executable executable { get; set; } = new GogDownloadRedistManifest.Executable();

        public string readableName { get; set; } = "";
        public string buildId_source { get; set; }
        public string buildId_target { get; set; }
        public string algorithm { get; set; }

        public class Offlinedepot
        {
            public int compressedSize { get; set; }
            public string[] languages { get; set; }
            public string manifest { get; set; }
            public string productId { get; set; }
            public int size { get; set; }
        }

        public class Depot
        {
            public long compressedSize { get; set; }
            public List<string> languages { get; set; } = new List<string>();
            public string manifest { get; set; } = "";
            public string productId { get; set; } = "";
            public long size { get; set; }
            public bool isGogDepot { get; set; }
            public List<string> gameIDs { get; set; } = new List<string>();
            public string redist { get; set; }
            public string executable { get; set; }
            public string argument { get; set; }
            public string targetDir { get; set; }
            public List<string> osBitness { get; set; }
        }

        public class ProductV1
        {
            public int timestamp { get; set; }
            public List<SupportCommand> support_commands { get; set; } = new List<SupportCommand>();
            public string installDirectory { get; set; }
            public string rootGameID { get; set; }
            public List<Gameid> gameIDs { get; set; }
            public string projectName { get; set; }
            public List<Depot> depots { get; set; } = new List<Depot>();
        }

        public class Gameid
        {
            public string gameID { get; set; }
            public bool standalone { get; set; }
            public Dictionary<string, string> name { get; set; }
        }


        public class Product
        {
            public string name { get; set; } = "";
            public string productId { get; set; } = "";
            public string temp_arguments { get; set; } = "";
            public string temp_executable { get; set; } = "";
        }

        public class SupportCommand
        {
            public List<string> languages { get; set; }
            public string executable { get; set; }
            public string gameID { get; set; }
            public string argument { get; set; }
            public List<string> systems { get; set; }
        }

        public class SizeType
        {
            public double download_size { get; set; } = 0;
            public double disk_size { get; set; } = 0;
        }

        public class Dlc
        {
            public string title { get; set; }
            public Dictionary<string, SizeType> size { get; set; } = new Dictionary<string, SizeType>();
        }

    }
}
