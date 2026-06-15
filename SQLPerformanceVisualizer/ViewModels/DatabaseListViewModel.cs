using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SQLPerformanceVisualizer.Models;
using SQLPerformanceVisualizer.Services;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class DatabaseListViewModel : ViewModelBase
{
    private readonly ISqlServerService _service;

    [ObservableProperty] private ObservableCollection<DatabaseInfo> _databases = [];
    [ObservableProperty] private DatabaseInfo? _selectedDatabase;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    private CancellationTokenSource? _cts;

    public DatabaseListViewModel(ISqlServerService service)
    {
        _service = service;
    }

    public async Task LoadAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            var items = await _service.GetDatabasesAsync(_cts.Token);
            Databases = new ObservableCollection<DatabaseInfo>(items);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }
}
