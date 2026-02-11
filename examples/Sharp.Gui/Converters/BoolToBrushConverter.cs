using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Sharp.Gui.Converters;

public class BoolToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush UserBrush = new(Color.Parse("#1e3a5f"));
    private static readonly SolidColorBrush AssistantBrush = new(Color.Parse("#1e293b"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? UserBrush : AssistantBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
