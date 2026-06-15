using SQLPerformanceVisualizer.Models;

namespace SQLPerformanceVisualizer.Services;

public interface ISqlServerService
{
    bool IsConnected { get; }
    void SetConnectionString(string connectionString);
    Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TableInfo>> GetTablesAsync(string database, CancellationToken ct = default);
    Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string database, string schema, string table, CancellationToken ct = default);
    Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(string database, string schema, string table, string columnName, CancellationToken ct = default);
    Task<IReadOnlyList<StatisticInfo>> GetStatisticsAsync(string database, string schema, string table, CancellationToken ct = default);
}
