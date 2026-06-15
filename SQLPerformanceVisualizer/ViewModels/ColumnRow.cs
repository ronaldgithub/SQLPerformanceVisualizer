using CommunityToolkit.Mvvm.ComponentModel;
using SQLPerformanceVisualizer.Models;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class ColumnRow : ObservableObject
{
    public ColumnInfo Info { get; }
    [ObservableProperty] private bool _isHighlighted;

    public ColumnRow(ColumnInfo info) => Info = info;
}
