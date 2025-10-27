using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GogOssLibraryNS.Models
{
    public class GogDepot
    {
        public Depot depot { get; set; } = new Depot();
        public int version { get; set; }
        public class Depot
        {
            public List<Item> items { get; set; } = new List<Item>();
            public Smallfilescontainer smallFilesContainer { get; set; }
            public Dictionary<string, List<Chunk>> sfcContainersByHash { get; set; } = new();
        }
        public class Item
        {
            public string path { get; set; } = "";
            public List<Chunk> chunks { get; set; } = new List<Chunk>();
            public string type { get; set; } = "";
            public List<string> flags { get; set; }
            public string sha256 { get; set; } = "";
            public sfcRef sfcRef { get; set; }
        }
        public class sfcRef
        {
            public long offset { get; set; }
            public double size { get; set; }
            public string depotHash { get; set; }
        }
        public class Chunk
        {
            public string md5 { get; set; }
            public double size { get; set; }
            public string compressedMd5 { get; set; } = "";
            public double compressedSize { get; set; }
            public long offset { get; internal set; }
        }
        public class Smallfilescontainer
        {
            public List<Chunk> chunks { get; set; } = new();
        }
    }

}
