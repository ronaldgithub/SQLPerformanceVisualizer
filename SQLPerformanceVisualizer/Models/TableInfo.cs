namespace SQLPerformanceVisualizer.Models;

public record TableInfo(string Schema, string Name, long RowCount)
{
    public string FullName =>
        $"{Schema}.{Name} [{RowCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}]";
}
