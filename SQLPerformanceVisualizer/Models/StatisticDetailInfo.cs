using System.Globalization;

namespace SQLPerformanceVisualizer.Models;

public record StatHeaderInfo(
    string Name,
    DateTime Updated,
    long Rows,
    long RowsSampled,
    int Steps,
    double Density,
    double AverageKeyLength,
    string StringIndex,
    string? FilterExpression,
    long UnfilteredRows,
    double? PersistedSamplePercent)
{
    public string DensityDisplay => Density.ToString("0.######", CultureInfo.InvariantCulture);

    public string AverageKeyLengthDisplay => AverageKeyLength.ToString("F2", CultureInfo.InvariantCulture);
}

public record DensityVectorInfo(double AllDensity, double AverageLength, string Columns)
{
    public string AllDensityDisplay => AllDensity.ToString("0.######", CultureInfo.InvariantCulture);

    public string AverageLengthDisplay => AverageLength.ToString("F2", CultureInfo.InvariantCulture);
}

public record HistogramStepInfo(string RangeHiKey, double RangeRows, double EqRows, double DistinctRangeRows, double AvgRangeRows)
{
    public string RangeRowsDisplay => RangeRows.ToString("F2", CultureInfo.InvariantCulture);

    public string EqRowsDisplay => EqRows.ToString("F2", CultureInfo.InvariantCulture);

    public string DistinctRangeRowsDisplay => DistinctRangeRows.ToString("F2", CultureInfo.InvariantCulture);

    public string AvgRangeRowsDisplay => AvgRangeRows.ToString("F2", CultureInfo.InvariantCulture);
}

public record StatisticDetailInfo(
    StatHeaderInfo? Header,
    IReadOnlyList<DensityVectorInfo> DensityVectors,
    IReadOnlyList<HistogramStepInfo> HistogramSteps);
