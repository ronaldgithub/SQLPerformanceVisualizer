using CommunityToolkit.Mvvm.ComponentModel;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class PlanFileRow : ObservableObject
{
    public string FilePath { get; }
    public string FileName { get; }

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isAnalyzed;

    public PlanFileRow(string filePath)
    {
        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
    }
}
