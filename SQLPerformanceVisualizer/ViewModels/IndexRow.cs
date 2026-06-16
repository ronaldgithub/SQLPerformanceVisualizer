using CommunityToolkit.Mvvm.ComponentModel;
using SQLPerformanceVisualizer.Models;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class IndexRow : ObservableObject
{
    public IndexInfo Info { get; }
    [ObservableProperty] private bool _isBusy;

    public IndexRow(IndexInfo info) => Info = info;
}
