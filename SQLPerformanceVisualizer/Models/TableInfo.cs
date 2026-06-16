using System.Globalization;

namespace SQLPerformanceVisualizer.Models;

public record TableInfo(string Schema, string Name, long RowCount, long DataSizeKB, long IndexSizeKB)
{
    private static readonly CultureInfo EuropeanCulture = new("nl-NL");

    public string FullName => $"{Schema}.{Name}";

    public string RowCountDisplay => RowCount.ToString("N0", EuropeanCulture);

    public string DataSizeDisplay => (DataSizeKB / 1024.0).ToString("N0", EuropeanCulture);

    public string IndexSizeDisplay => (IndexSizeKB / 1024.0).ToString("N0", EuropeanCulture);
}
