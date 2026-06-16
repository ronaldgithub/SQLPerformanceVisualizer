using System.Globalization;
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
            SELECT
                s.name AS TABLE_SCHEMA,
                t.name AS TABLE_NAME,
                CAST(SUM(CASE WHEN ps.index_id IN (0, 1) THEN ps.row_count ELSE 0 END) AS BIGINT) AS rc,
                CAST(SUM(CASE WHEN ps.index_id IN (0, 1)
                              THEN ps.in_row_data_page_count + ps.lob_used_page_count + ps.row_overflow_used_page_count
                              ELSE 0 END) * 8 AS BIGINT) AS DataSizeKB,
                CAST(SUM(CASE WHEN ps.index_id IN (0, 1)
                              THEN ps.used_page_count - (ps.in_row_data_page_count + ps.lob_used_page_count + ps.row_overflow_used_page_count)
                              ELSE ps.used_page_count END) * 8 AS BIGINT) AS IndexSizeKB
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            JOIN sys.dm_db_partition_stats ps ON ps.object_id = t.object_id
            WHERE t.is_ms_shipped = 0
            GROUP BY s.name, t.name
            ORDER BY s.name, t.name
            """;
        var results = new List<TableInfo>();
        await using var conn = new SqlConnection(ConnectionStringForDb(database));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(new TableInfo(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4)));
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

    public async Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(string database, string schema, string table, CancellationToken ct = default)
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
                    FOR XML PATH(''), TYPE).value('.','nvarchar(max)'), 1, 2, ''), '') AS IncludedColumns,
                ISNULL((
                    SELECT SUM(ps.used_page_count)
                    FROM sys.dm_db_partition_stats ps
                    WHERE ps.object_id = i.object_id AND ps.index_id = i.index_id), 0) AS Pages,
                ISNULL((
                    SELECT AVG(ps2.avg_fragmentation_in_percent)
                    FROM sys.dm_db_index_physical_stats(DB_ID(), i.object_id, i.index_id, NULL, 'LIMITED') ps2), 0) AS FragmentationPercent
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id = i.object_id
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema AND t.name = @table AND i.index_id > 0
            ORDER BY CASE WHEN i.is_primary_key = 1 THEN 0 ELSE 1 END, i.name
            """;
        var results = new List<IndexInfo>();
        await using var conn = new SqlConnection(ConnectionStringForDb(database));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var pages = reader.GetInt64(6);
            results.Add(new IndexInfo(
                IndexName: reader.GetString(0),
                IndexType: reader.GetString(1),
                IsUnique: reader.GetBoolean(2),
                IsPrimaryKey: reader.GetBoolean(3),
                KeyColumns: reader.GetString(4),
                IncludedColumns: reader.GetString(5),
                Pages: pages,
                FragmentationPercent: Convert.ToDouble(reader[7]),
                SizeKB: pages * 8));
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
            LEFT JOIN sys.indexes i ON i.object_id = st.object_id AND i.name = st.name
            CROSS APPLY sys.dm_db_stats_properties(st.object_id, st.stats_id) sp
            WHERE s.name = @schema AND t.name = @table
            ORDER BY
                CASE
                    WHEN i.is_primary_key = 1 THEN 0
                    WHEN st.auto_created = 1 THEN 2
                    ELSE 1
                END,
                st.name
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

    public async Task<StatisticDetailInfo> GetStatisticDetailAsync(string database, string schema, string table, string statName, CancellationToken ct = default)
    {
        var qualifiedTable = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        var sql = $"DBCC SHOW_STATISTICS ('{EscapeLiteral(qualifiedTable)}', '{EscapeLiteral(statName)}') WITH STAT_HEADER, DENSITY_VECTOR, HISTOGRAM;";

        await using var conn = new SqlConnection(ConnectionStringForDb(database));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        StatHeaderInfo? header = null;
        if (await reader.ReadAsync(ct))
        {
            header = new StatHeaderInfo(
                Name: reader["Name"]?.ToString() ?? "",
                Updated: Convert.ToDateTime(reader["Updated"], CultureInfo.InvariantCulture),
                Rows: Convert.ToInt64(reader["Rows"]),
                RowsSampled: Convert.ToInt64(reader["Rows Sampled"]),
                Steps: Convert.ToInt32(reader["Steps"]),
                Density: Convert.ToDouble(reader["Density"]),
                AverageKeyLength: Convert.ToDouble(reader["Average Key Length"]),
                StringIndex: reader["String Index"]?.ToString() ?? "",
                FilterExpression: reader["Filter Expression"] as string,
                UnfilteredRows: Convert.ToInt64(reader["Unfiltered Rows"]),
                PersistedSamplePercent: HasColumn(reader, "Persisted Sample Percent") ? Convert.ToDouble(reader["Persisted Sample Percent"]) : null);
        }

        var densityVectors = new List<DensityVectorInfo>();
        if (await reader.NextResultAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                densityVectors.Add(new DensityVectorInfo(
                    AllDensity: Convert.ToDouble(reader["All density"]),
                    AverageLength: Convert.ToDouble(reader["Average Length"]),
                    Columns: reader["Columns"]?.ToString() ?? ""));
            }
        }

        var histogramSteps = new List<HistogramStepInfo>();
        if (await reader.NextResultAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                histogramSteps.Add(new HistogramStepInfo(
                    RangeHiKey: reader["RANGE_HI_KEY"]?.ToString() ?? "",
                    RangeRows: Convert.ToDouble(reader["RANGE_ROWS"]),
                    EqRows: Convert.ToDouble(reader["EQ_ROWS"]),
                    DistinctRangeRows: Convert.ToDouble(reader["DISTINCT_RANGE_ROWS"]),
                    AvgRangeRows: Convert.ToDouble(reader["AVG_RANGE_ROWS"])));
            }
        }

        return new StatisticDetailInfo(header, densityVectors, histogramSteps);
    }

    public async Task UpdateStatisticsAsync(string database, string schema, string table, string statName, double? samplePercent, CancellationToken ct = default)
    {
        var target = $"{QuoteIdentifier(schema)}.{QuoteIdentifier(table)} {QuoteIdentifier(statName)}";
        var withClause = samplePercent.HasValue
            ? $"WITH SAMPLE {samplePercent.Value.ToString(CultureInfo.InvariantCulture)} PERCENT"
            : "WITH FULLSCAN";
        var sql = $"UPDATE STATISTICS {target} {withClause};";

        await using var conn = new SqlConnection(ConnectionStringForDb(database));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RebuildIndexAsync(string database, string schema, string table, string indexName, CancellationToken ct = default)
    {
        var sql = $"ALTER INDEX {QuoteIdentifier(indexName)} ON {QuoteIdentifier(schema)}.{QuoteIdentifier(table)} REBUILD;";
        await using var conn = new SqlConnection(ConnectionStringForDb(database));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ReorganizeIndexAsync(string database, string schema, string table, string indexName, CancellationToken ct = default)
    {
        var sql = $"ALTER INDEX {QuoteIdentifier(indexName)} ON {QuoteIdentifier(schema)}.{QuoteIdentifier(table)} REORGANIZE;";
        await using var conn = new SqlConnection(ConnectionStringForDb(database));
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static bool HasColumn(SqlDataReader reader, string columnName)
    {
        for (var i = 0; i < reader.FieldCount; i++)
            if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string QuoteIdentifier(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    private static string EscapeLiteral(string literal) => literal.Replace("'", "''");
}
