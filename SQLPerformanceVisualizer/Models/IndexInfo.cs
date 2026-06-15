namespace SQLPerformanceVisualizer.Models;

public record IndexInfo(
    string IndexName,
    string IndexType,
    bool IsUnique,
    bool IsPrimaryKey,
    string KeyColumns,
    string IncludedColumns);
