using System.Globalization;
using Avalonia.Data.Converters;

namespace Sharp.Gui.Converters;

public class BoolToOpacityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // When CanChangeWorkspace is true, opacity is 1.0
        // When false (session has started), opacity is 0.5 to indicate disabled
        return value is true ? 1.0 : 0.5;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
