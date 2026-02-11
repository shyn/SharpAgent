using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;

namespace Sharp.Gui.Converters;

public class BoolToAlignmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
