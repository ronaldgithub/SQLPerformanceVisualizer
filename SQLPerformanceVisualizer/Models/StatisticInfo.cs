namespace SQLPerformanceVisualizer.Models;

public record StatisticInfo(
    string StatName,
    string Columns,
    DateTime? LastUpdated,
    long? Rows,
    long? RowsSampled,
    long? ModificationCounter,
    double? SamplingPercent)
{
    public string SamplingPercentText => SamplingPercent.HasValue
        ? SamplingPercent.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
        : "";
}
