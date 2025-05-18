using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoStreamRec.Converters
{
    [ValueConversion(typeof(bool), typeof(object))]
    public class UniversalBoolConverter : IValueConverter
    {
        // Mode de conversion par défaut (InverseBool)
        public bool InverseBool { get; set; } = true;
        
        // Pour la conversion en Visibility
        public bool ToVisibility { get; set; }
        
        // Pour les valeurs nulles (optionnel)
        public object NullValue { get; set; } = DependencyProperty.UnsetValue;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return NullValue;

            bool boolValue;
            if (value is bool b)
                boolValue = b;
            else if (value is bool?)
                boolValue = (value as bool?) ?? false;
            else
                return NullValue;

            // Application de l'inversion si demandée
            bool result = InverseBool ? !boolValue : boolValue;

            // Conversion en Visibility si demandé
            if (ToVisibility)
                return result ? Visibility.Visible : Visibility.Collapsed;

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (ToVisibility && value is Visibility visibility)
                return InverseBool ? visibility != Visibility.Visible : visibility == Visibility.Visible;

            if (value is bool boolValue)
                return InverseBool ? !boolValue : boolValue;

            return NullValue;
        }
    }
}