using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLPerformanceVisualizer.Models;
using SQLPerformanceVisualizer.Services;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class StatisticDetailViewModel : ViewModelBase
{
    private readonly ISqlServerService _service;

    [ObservableProperty] private string? _commandText;
    [ObservableProperty] private ObservableCollection<StatHeaderInfo> _headerRow = [];
    [ObservableProperty] private ObservableCollection<DensityVectorInfo> _densityVectors = [];
    [ObservableProperty] private ObservableCollection<HistogramStepInfo> _histogramSteps = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty] private bool _isUpdatePopupOpen;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatePreviewText))]
    private bool _useSamplePercent = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatePreviewText))]
    private bool _useFullScan;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatePreviewText))]
    private string _samplePercentInput = "10";
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private string? _updateErrorMessage;

    public string UpdatePreviewText => _statName is null ? "" :
        $"UPDATE STATISTICS {_schema}.{_table} {_statName} WITH {(UseSamplePercent ? $"SAMPLE {SamplePercentInput} PERCENT" : "FULLSCAN")};";

    public event EventHandler<string>? StatisticsUpdated;

    private CancellationTokenSource? _cts;
    private string? _database;
    private string? _schema;
    private string? _table;
    private string? _statName;

    public StatisticDetailViewModel(ISqlServerService service)
    {
        _service = service;
    }

    public async Task LoadAsync(string database, string schema, string table, string statName)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _database = database;
        _schema = schema;
        _table = table;
        _statName = statName;
        CommandText = $"DBCC SHOW_STATISTICS ('{schema}.{table}', '{statName}');";
        OnPropertyChanged(nameof(UpdatePreviewText));
        HeaderRow = [];
        DensityVectors = [];
        HistogramSteps = [];
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            var detail = await _service.GetStatisticDetailAsync(database, schema, table, statName, _cts.Token);
            HeaderRow = detail.Header is null ? [] : new ObservableCollection<StatHeaderInfo>([detail.Header]);
            DensityVectors = new ObservableCollection<DensityVectorInfo>(detail.DensityVectors);
            HistogramSteps = new ObservableCollection<HistogramStepInfo>(detail.HistogramSteps);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    public void Clear()
    {
        _cts?.Cancel();
        _database = null;
        _schema = null;
        _table = null;
        _statName = null;
        CommandText = null;
        OnPropertyChanged(nameof(UpdatePreviewText));
        HeaderRow = [];
        DensityVectors = [];
        HistogramSteps = [];
        ErrorMessage = null;
        IsUpdatePopupOpen = false;
    }

    [RelayCommand]
    private void OpenUpdatePopup()
    {
        UpdateErrorMessage = null;
        IsUpdatePopupOpen = true;
    }

    [RelayCommand]
    private void CancelUpdate()
    {
        IsUpdatePopupOpen = false;
    }

    [RelayCommand]
    private async Task RunUpdateStatisticsAsync()
    {
        if (_database is null || _schema is null || _table is null || _statName is null) return;

        double? samplePercent = null;
        if (UseSamplePercent)
        {
            var normalized = SamplePercentInput.Trim().Replace(',', '.');
            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct) || pct <= 0 || pct > 100)
            {
                UpdateErrorMessage = "Enter a sample percent between 0 and 100.";
                return;
            }
            samplePercent = pct;
        }

        UpdateErrorMessage = null;
        IsUpdating = true;
        try
        {
            await _service.UpdateStatisticsAsync(_database, _schema, _table, _statName, samplePercent);
            IsUpdatePopupOpen = false;
            StatisticsUpdated?.Invoke(this, _statName);
        }
        catch (Exception ex)
        {
            UpdateErrorMessage = ex.Message;
        }
        finally
        {
            IsUpdating = false;
        }
    }
}
