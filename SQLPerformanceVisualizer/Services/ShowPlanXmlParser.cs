using System.Xml.Linq;
using SQLPerformanceVisualizer.Models;

namespace SQLPerformanceVisualizer.Services;

/// <summary>
/// Turns a showplan XML document into a <see cref="PlanOperatorNode"/> tree. Pure/stateless —
/// no file I/O here, that stays in <see cref="PlanAnalysisService"/> per the project's file-I/O rule.
/// </summary>
internal static class ShowPlanXmlParser
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    public static ExecutionPlanTree Parse(XDocument doc)
    {
        var queryPlan = doc.Descendants(Ns + "QueryPlan").FirstOrDefault()
            ?? throw new InvalidOperationException("No <QueryPlan> element found in this plan file.");
        var rootRelOp = queryPlan.Elements(Ns + "RelOp").FirstOrDefault()
            ?? throw new InvalidOperationException("No <RelOp> found under <QueryPlan> in this plan file.");
        var root = BuildNode(rootRelOp);

        // Lightweight root operators (e.g. Compute Scalar) sometimes carry no RunTimeInformation of
        // their own even in an actual plan. Fall back to the statement-level QueryTimeStats, which is
        // exactly what the root's cumulative elapsed time represents anyway.
        if (root.ActualElapsedMs is null)
        {
            var stats = queryPlan.Element(Ns + "QueryTimeStats");
            var statementElapsed = (double?)stats?.Attribute("ElapsedTime");
            if (statementElapsed is not null)
            {
                var childCumulative = root.Children.Sum(c => c.ActualElapsedMs ?? 0);
                root = root with
                {
                    ActualElapsedMs = statementElapsed,
                    ActualCpuMs = (double?)stats?.Attribute("CpuTime"),
                    SelfElapsedMs = Math.Max(0, statementElapsed.Value - childCumulative),
                };
            }
        }

        var degreeOfParallelism = (int?)queryPlan.Attribute("DegreeOfParallelism") ?? 1;
        return new ExecutionPlanTree(root, degreeOfParallelism);
    }

    private static PlanOperatorNode BuildNode(XElement relOp)
    {
        var bounded = DescendantsBounded(relOp).ToList();
        var children = bounded
            .Where(e => e.Name == Ns + "RelOp")
            .Select(BuildNode)
            .ToList();

        var objectEl = bounded.FirstOrDefault(e => e.Name == Ns + "Object");
        var objectLabel = objectEl is null ? null : FormatObject(objectEl);

        var threadCounters = relOp.Element(Ns + "RunTimeInformation")?
            .Elements(Ns + "RunTimeCountersPerThread")
            .ToList();

        long? actualRows = null;
        double? actualElapsedMs = null;
        double? actualCpuMs = null;
        var threadRows = Array.Empty<ThreadRowStat>() as IReadOnlyList<ThreadRowStat>;
        if (threadCounters is { Count: > 0 })
        {
            actualRows = threadCounters.Sum(e => (long?)e.Attribute("ActualRows") ?? 0);
            actualElapsedMs = threadCounters.Max(e => (double?)e.Attribute("ActualElapsedms") ?? 0);
            actualCpuMs = threadCounters.Max(e => (double?)e.Attribute("ActualCPUms") ?? 0);
            threadRows = threadCounters
                .Select(e => new ThreadRowStat(
                    (int?)e.Attribute("Thread") ?? 0,
                    (long?)e.Attribute("ActualRows") ?? 0,
                    (double?)e.Attribute("ActualElapsedms") ?? 0))
                .OrderBy(t => t.ThreadId)
                .ToList();
        }

        var estimatedSubtreeCost = (double?)relOp.Attribute("EstimatedTotalSubtreeCost") ?? 0;
        double selfElapsedMs;
        if (actualElapsedMs is not null)
        {
            var childCumulative = children.Sum(c => c.ActualElapsedMs ?? 0);
            selfElapsedMs = Math.Max(0, actualElapsedMs.Value - childCumulative);
        }
        else
        {
            var childCost = children.Sum(c => c.EstimatedSubtreeCost);
            selfElapsedMs = Math.Max(0, estimatedSubtreeCost - childCost);
        }

        return new PlanOperatorNode(
            NodeId: (int)relOp.Attribute("NodeId")!,
            PhysicalOp: (string?)relOp.Attribute("PhysicalOp") ?? "?",
            LogicalOp: (string?)relOp.Attribute("LogicalOp") ?? "?",
            ObjectLabel: objectLabel,
            EstimateRows: (long)((double?)relOp.Attribute("EstimateRows") ?? 0),
            EstimatedSubtreeCost: estimatedSubtreeCost,
            ActualRows: actualRows,
            ActualElapsedMs: actualElapsedMs,
            ActualCpuMs: actualCpuMs,
            SelfElapsedMs: selfElapsedMs,
            ThreadRows: threadRows,
            Children: children);
    }

    /// <summary>
    /// Depth-first walk of <paramref name="el"/>'s descendants that stops at (but still yields)
    /// nested &lt;RelOp&gt; elements, so callers can find "this operator's own" content —
    /// e.g. its Object/table reference — without picking up a child operator's content.
    /// </summary>
    private static IEnumerable<XElement> DescendantsBounded(XElement el)
    {
        foreach (var child in el.Elements())
        {
            yield return child;
            if (child.Name != Ns + "RelOp")
                foreach (var d in DescendantsBounded(child))
                    yield return d;
        }
    }

    private static string? FormatObject(XElement objectEl)
    {
        var table = ((string?)objectEl.Attribute("Table"))?.Trim('[', ']');
        var alias = ((string?)objectEl.Attribute("Alias"))?.Trim('[', ']');
        if (table is null) return null;
        return alias is null ? table : $"{table} AS {alias}";
    }
}
