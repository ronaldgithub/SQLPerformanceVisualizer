using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SQLPerformanceVisualizer.Converters;

public class HighlightBrushConverter : IValueConverter
{
    public static readonly HighlightBrushConverter Instance = new();
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(60, 0, 140, 255));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? HighlightBrush : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}
