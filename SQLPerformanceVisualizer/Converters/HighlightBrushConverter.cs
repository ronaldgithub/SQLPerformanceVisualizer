using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SQLPerformanceVisualizer.Converters;

public class HighlightBrushConverter : IValueConverter
{
    public static readonly HighlightBrushConverter Instance = new();
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(80, 255, 215, 0));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? HighlightBrush : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
