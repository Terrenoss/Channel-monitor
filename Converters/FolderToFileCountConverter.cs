using System;
using System.Globalization;
using Avalonia.Data.Converters;
using System.IO;

namespace Strivea.Converters
{
    public class FolderToFileCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string folderPath && Directory.Exists(folderPath))
            {
                try
                {
                    var count = Directory.GetFiles(folderPath, "*.ts").Length;
                    return count;
                }
                catch
                {
                    return 0;
                }
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 