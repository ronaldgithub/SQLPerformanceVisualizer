using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SQLPerformanceVisualizer.Services;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class ColumnListViewModel : ViewModelBase
{
    private readonly ISqlServerService _service;

    [ObservableProperty] private ObservableCollection<ColumnRow> _columns = [];
    [ObservableProperty] private ColumnRow? _selectedColumn;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    private CancellationTokenSource? _cts;

    public ColumnListViewModel(ISqlServerService service)
    {
        _service = service;
    }

    public async Task LoadAsync(string database, string schema, string table)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        SelectedColumn = null;
        Columns = [];
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            var items = await _service.GetColumnsAsync(database, schema, table, _cts.Token);
            Columns = new ObservableCollection<ColumnRow>(items.Select(c => new ColumnRow(c)));
            SelectedColumn = Columns.FirstOrDefault();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    public void HighlightColumns(IEnumerable<string> columnNames)
    {
        var names = new HashSet<string>(columnNames, StringComparer.OrdinalIgnoreCase);
        foreach (var col in Columns)
            col.IsHighlighted = names.Contains(col.Info.ColumnName);
    }

    public void ClearHighlight()
    {
        foreach (var col in Columns)
            col.IsHighlighted = false;
    }
}
