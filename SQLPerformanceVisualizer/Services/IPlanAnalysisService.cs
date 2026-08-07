namespace SQLPerformanceVisualizer.Services;

public record PlanAnalysisResult(string ReportPath, string Markdown);

public interface IPlanAnalysisService
{
    string GetPlansFolder(string database);
    Task<string> SavePlanAsync(string database, long queryId, string planXml, string suffix, CancellationToken ct = default);
    Task<PlanAnalysisResult> AnalyzePlanAsync(string planPath, CancellationToken ct = default);
    Task<PlanAnalysisResult> AnalyzePlanAsync(string planPath, string reportPath, CancellationToken ct = default);
}
