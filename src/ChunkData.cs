using GogOssLibraryNS.Enums;
using System.IO;

namespace GogOssLibraryNS
{
    public class ChunkData
    {
        public string FilePath { get; set; }
        public long Offset { get; set; }
        public MemoryStream MemoryStream { get; set; }
        public long AllocatedBytes { get; set; }
        public int Length { get; set; }
        public string TempFilePath { get; set; }
        public bool IsCompressed { get; set; }
        public DepotFileType DepotFileType { get; set; }
        public string ChunkId { get; set; }
    }
}