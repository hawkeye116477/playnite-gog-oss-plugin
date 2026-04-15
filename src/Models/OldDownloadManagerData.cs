using CommonPlugin.Enums;
using GogOssLibraryNS.Enums;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GogOssLibraryNS.Models
{
    public class OldDownloadManagerData
    {
        public ObservableCollection<Download> downloads { get; set; }

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
}
