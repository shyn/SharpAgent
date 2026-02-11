using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Sharp.Gui.Converters;

public class BrushToTransparentBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Handle Color directly from binding
        if (value is Color color)
        {
            // Create a brush with 20% opacity of the original color
            return new SolidColorBrush(new Color(51, color.R, color.G, color.B));
        }

        // Handle SolidColorBrush (fallback)
        if (value is ISolidColorBrush solidBrush)
        {
            var c = solidBrush.Color;
            return new SolidColorBrush(new Color(51, c.R, c.G, c.B));
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
