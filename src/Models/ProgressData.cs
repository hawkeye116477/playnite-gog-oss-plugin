namespace GogOssLibraryNS.Models
{
    public class ProgressData
    {
        public long NetworkBytes { get; set; }
        public long DiskBytes { get; set; }
        public long TotalBytes { get; set; }
        public int ActiveDownloadWorkers { get; set; }
        public int ActiveDiskWorkers { get; set; }
        public long InitialNetworkBytes { get; set; }
        public long InitialDiskBytes { get; set; }
        public bool FinalReport { get; set; } = false;
    }
}
