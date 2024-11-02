using CommonPlugin.Enums;
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
                case DownloadStatus.Queued:
                    value = ResourceProvider.GetString(LOC.GogOssDownloadQueued);
                    break;
                case DownloadStatus.Running:
                    value = ResourceProvider.GetString(LOC.GogOssDownloadRunning);
                    break;
                case DownloadStatus.Canceled:
                    value = ResourceProvider.GetString(LOC.GogOssDownloadCanceled);
                    break;
                case DownloadStatus.Paused:
                    value = ResourceProvider.GetString(LOC.GogOssDownloadPaused);
                    break;
                case DownloadStatus.Completed:
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
