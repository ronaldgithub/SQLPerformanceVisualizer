namespace SQLPerformanceVisualizer.Models;

/// <summary>
/// A parsed showplan: the operator tree plus statement-level facts the tree itself doesn't carry.
/// </summary>
public record ExecutionPlanTree(PlanOperatorNode Root, int DegreeOfParallelism)
{
    public bool IsSerial => DegreeOfParallelism <= 1;
}
