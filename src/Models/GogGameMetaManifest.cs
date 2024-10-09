using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GogOssLibraryNS.Models
{
    public class GogGameMetaManifest
    {
        public string baseProductId { get; set; }
        public string buildId { get; set; }
        public string clientId { get; set; }
        public string clientSecret { get; set; }
        public Depot[] depots { get; set; }
        public string installDirectory { get; set; }
        public Offlinedepot offlineDepot { get; set; }
        public string platform { get; set; }
        public List<Product> products { get; set; } = new List<Product>();
        public bool scriptInterpreter { get; set; } = false;
        public string[] tags { get; set; }
        public int version { get; set; }
        public string HGLInstallLanguage { get; set; }
        public object[] HGLdlcs { get; set; }
        public List<string> dependencies { get; set; } = new List<string>();
        public ProductV1 product { get; set; }

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
            public string[] languages { get; set; }
            public string manifest { get; set; }
            public string productId { get; set; }
            public long size { get; set; }
            public bool isGogDepot { get; set; }
        }

        public class ProductV1
        {
            public int timestamp { get; set; }
            public List<SupportCommand> support_commands { get; set; } = new List<SupportCommand>();
            public string installDirectory { get; set; }
            public string rootGameID { get; set; }
            public List<Gameid> gameIDs { get; set; }
            public string projectName { get; set; }
        }

        public class Gameid
        {
            public string gameID { get; set; }
            public bool standalone { get; set; }
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

    }
}
