using GogOssLibraryNS.Enums;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GogOssLibraryNS.Models
{
    public class DownloadManagerData
    {
        public class Rootobject
        {
            public ObservableCollection<Download> downloads { get; set; }
        }

        public class Download : ObservableObject
        {
            public string gameID { get; set; }
            public string name { get; set; }
            public string fullInstallPath { get; set; }

            private double _downloadSizeNumber;
            public double downloadSizeNumber
            {
                get => _downloadSizeNumber;
                set => SetValue(ref _downloadSizeNumber, value);
            }

            private double _installSizeNumber;
            public double installSizeNumber
            {
                get => _installSizeNumber;
                set => SetValue(ref _installSizeNumber, value);
            }

            public long addedTime { get; set; }

            private long _completedTime;
            public long completedTime
            {
                get => _completedTime;
                set => SetValue(ref _completedTime, value);
            }

            private DownloadStatus _status;
            public DownloadStatus status
            {
                get => _status;
                set => SetValue(ref _status, value);
            }

            private double _progress;
            public double progress
            {
                get => _progress;
                set => SetValue(ref _progress, value);
            }

            private double _downloadedNumber;
            public double downloadedNumber
            {
                get => _downloadedNumber;
                set => SetValue(ref _downloadedNumber, value);
            }
            public DownloadItemType downloadItemType { get; set; } = DownloadItemType.Game;
            public DownloadProperties downloadProperties { get; set; } = new DownloadProperties();
            public List<string> depends { get; set; } = new List<string>();
        }
    }

    public class DownloadProperties : ObservableObject
    {
        public string installPath { get; set; } = "";
        public DownloadAction downloadAction { get; set; }
        public int maxWorkers { get; set; }
        public List<string> extraContent { get; set; } = new List<string>();
        public string language { get; set; } = "";
        public string buildId { get; set; } = "";
        public string version { get; set; } = "";
        public string betaChannel { get; set; } = "disabled";
        public string os { get; set; } = "windows";
    }
}
