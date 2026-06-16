using System.Globalization;

namespace SQLPerformanceVisualizer.Models;

public record IndexInfo(
    string IndexName,
    string IndexType,
    bool IsUnique,
    bool IsPrimaryKey,
    string KeyColumns,
    string IncludedColumns,
    long Pages,
    long SizeKB,
    double FragmentationPercent)
{
    private static readonly CultureInfo EuropeanCulture = new("nl-NL");

    public string IndexTypeDisplay => IndexType.ToLowerInvariant();

    public string PagesDisplay => Pages.ToString("N0", EuropeanCulture);

    public string SizeMBDisplay => (SizeKB / 1024.0).ToString("N0", EuropeanCulture);

    public string FragmentationDisplay => FragmentationPercent.ToString("N1", EuropeanCulture);
}
