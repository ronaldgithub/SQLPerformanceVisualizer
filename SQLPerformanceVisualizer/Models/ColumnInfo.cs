namespace SQLPerformanceVisualizer.Models;

public record ColumnInfo(
    string ColumnName,
    string DataType,
    string IsNullable,
    int? MaxLength,
    int? NumericPrecision,
    int? NumericScale)
{
    public string DataTypeDisplay
    {
        get
        {
            var t = DataType.ToLowerInvariant();
            if (t is "varchar" or "nvarchar" or "char" or "nchar" or "binary" or "varbinary" && MaxLength.HasValue)
                return MaxLength == -1 ? $"{DataType}(max)" : $"{DataType}({MaxLength})";
            return DataType;
        }
    }
}
