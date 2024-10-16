namespace GogOssLibraryNS.Models
{
    public class CloudFile
    {
        public int bytes { get; set; }
        public string last_modified { get; set; }
        public string hash { get; set; }
        public string name { get; set; }
        public string content_type { get; set; }
        public string real_file_path { get; set; }
        public long timestamp { get; set; }
    }
}
