using GogOssLibraryNS.Enums;
using Playnite.SDK;
using System;
using System.Globalization;
using System.Windows.Data;

namespace GogOssLibraryNS.Converters
{
    public class DownloadStatusEnumToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value)
            {
                case Enums.DownloadStatus.Queued:
                    value = ResourceProvider.GetString(LOC.GogOssDownloadQueued);
                    break;
                case Enums.DownloadStatus.Running:
                    value = ResourceProvider.GetString(LOC.GogOssDownloadRunning);
                    break;
                case Enums.DownloadStatus.Canceled:
                    value = ResourceProvider.GetString(LOC.GogOssDownloadCanceled);
                    break;
                case Enums.DownloadStatus.Paused:
                    value = ResourceProvider.GetString(LOC.GogOssDownloadPaused);
                    break;
                case Enums.DownloadStatus.Completed:
                    value = ResourceProvider.GetString(LOC.GogOssDownloadCompleted);
                    break;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
