# SQLPerformanceVisualizer — Developer Guide

## Project Purpose

Avalonia C# desktop application for SQL Server maintenance. Connects to a SQL Server instance and lets the user drill down from databases → tables → columns → indexes → statistics to support informed maintenance decisions (index rebuilds, statistics updates, etc.).

## Tech Stack

- **UI**: Avalonia 12.0.3, Fluent dark theme
- **Framework**: .NET 8 (net8.0)
- **Pattern**: MVVM via CommunityToolkit.Mvvm 8.4.x (`[ObservableProperty]`, `[RelayCommand]`)
- **Data access**: Microsoft.Data.SqlClient 6.1.4
- **DI**: Microsoft.Extensions.DependencyInjection (registered in `App.axaml.cs`)

## Build & Run

```sh
dotnet build
dotnet run --project SQLPerformanceVisualizer
```

## Project Layout

```text
SQLPerformanceVisualizer/
├── Converters/      — HighlightBrushConverter (bool → blue tint brush for column highlight)
├── Models/          — plain C# records: DatabaseInfo, TableInfo, ColumnInfo, IndexInfo, StatisticInfo
├── Services/        — ISqlServerService + SqlServerService (all SQL queries live here)
├── ViewModels/      — one ViewModel per panel + ColumnRow wrapper + MainWindowViewModel (coordinator)
└── Views/           — MainWindow.axaml (single window, top bar + 3-column grid)
```

## UI Layout

```text
┌──────────────────────────────────────────────────────────────────────┐
│  SQL Server: [localhost] ● [Connect]   Database: [▼ combo]           │
├──────────────┬──────────────┬─────────────────────────────────────────┤
│  Tables      │  Columns     │  Indexes                                │
│  dbo.X [n]   │  Id  int  NO │                                         │
│  dbo.Y [n]   │  Name nva…   ├─────────────────────────────────────────┤
│              │  (highlight  │  Statistics                             │
│              │   on stat    │  (click row → highlights columns)       │
│              │   select)    │                                         │
└──────────────┴──────────────┴─────────────────────────────────────────┘
```

## Architecture Rules

- No business logic in code-behind (`.axaml.cs` files stay empty except `InitializeComponent()`).
- All SQL goes in `SqlServerService`. ViewModels call service methods, never `SqlConnection` directly.
- Use `async/await` for all SQL calls. Each ViewModel maintains its own `CancellationTokenSource`; cancel and replace it on each new selection to prevent stale results.
- AXAML uses compiled bindings (`x:DataType`). Set `x:DataType` on `Border` elements when the `DataContext` changes from the parent. Use `{x:Static ClassName.Instance}` for converters.
- `MainWindowViewModel` owns all child ViewModels and wires the cascade via `PropertyChanged` subscriptions.
- Column highlight uses `ColumnRow` (ObservableObject wrapper around `ColumnInfo` with `IsHighlighted`). The Columns panel is a `ListBox`; row background binds to `IsHighlighted` via `HighlightBrushConverter`.

## Cascade Flow

```text
ConnectVm.Connected         → DatabaseListVm.LoadAsync()
DatabaseListVm.Selected     → TableListVm.LoadAsync(db)
TableListVm.Selected        → ColumnListVm.LoadAsync(db, schema, table)
                            → StatisticsVm.LoadAsync(db, schema, table)   [parallel]
ColumnListVm.Selected       → IndexListVm.LoadAsync(db, schema, table, column)
StatisticsVm.Selected       → ColumnListVm.HighlightColumns(stat.Columns)
```

Auto-selection: first table is selected after load; first column is selected after load.

## SQL Notes

- `GetDatabasesAsync`: excludes system databases (`master`, `model`, `msdb`, `tempdb`) and `DW*` databases.
- `GetTablesAsync`: joins `sys.objects WHERE is_ms_shipped = 0` to exclude shipped tables (e.g. `sysdiagrams`); includes row count via `sys.partitions`. Alias must NOT be `RowCount` — it is a T-SQL reserved word; use `rc`.
- `GetStatisticsAsync`: uses `sys.dm_db_stats_properties` (no elevated permissions needed).
- `GetIndexesAsync`: uses FOR XML PATH subqueries to concatenate key and included columns.
- Per-database queries swap `Database=master` → target DB via `Regex.Replace` in `ConnectionStringForDb`.

## Coding Conventions

- `[ObservableProperty]` fields are `private` and camelCase (e.g. `private string _serverName`); the generated public property is PascalCase (`ServerName`).
- Errors surfaced via `string? ErrorMessage` property; never throw from a ViewModel.
- Connection strings always include `TrustServerCertificate=True` (required for SqlClient 4+).
- Number formatting uses `CultureInfo.InvariantCulture` (period as decimal separator) — see `StatisticInfo.SamplingPercentText` and `TableInfo.FullName`.
- `DataTypeDisplay` on `ColumnInfo` appends `(n)` or `(max)` for string/binary types; `MaxLength = -1` means `MAX`.

## Known Gotchas

- `Avalonia.Controls.DataGrid` is a separate NuGet package; max version is `12.0.0` (not `12.0.3`). Its Fluent styles must be registered in `App.axaml`.
- `TextBox.Watermark` is obsolete in Avalonia 12 — use `PlaceholderText`.
- The Avalonia template does NOT include `<ImplicitUsings>enable</ImplicitUsings>` — must be added manually.
- `HorizontalContentAlignment` on `ListBox` does not exist in Avalonia 12; set it via `ListBox.Styles` on `ListBoxItem`.
- `BoolConverters.Not` (built into Avalonia) can be used as `{x:Static BoolConverters.Not}` without extra packages.
