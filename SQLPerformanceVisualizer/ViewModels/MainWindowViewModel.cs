using SQLPerformanceVisualizer.Services;

namespace SQLPerformanceVisualizer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ConnectServerViewModel ConnectVm { get; }
    public DatabaseListViewModel DatabaseListVm { get; }
    public TableListViewModel TableListVm { get; }
    public ColumnListViewModel ColumnListVm { get; }
    public IndexListViewModel IndexListVm { get; }
    public StatisticsViewModel StatisticsVm { get; }
    public StatisticDetailViewModel StatisticDetailVm { get; }
    public AiViewModel AiVm { get; }
    public ExecutionPlansViewModel ExecutionPlansVm { get; }

    public MainWindowViewModel(ISqlServerService service, IPlanAnalysisService planAnalysisService)
    {
        ConnectVm = new ConnectServerViewModel(service);
        DatabaseListVm = new DatabaseListViewModel(service);
        TableListVm = new TableListViewModel(service);
        ColumnListVm = new ColumnListViewModel(service);
        IndexListVm = new IndexListViewModel(service);
        StatisticsVm = new StatisticsViewModel(service);
        StatisticDetailVm = new StatisticDetailViewModel(service);
        AiVm = new AiViewModel(service, planAnalysisService);
        ExecutionPlansVm = new ExecutionPlansViewModel(planAnalysisService);

        ConnectVm.Connected += async (_, _) =>
            await DatabaseListVm.LoadAsync();

        DatabaseListVm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(DatabaseListViewModel.SelectedDatabase)) return;
            var db = DatabaseListVm.SelectedDatabase;
            if (db is null) return;
            TableListVm.SelectedTable = null;
            ColumnListVm.SelectedColumn = null;
            IndexListVm.Indexes = [];
            StatisticsVm.SelectedStatistic = null;
            StatisticsVm.Statistics = [];
            await Task.WhenAll(
                TableListVm.LoadAsync(db.Name),
                AiVm.LoadAsync(db.Name));
        };

        TableListVm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(TableListViewModel.SelectedTable)) return;
            var table = TableListVm.SelectedTable;
            var db = DatabaseListVm.SelectedDatabase;
            if (table is null || db is null) return;
            ColumnListVm.SelectedColumn = null;
            await Task.WhenAll(
                ColumnListVm.LoadAsync(db.Name, table.Schema, table.Name),
                IndexListVm.LoadAsync(db.Name, table.Schema, table.Name),
                StatisticsVm.LoadAsync(db.Name, table.Schema, table.Name));
        };

        StatisticsVm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(StatisticsViewModel.SelectedStatistic)) return;
            var stat = StatisticsVm.SelectedStatistic;
            if (stat is null)
            {
                ColumnListVm.ClearHighlight();
                StatisticDetailVm.Clear();
                return;
            }
            var cols = stat.Info.Columns.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
            ColumnListVm.HighlightColumns(cols);

            var table = TableListVm.SelectedTable;
            var db = DatabaseListVm.SelectedDatabase;
            if (table is not null && db is not null)
                await StatisticDetailVm.LoadAsync(db.Name, table.Schema, table.Name, stat.Info.StatName);
        };

        StatisticDetailVm.StatisticsUpdated += async (_, statName) =>
        {
            var table = TableListVm.SelectedTable;
            var db = DatabaseListVm.SelectedDatabase;
            if (table is null || db is null) return;
            await StatisticsVm.LoadAsync(db.Name, table.Schema, table.Name);
            StatisticsVm.MarkJustUpdated(statName);
        };
    }
}
