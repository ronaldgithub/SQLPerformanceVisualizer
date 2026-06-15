using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using SQLPerformanceVisualizer.Models;

namespace SQLPerformanceVisualizer.Services;

public class SqlServerService : ISqlServerService
{
    private string _baseConnectionString = string.Empty;

    public bool IsConnected => !string.IsNullOrEmpty(_baseConnectionString);

    public void SetConnectionString(string connectionString)
    {
        _baseConnectionString = connectionString;
    }

    private string ConnectionStringForDb(string dbName) =>
        Regex.Replace(_baseConnectionString, @"Database=[^;]+", $"Database={dbName}", RegexOptions.IgnoreCase);

    public async Task<IReadOnlyList<DatabaseInfo>> GetDatabasesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT name FROM sys.databases
            WHERE state_desc = 'ONLINE'
              AND name NOT IN ('master', 'model', 'msdb', 'tempdb')
              AND name NOT LIKE 'DW%'
            ORDER BY name
            """;
        var results = new List<DatabaseInfo>();
        await using var conn = new SqlConnection(_baseConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(new DatabaseInfo(reader.GetString(0)));
        return results;
    }

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(string database, CancellationToken ct = default)
    {
        const string sql = """
            SELECT t.TABLE_SCHEMA, t.TABLE_NAME,
                   CAST(ISNULL(SUM(p.rows), 0) AS BIGINT) AS rc
            FROM INFORMATION_SCHEMA.TABLES t
            JOIN sys.objects o ON o.name = t.TABLE_NAME
                AND o.schema_id = SCHEMA_ID(t.TABLE_SCHEMA)
                AND o.is_ms_shipped = 0
            LEFT JOIN sys.partitions p ON p.object_id = o.object_id
                AND p.index_id IN (0, 1)
            WHERE t.TABLE_TYPE = 'BASE TABLE'
            GROUP BY t.TABLE_SCHEMA, t.TABLE_NAME
            ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME
            """;
        var results = new List<TableInfo>();
        await using var conn = new SqlConnection(ConnectionStringForDb(database));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(new TableInfo(reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        return results;
    }

    public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(string database, string schema, string table, CancellationToken ct = default)
    {
        const string sql = """
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE,
                   CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION
            """;
        var results = new List<ColumnInfo>();
        await using var conn = new SqlConnection(ConnectionStringForDb(database));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ColumnInfo(
                ColumnName: reader.GetString(0),
                DataType: reader.GetString(1),
                IsNullable: reader.GetString(2),
                MaxLength: reader.IsDBNull(3) ? null : reader.GetInt32(3),
                NumericPrecision: reader.IsDBNull(4) ? null : (int)reader.GetByte(4),
                NumericScale: reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        }
        return results;
    }

    public async Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(string database, string schema, string table, string columnName, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                i.name AS IndexName,
                i.type_desc AS IndexType,
                CAST(i.is_unique AS BIT) AS IsUnique,
                CAST(i.is_primary_key AS BIT) AS IsPrimaryKey,
                ISNULL(STUFF((
                    SELECT ', ' + c2.name
                    FROM sys.index_columns ic2
                    JOIN sys.columns c2 ON c2.object_id = ic2.object_id AND c2.column_id = ic2.column_id
                    WHERE ic2.object_id = i.object_id AND ic2.index_id = i.index_id AND ic2.is_included_column = 0
                    ORDER BY ic2.key_ordinal
                    FOR XML PATH(''), TYPE).value('.','nvarchar(max)'), 1, 2, ''), '') AS KeyColumns,
                ISNULL(STUFF((
                    SELECT ', ' + c3.name
                    FROM sys.index_columns ic3
                    JOIN sys.columns c3 ON c3.object_id = ic3.object_id AND c3.column_id = ic3.column_id
                    WHERE ic3.object_id = i.object_id AND ic3.index_id = i.index_id AND ic3.is_included_column = 1
                    ORDER BY ic3.column_id
                    FOR XML PATH(''), TYPE).value('.','nvarchar(max)'), 1, 2, ''), '') AS IncludedColumns
            FROM sys.indexes i
            JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND t.name = @table AND c.name = @column
            GROUP BY i.object_id, i.index_id, i.name, i.type_desc, i.is_unique, i.is_primary_key
            ORDER BY i.name
            """;
        var results = new List<IndexInfo>();
        await using var conn = new SqlConnection(ConnectionStringForDb(database));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@column", columnName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new IndexInfo(
                IndexName: reader.GetString(0),
                IndexType: reader.GetString(1),
                IsUnique: reader.GetBoolean(2),
                IsPrimaryKey: reader.GetBoolean(3),
                KeyColumns: reader.GetString(4),
                IncludedColumns: reader.GetString(5)));
        }
        return results;
    }

    public async Task<IReadOnlyList<StatisticInfo>> GetStatisticsAsync(string database, string schema, string table, CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                st.name AS StatName,
                ISNULL(STUFF((
                    SELECT ', ' + c.name
                    FROM sys.stats_columns sc
                    JOIN sys.columns c ON c.object_id = sc.object_id AND c.column_id = sc.column_id
                    WHERE sc.object_id = st.object_id AND sc.stats_id = st.stats_id
                    ORDER BY sc.stats_column_id
                    FOR XML PATH(''), TYPE).value('.','nvarchar(max)'), 1, 2, ''), '') AS Columns,
                sp.last_updated AS LastUpdated,
                sp.rows AS Rows,
                sp.rows_sampled AS RowsSampled,
                sp.modification_counter AS ModificationCounter,
                CASE WHEN sp.rows > 0
                     THEN CAST(sp.rows_sampled * 100.0 / sp.rows AS decimal(5,2))
                     ELSE NULL
                END AS SamplingPercent
            FROM sys.stats st
            JOIN sys.tables t ON t.object_id = st.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            CROSS APPLY sys.dm_db_stats_properties(st.object_id, st.stats_id) sp
            WHERE s.name = @schema AND t.name = @table
            ORDER BY st.name
            """;
        var results = new List<StatisticInfo>();
        await using var conn = new SqlConnection(ConnectionStringForDb(database));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new StatisticInfo(
                StatName: reader.GetString(0),
                Columns: reader.GetString(1),
                LastUpdated: reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                Rows: reader.IsDBNull(3) ? null : reader.GetInt64(3),
                RowsSampled: reader.IsDBNull(4) ? null : reader.GetInt64(4),
                ModificationCounter: reader.IsDBNull(5) ? null : reader.GetInt64(5),
                SamplingPercent: reader.IsDBNull(6) ? null : (double)reader.GetDecimal(6)));
        }
        return results;
    }
}
