using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia;

namespace Strivea.Converters
{
    public class UniversalBoolConverter : IValueConverter
    {
        // Mode de conversion par défaut (InverseBool)
        public bool InverseBool { get; set; } = true;
        
        // Pour la conversion en Visibility
        public bool ToVisibility { get; set; }
        
        // Pour les valeurs nulles (optionnel)
        public object NullValue { get; set; } = AvaloniaProperty.UnsetValue;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool result;

            switch (value)
            {
                case bool boolValue:
                    result = boolValue;
                    break;
                case string stringValue:
                    result = !string.IsNullOrWhiteSpace(stringValue);
                    break;
                case int intValue:
                    result = intValue != 0;
                    break;
                case double doubleValue:
                    result = doubleValue != 0;
                    break;
                case DateTime dateTimeValue:
                    result = dateTimeValue != DateTime.MinValue;
                    break;
                default:
                    result = value != null;
                    break;
            }

            if (InverseBool)
            {
                result = !result;
            }

            if (ToVisibility)
            {
                return result ? Visibility.Visible : Visibility.Collapsed;
            }

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                if (InverseBool)
                {
                    boolValue = !boolValue;
                }

                if (targetType == typeof(string))
                {
                    return boolValue.ToString();
                }
                else if (targetType == typeof(int))
                {
                    return boolValue ? 1 : 0;
                }
                else if (targetType == typeof(double))
                {
                    return boolValue ? 1.0 : 0.0;
                }
            }

            throw new NotSupportedException($"Conversion non supportée de {value} vers {targetType}");
        }

        // Ajout d'une énumération interne pour remplacer Visibility de WPF
        public enum Visibility
        {
            Visible,
            Collapsed
        }
    }
}