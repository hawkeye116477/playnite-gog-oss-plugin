using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Data;

namespace GogOssLibraryNS.Converters
{
    public class ObservableCollectionToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is ObservableCollection<string> list)
            {
                return string.Join(", ", list);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string stringVal && stringVal != "")
            {
                var converted = stringVal.Split(new[] { ", " }, StringSplitOptions.None);
                return converted.ToObservable();
            }
            return null;
        }
    }
}
