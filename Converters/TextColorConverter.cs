using System.Windows.Data;
using System.Windows.Media;

namespace GridlockDesigner;

public class TextColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string colorHex)
        {
            // Colors that should have white text
            var darkColors = new[] { "#000000", "#0000FF", "#FF0000", "#800080", "#A52A2A" };

            if (darkColors.Contains(colorHex))
            {
                return Brushes.White;
            }

            // All other colors get black text
            return Brushes.Black;
        }

        return Brushes.Black; // Default
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
