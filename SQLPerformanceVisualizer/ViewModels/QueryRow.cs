using CommunityToolkit.Mvvm.ComponentModel;
using SQLPerformanceVisualizer.Models;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class QueryRow : ObservableObject
{
    public QueryStoreQueryInfo Info { get; }
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isAnalyzed;

    public QueryRow(QueryStoreQueryInfo info) => Info = info;
}
