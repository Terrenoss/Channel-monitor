using System;
using System.Globalization;
using Avalonia.Data.Converters;
using System.IO;

namespace Strivea.Converters
{
    public class FolderToChannelNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string folderPath && !string.IsNullOrWhiteSpace(folderPath))
            {
                // On suppose que le dossier Temp est à la fin du chemin
                var dirInfo = new DirectoryInfo(folderPath);
                var channelDir = dirInfo.Parent;
                return channelDir?.Name ?? folderPath;
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 