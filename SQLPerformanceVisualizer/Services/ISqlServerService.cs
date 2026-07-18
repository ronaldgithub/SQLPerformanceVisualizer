using SQLPerformanceVisualizer.Models;

namespace SQLPerformanceVisualizer.Services;

public interface ISqlServerService
{
    bool IsConnected { get; }
    void SetConnectionString(string connectionString);
    Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TableInfo>> GetTablesAsync(string database, CancellationToken ct = default);
    Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string database, string schema, string table, CancellationToken ct = default);
    Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(string database, string schema, string table, CancellationToken ct = default);
    Task<IReadOnlyList<StatisticInfo>> GetStatisticsAsync(string database, string schema, string table, CancellationToken ct = default);
    Task<StatisticDetailInfo> GetStatisticDetailAsync(string database, string schema, string table, string statName, CancellationToken ct = default);
    Task UpdateStatisticsAsync(string database, string schema, string table, string statName, double? samplePercent, CancellationToken ct = default);
    Task RebuildIndexAsync(string database, string schema, string table, string indexName, CancellationToken ct = default);
    Task ReorganizeIndexAsync(string database, string schema, string table, string indexName, CancellationToken ct = default);
    Task<IReadOnlyList<QueryStoreQueryInfo>> GetQueryStoreTopQueriesAsync(string database, QueryStoreMetric metric, CancellationToken ct = default);
    Task<string> GetQueryStorePlanXmlAsync(string database, long planId, CancellationToken ct = default);
    Task<string> ExecuteQueryCaptureActualPlanAsync(string database, string sqlText, CancellationToken ct = default);
}
