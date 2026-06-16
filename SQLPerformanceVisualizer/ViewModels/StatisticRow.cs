using CommunityToolkit.Mvvm.ComponentModel;
using SQLPerformanceVisualizer.Models;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class StatisticRow : ObservableObject
{
    public StatisticInfo Info { get; }
    [ObservableProperty] private bool _isJustUpdated;

    public StatisticRow(StatisticInfo info) => Info = info;
}
