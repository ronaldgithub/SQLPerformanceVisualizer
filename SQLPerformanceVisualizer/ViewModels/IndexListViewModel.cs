using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SQLPerformanceVisualizer.Models;
using SQLPerformanceVisualizer.Services;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class IndexListViewModel : ViewModelBase
{
    private readonly ISqlServerService _service;

    [ObservableProperty] private ObservableCollection<IndexInfo> _indexes = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    private CancellationTokenSource? _cts;

    public IndexListViewModel(ISqlServerService service)
    {
        _service = service;
    }

    public async Task LoadAsync(string database, string schema, string table, string columnName)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        Indexes = [];
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            var items = await _service.GetIndexesAsync(database, schema, table, columnName, _cts.Token);
            Indexes = new ObservableCollection<IndexInfo>(items);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }
}
