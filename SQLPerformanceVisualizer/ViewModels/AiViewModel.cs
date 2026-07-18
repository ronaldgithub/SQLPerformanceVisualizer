using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLPerformanceVisualizer.Models;
using SQLPerformanceVisualizer.Services;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class AiViewModel : ViewModelBase
{
    private readonly ISqlServerService _service;
    private readonly IPlanAnalysisService _analysis;

    public IReadOnlyList<QueryStoreMetricItem> Metrics { get; } = QueryStoreMetricItem.All;

    [ObservableProperty] private ObservableCollection<QueryRow> _queries = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private QueryRow? _selectedQuery;
    [ObservableProperty] private QueryStoreMetricItem _selectedMetric = QueryStoreMetricItem.All[0];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty] private string? _selectedSqlText;
    [ObservableProperty] private string? _analysisReport;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private string? _elapsedText;
    [ObservableProperty] private string? _runErrorMessage;
    [ObservableProperty] private bool _isRunPopupOpen;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private bool _isAnalyzing;

    public bool CanRun => SelectedQuery is not null && !IsAnalyzing;

    private CancellationTokenSource? _cts;
    private string? _database;
    private DateTime _runStarted;
    private readonly DispatcherTimer _elapsedTimer;

    public AiViewModel(ISqlServerService service, IPlanAnalysisService analysis)
    {
        _service = service;
        _analysis = analysis;
        _elapsedTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal,
            (_, _) => ElapsedText = FormatElapsed());
        _elapsedTimer.Stop();
    }

    private string FormatElapsed() => (DateTime.Now - _runStarted).ToString(@"m\:ss");

    public async Task LoadAsync(string database)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _database = database;
        SelectedQuery = null;
        Queries = [];
        SelectedSqlText = null;
        AnalysisReport = null;
        StatusText = null;
        RunErrorMessage = null;
        ErrorMessage = null;
        IsRunPopupOpen = false;
        IsLoading = true;
        try
        {
            var items = await _service.GetQueryStoreTopQueriesAsync(database, SelectedMetric.Metric, _cts.Token);
            Queries = new ObservableCollection<QueryRow>(items.Select(i => new QueryRow(i)));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    partial void OnSelectedQueryChanged(QueryRow? value)
    {
        SelectedSqlText = value?.Info.SqlText;
    }

    partial void OnSelectedMetricChanged(QueryStoreMetricItem value)
    {
        if (_database is not null)
            _ = LoadAsync(_database);
    }

    [RelayCommand]
    private void OpenRunPopup()
    {
        if (SelectedQuery is null) return;
        RunErrorMessage = null;
        IsRunPopupOpen = true;
    }

    [RelayCommand]
    private void CancelRun()
    {
        IsRunPopupOpen = false;
    }

    [RelayCommand]
    private async Task ConfirmRunAsync()
    {
        var row = SelectedQuery;
        if (row is null || _database is null) return;

        IsRunPopupOpen = false;
        RunErrorMessage = null;
        AnalysisReport = null;
        row.IsBusy = true;
        IsAnalyzing = true;
        _runStarted = DateTime.Now;
        ElapsedText = FormatElapsed();
        _elapsedTimer.Start();
        try
        {
            StatusText = "Step 1/3 — fetching estimated plan from Query Store…";
            var estimatedXml = await _service.GetQueryStorePlanXmlAsync(_database, row.Info.PlanId);
            await _analysis.SavePlanAsync(_database, row.Info.QueryId, estimatedXml, ".estimated");

            StatusText = "Step 2/3 — executing query (capturing actual plan)…";
            var actualXml = await _service.ExecuteQueryCaptureActualPlanAsync(_database, row.Info.SqlText);
            var planPath = await _analysis.SavePlanAsync(_database, row.Info.QueryId, actualXml, "");

            StatusText = "Step 3/3 — Claude is analyzing the plan… (this can take a few minutes)";
            var result = await _analysis.AnalyzePlanAsync(planPath);
            AnalysisReport = result.Markdown;
            row.IsAnalyzed = true;
            StatusText = $"Done in {FormatElapsed()} — report saved to {result.ReportPath}";
        }
        catch (Exception ex)
        {
            RunErrorMessage = ex.Message;
            StatusText = null;
        }
        finally
        {
            _elapsedTimer.Stop();
            ElapsedText = null;
            row.IsBusy = false;
            IsAnalyzing = false;
        }
    }
}
