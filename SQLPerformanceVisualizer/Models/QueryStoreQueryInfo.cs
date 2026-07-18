using System.Globalization;
using System.Text.RegularExpressions;

namespace SQLPerformanceVisualizer.Models;

public record QueryStoreQueryInfo(
    long QueryId,
    long PlanId,
    string SqlText,
    string ObjectName,
    long Executions,
    double AvgDurationMs,
    double AvgCpuMs,
    double AvgLogicalReads,
    DateTime? LastExecution)
{
    private static readonly CultureInfo EuropeanCulture = new("nl-NL");

    public string SqlSnippet
    {
        get
        {
            var collapsed = Regex.Replace(SqlText, @"\s+", " ").Trim();
            return collapsed.Length <= 120 ? collapsed : collapsed[..120] + "…";
        }
    }

    public string ExecutionsDisplay => Executions.ToString("N0", EuropeanCulture);

    public string AvgDurationDisplay => FormatMetric(AvgDurationMs);

    public string AvgCpuDisplay => FormatMetric(AvgCpuMs);

    public string AvgLogicalReadsDisplay => FormatMetric(AvgLogicalReads);

    // keep one decimal only for tiny values; whole numbers elsewhere so cells stay narrow
    private static string FormatMetric(double value) =>
        value < 10 ? value.ToString("N1", EuropeanCulture) : value.ToString("N0", EuropeanCulture);

    public string LastExecutionDisplay => LastExecution?.ToString("yyyy-MM-dd HH:mm") ?? "";
}
