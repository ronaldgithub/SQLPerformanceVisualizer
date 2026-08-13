using System.Globalization;

namespace SQLPerformanceVisualizer.Models;

/// <summary>One worker thread's contribution to a parallel operator (one &lt;RunTimeCountersPerThread&gt; row).</summary>
public record ThreadRowStat(int ThreadId, long ActualRows, double ActualElapsedMs);

/// <summary>
/// One RelOp node from a showplan XML file, with its children, plus a self-elapsed time
/// computed by subtracting the children's cumulative time from this node's own cumulative time.
/// </summary>
public record PlanOperatorNode(
    int NodeId,
    string PhysicalOp,
    string LogicalOp,
    string? ObjectLabel,
    long EstimateRows,
    double EstimatedSubtreeCost,
    long? ActualRows,
    double? ActualElapsedMs,
    double? ActualCpuMs,
    double SelfElapsedMs,
    IReadOnlyList<ThreadRowStat> ThreadRows,
    IReadOnlyList<PlanOperatorNode> Children)
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public bool HasActuals => ActualElapsedMs is not null;

    public string DisplayName => ObjectLabel is null ? PhysicalOp : $"{PhysicalOp} — {ObjectLabel}";

    public long RowsForDisplay => ActualRows ?? EstimateRows;

    public string RowsLabel => ActualRows is not null
        ? $"{ActualRows.Value.ToString("N0", Invariant)} rows"
        : $"~{EstimateRows.ToString("N0", Invariant)} rows (est)";

    public string SelfTimeLabel => HasActuals
        ? $"self {SelfElapsedMs.ToString("N0", Invariant)} ms"
        : $"self cost {SelfElapsedMs.ToString("N2", Invariant)}";
}
