using System.Windows.Media;

namespace GridlockDesigner.Helpers;

public static class ColorUtilities
{
    public static string GetVehicleColor(string colorName)
    {
        return colorName switch
        {
            "Red" => "#FF0000",
            "Cyan" => "#00FFFF",
            "Yellow" => "#E9FF8C",
            "Purple" => "#9962D8",
            "Green" => "#38F546",
            "Blue" => "#0000FF",
            "Brown" => "#995E3F",
            "Black" => "#000000",
            "White" => "#FFFFFF",
            _ => "#CCCCCC"
        };
    }

    public static string GetNextColor(string currentColor)
    {
        var colors = new[] { "Cyan", "Yellow", "Purple", "Green", "Blue", "Brown", "Black", "White" };
        var currentIndex = Array.IndexOf(colors, currentColor);
        var nextIndex = (currentIndex + 1) % colors.Length;
        return colors[nextIndex];
    }

    public static SolidColorBrush GetVehicleTextColor(string colorHex)
    {
        var darkColors = new[] { "#000000", "#0000FF", "#FF0000", "#800080", "#A52A2A", "#995E3F" };
        return darkColors.Contains(colorHex) ? Brushes.White : Brushes.Black;
    }
}
