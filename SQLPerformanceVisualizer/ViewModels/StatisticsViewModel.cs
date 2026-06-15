using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SQLPerformanceVisualizer.Models;
using SQLPerformanceVisualizer.Services;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class StatisticsViewModel : ViewModelBase
{
    private readonly ISqlServerService _service;

    [ObservableProperty] private ObservableCollection<StatisticInfo> _statistics = [];
    [ObservableProperty] private StatisticInfo? _selectedStatistic;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    private CancellationTokenSource? _cts;

    public StatisticsViewModel(ISqlServerService service)
    {
        _service = service;
    }

    public async Task LoadAsync(string database, string schema, string table)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        SelectedStatistic = null;
        Statistics = [];
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            var items = await _service.GetStatisticsAsync(database, schema, table, _cts.Token);
            Statistics = new ObservableCollection<StatisticInfo>(items);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }
}
