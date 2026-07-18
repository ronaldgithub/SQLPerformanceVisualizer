namespace SQLPerformanceVisualizer.Models;

public enum QueryStoreMetric
{
    TotalDuration,
    TotalCpu,
    LogicalReads,
    ExecutionCount,
}

public sealed record QueryStoreMetricItem(QueryStoreMetric Metric, string DisplayName)
{
    public static IReadOnlyList<QueryStoreMetricItem> All { get; } =
    [
        new(QueryStoreMetric.TotalDuration, "Total duration"),
        new(QueryStoreMetric.TotalCpu, "Total CPU"),
        new(QueryStoreMetric.LogicalReads, "Logical reads"),
        new(QueryStoreMetric.ExecutionCount, "Execution count"),
    ];
}
