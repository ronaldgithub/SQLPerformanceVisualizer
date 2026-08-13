using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLPerformanceVisualizer.Services;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class ExecutionPlansViewModel : ViewModelBase
{
    private const string DefaultFolder = @"C:\github\SQLPerformanceVisualizer\executionplans";
    private const string DefaultResultsFolder = @"C:\github\SQLPerformanceVisualizer\executionplans\results";

    private readonly IPlanAnalysisService _analysis;

    [ObservableProperty] private string _folderPath = DefaultFolder;
    [ObservableProperty] private string _resultsFolderPath = DefaultResultsFolder;
    [ObservableProperty] private ObservableCollection<PlanFileRow> _planFiles = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAnalyze))]
    [NotifyPropertyChangedFor(nameof(CanOpenReport))]
    private PlanFileRow? _selectedPlanFile;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _selectedPlanFilePath;

    [ObservableProperty] private string? _analysisReport;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private string? _elapsedText;
    [ObservableProperty] private string? _runErrorMessage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAnalyze))]
    [NotifyPropertyChangedFor(nameof(CanOpenReport))]
    private bool _isAnalyzing;

    public bool CanAnalyze => SelectedPlanFile is not null && !IsAnalyzing;
    public bool CanOpenReport => SelectedPlanFile is not null && !IsAnalyzing;

    private DateTime _runStarted;
    private readonly DispatcherTimer _elapsedTimer;

    public ExecutionPlansViewModel(IPlanAnalysisService analysis)
    {
        _analysis = analysis;
        _elapsedTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal,
            (_, _) => ElapsedText = FormatElapsed());
        _elapsedTimer.Stop();
        Refresh();
    }

    private string FormatElapsed() => (DateTime.Now - _runStarted).ToString(@"m\:ss");

    partial void OnSelectedPlanFileChanged(PlanFileRow? value)
    {
        SelectedPlanFilePath = value?.FilePath;
        AnalysisReport = null;
        RunErrorMessage = null;
        StatusText = null;
    }

    partial void OnFolderPathChanged(string value) => Refresh();

    [RelayCommand]
    private void Refresh()
    {
        ErrorMessage = null;
        SelectedPlanFile = null;
        try
        {
            if (!Directory.Exists(FolderPath))
            {
                PlanFiles = [];
                ErrorMessage = $"Folder not found: {FolderPath}";
                return;
            }
            var files = Directory.GetFiles(FolderPath, "*.sqlplan", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => new PlanFileRow(f));
            PlanFiles = new ObservableCollection<PlanFileRow>(files);
        }
        catch (Exception ex)
        {
            PlanFiles = [];
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        var row = SelectedPlanFile;
        if (row is null) return;

        RunErrorMessage = null;
        var reportPath = Path.Combine(ResultsFolderPath, Path.GetFileNameWithoutExtension(row.FilePath) + ".md");
        try
        {
            AnalysisReport = await _analysis.ReadReportAsync(reportPath);
            row.IsAnalyzed = true;
            StatusText = $"Opened existing report — {reportPath}";
        }
        catch (Exception ex)
        {
            RunErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        var row = SelectedPlanFile;
        if (row is null) return;

        RunErrorMessage = null;
        AnalysisReport = null;
        row.IsBusy = true;
        IsAnalyzing = true;
        _runStarted = DateTime.Now;
        ElapsedText = FormatElapsed();
        _elapsedTimer.Start();
        try
        {
            StatusText = "Claude is analyzing the plan… (this can take a few minutes)";
            var reportPath = Path.Combine(ResultsFolderPath, Path.GetFileNameWithoutExtension(row.FilePath) + ".md");
            var result = await _analysis.AnalyzePlanAsync(row.FilePath, reportPath);
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
