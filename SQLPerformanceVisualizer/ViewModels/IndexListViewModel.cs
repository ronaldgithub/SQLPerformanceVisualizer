using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLPerformanceVisualizer.Services;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class IndexListViewModel : ViewModelBase
{
    private readonly ISqlServerService _service;

    [ObservableProperty] private ObservableCollection<IndexRow> _indexes = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    private CancellationTokenSource? _cts;
    private string? _database;
    private string? _schema;
    private string? _table;

    public IndexListViewModel(ISqlServerService service)
    {
        _service = service;
    }

    public async Task LoadAsync(string database, string schema, string table)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _database = database;
        _schema = schema;
        _table = table;
        Indexes = [];
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            var items = await _service.GetIndexesAsync(database, schema, table, _cts.Token);
            Indexes = new ObservableCollection<IndexRow>(items.Select(i => new IndexRow(i)));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task RebuildAsync(IndexRow row)
    {
        if (_database is null || _schema is null || _table is null) return;
        ErrorMessage = null;
        row.IsBusy = true;
        try
        {
            await _service.RebuildIndexAsync(_database, _schema, _table, row.Info.IndexName);
            await LoadAsync(_database, _schema, _table);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            row.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReorganizeAsync(IndexRow row)
    {
        if (_database is null || _schema is null || _table is null) return;
        ErrorMessage = null;
        row.IsBusy = true;
        try
        {
            await _service.ReorganizeIndexAsync(_database, _schema, _table, row.Info.IndexName);
            await LoadAsync(_database, _schema, _table);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            row.IsBusy = false;
        }
    }
}
