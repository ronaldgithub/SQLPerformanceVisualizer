using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SQLPerformanceVisualizer.Models;
using SQLPerformanceVisualizer.Services;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class TableListViewModel : ViewModelBase
{
    private readonly ISqlServerService _service;

    [ObservableProperty] private ObservableCollection<TableInfo> _tables = [];
    [ObservableProperty] private TableInfo? _selectedTable;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    private CancellationTokenSource? _cts;

    public TableListViewModel(ISqlServerService service)
    {
        _service = service;
    }

    public async Task LoadAsync(string database)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        SelectedTable = null;
        Tables = [];
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            var items = await _service.GetTablesAsync(database, _cts.Token);
            Tables = new ObservableCollection<TableInfo>(items);
            SelectedTable = Tables.FirstOrDefault();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }
}
