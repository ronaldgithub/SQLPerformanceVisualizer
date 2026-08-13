using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLPerformanceVisualizer.Models;
using SQLPerformanceVisualizer.Services;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class ExecutionPlanAnimationViewModel : ViewModelBase
{
    private readonly IPlanAnalysisService _analysis;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    private PlanOperatorNode? _plan;
    [ObservableProperty] private bool _isSerialPlan = true;
    [ObservableProperty] private string? _sourcePlanFileName;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayButtonText))]
    private bool _isPlaying;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayButtonText))]
    private bool _isPaused;

    public bool HasPlan => Plan is not null;
    public string PlayButtonText => IsPlaying ? "⏸ Pause" : IsPaused ? "▶ Go" : "▶ Replay the scan";

    public ExecutionPlanAnimationViewModel(IPlanAnalysisService analysis)
    {
        _analysis = analysis;
    }

    public async Task LoadAsync(string planPath)
    {
        ErrorMessage = null;
        IsPlaying = false;
        IsPaused = false;
        SourcePlanFileName = Path.GetFileName(planPath);
        try
        {
            var tree = await _analysis.ParsePlanTreeAsync(planPath);
            Plan = tree.Root;
            IsSerialPlan = tree.IsSerial;
        }
        catch (Exception ex)
        {
            Plan = null;
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void TogglePlay() => IsPlaying = !IsPlaying;
}
