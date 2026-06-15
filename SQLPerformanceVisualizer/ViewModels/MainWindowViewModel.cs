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

    public MainWindowViewModel(ISqlServerService service)
    {
        ConnectVm = new ConnectServerViewModel(service);
        DatabaseListVm = new DatabaseListViewModel(service);
        TableListVm = new TableListViewModel(service);
        ColumnListVm = new ColumnListViewModel(service);
        IndexListVm = new IndexListViewModel(service);
        StatisticsVm = new StatisticsViewModel(service);

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
            await TableListVm.LoadAsync(db.Name);
        };

        TableListVm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(TableListViewModel.SelectedTable)) return;
            var table = TableListVm.SelectedTable;
            var db = DatabaseListVm.SelectedDatabase;
            if (table is null || db is null) return;
            ColumnListVm.SelectedColumn = null;
            IndexListVm.Indexes = [];
            await Task.WhenAll(
                ColumnListVm.LoadAsync(db.Name, table.Schema, table.Name),
                StatisticsVm.LoadAsync(db.Name, table.Schema, table.Name));
        };

        ColumnListVm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(ColumnListViewModel.SelectedColumn)) return;
            var col = ColumnListVm.SelectedColumn;
            var table = TableListVm.SelectedTable;
            var db = DatabaseListVm.SelectedDatabase;
            if (col is null || table is null || db is null) return;
            await IndexListVm.LoadAsync(db.Name, table.Schema, table.Name, col.Info.ColumnName);
        };

        StatisticsVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(StatisticsViewModel.SelectedStatistic)) return;
            var stat = StatisticsVm.SelectedStatistic;
            if (stat is null)
            {
                ColumnListVm.ClearHighlight();
                return;
            }
            var cols = stat.Columns.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);
            ColumnListVm.HighlightColumns(cols);
        };
    }
}
