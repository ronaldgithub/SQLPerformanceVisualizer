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
├── Models/          — plain C# records: DatabaseInfo, TableInfo, ColumnInfo, IndexInfo, StatisticInfo, StatisticDetailInfo (+ StatHeaderInfo/DensityVectorInfo/HistogramStepInfo)
├── Services/        — ISqlServerService + SqlServerService (all SQL queries live here)
├── ViewModels/      — one ViewModel per panel + ColumnRow/StatisticRow/IndexRow wrappers + MainWindowViewModel (coordinator)
└── Views/           — MainWindow.axaml (single window, top bar + tabbed content: Tables / Indexes / Statistics)
```

## UI Layout

```text
┌──────────────────────────────────────────────────────────────────────┐
│  SQL Server: [localhost] ● [Connect]   Database: [▼ combo]           │
├──────────────────────────────────────────────────────────────────────┤
│  [ Tables ] [ Indexes ] [ Statistics ]                                │
├──────────────────────────────────────────────────────────────────────┤
│  Tables tab:                                                          │
│  ┌──────────────┬───────────────────────────────────────────────────┐│
│  │  Tables      │  Columns                                          ││
│  │  dbo.X n d i │  Id   int   NO                                    ││
│  │  dbo.Y n d i │  Name nva…  (highlight on stat select)             ││
│  └──────────────┴───────────────────────────────────────────────────┘│
│                                                                        │
│  Indexes tab:     full-width Indexes grid, per-row [Rebuild]          │
│                    [Reorganize] buttons + red dot while running,      │
│                    Fragmentation % column                             │
│                                                                        │
│  Statistics tab:                                                      │
│  ┌──────────────┬───────────────────────────────────────────────────┐│
│  │  ● Statistics│  Statistic Detail              [Update Stats ▾]   ││
│  │  list        │  DBCC SHOW_STATISTICS ('schema.table', 'stat');   ││
│  │  (red dot =  │  Stat Header  (1 row: rows, steps, density, ...) ││
│  │   just       │  Density Vector  (all density, avg length, cols) ││
│  │   updated;   │  Histogram  (range_hi_key, range_rows, eq_rows,  ││
│  │   click row  │              distinct_range_rows, avg_range_rows)││
│  │   → detail   │                                                   ││
│  │   + highlight│  "Update Stats" opens a popup: sample % or        ││
│  │   columns)   │  full scan, then runs UPDATE STATISTICS on        ││
│  │              │  just the selected stat.                         ││
│  └──────────────┴───────────────────────────────────────────────────┘│
└──────────────────────────────────────────────────────────────────────┘
```

## Architecture Rules

- No business logic in code-behind (`.axaml.cs` files stay empty except `InitializeComponent()`).
- All SQL goes in `SqlServerService`. ViewModels call service methods, never `SqlConnection` directly.
- Use `async/await` for all SQL calls. Each ViewModel maintains its own `CancellationTokenSource`; cancel and replace it on each new selection to prevent stale results.
- AXAML uses compiled bindings (`x:DataType`). Set `x:DataType` on `Border` elements when the `DataContext` changes from the parent. Use `{x:Static ClassName.Instance}` for converters.
- `MainWindowViewModel` owns all child ViewModels and wires the cascade via `PropertyChanged` subscriptions.
- Column highlight uses `ColumnRow` (ObservableObject wrapper around `ColumnInfo` with `IsHighlighted`). The Columns panel is a `ListBox`; row background binds to `IsHighlighted` via `HighlightBrushConverter`.
- The "just updated"/"in progress" red dots use the same wrapper pattern: `StatisticRow`/`IndexRow` (ObservableObject wrapping the model with `IsJustUpdated`/`IsBusy`), rendered via a `DataGridTemplateColumn`/`Ellipse` rather than a converter.
- Popups (e.g. the Update Stats picker) are plain Avalonia `Popup` controls with `IsOpen` two-way bound to a ViewModel bool and `PlacementTarget="{Binding #ElementName}"` — no code-behind needed; this keeps the "no business logic in code-behind" rule intact even for transient UI like dialogs.
- Per-row command buttons inside a `DataGridTemplateColumn` (e.g. Rebuild/Reorganize) can't bind `Command` to the row's own DataContext (the row only has `Info`/`IsBusy`) — bind via `{Binding #GridName.((vm:SomeViewModel)DataContext).SomeCommand}` with `CommandParameter="{Binding}"` (the row itself) instead. Requires `Name="GridName"` on the `DataGrid`.

## Cascade Flow

```text
ConnectVm.Connected         → DatabaseListVm.LoadAsync()
DatabaseListVm.Selected     → TableListVm.LoadAsync(db)
TableListVm.Selected        → ColumnListVm.LoadAsync(db, schema, table)
                            → IndexListVm.LoadAsync(db, schema, table)    [parallel]
                            → StatisticsVm.LoadAsync(db, schema, table)   [parallel]
StatisticsVm.Selected       → ColumnListVm.HighlightColumns(stat.Info.Columns)
                            → StatisticDetailVm.LoadAsync(db, schema, table, stat.Info.StatName)
StatisticDetailVm.StatisticsUpdated → StatisticsVm.LoadAsync(db, schema, table) → StatisticsVm.MarkJustUpdated(statName)
```

Auto-selection: first table is selected after load; first column is selected after load.

## SQL Notes

- `GetDatabasesAsync`: excludes system databases (`master`, `model`, `msdb`, `tempdb`) and `DW*` databases.
- `GetTablesAsync`: filters `sys.tables WHERE is_ms_shipped = 0` to exclude shipped tables (e.g. `sysdiagrams`); row count, data size (KB), and index size (KB) all come from `sys.dm_db_partition_stats` (index_id 0/1 = data, the rest = index pages). Alias must NOT be `RowCount` — it is a T-SQL reserved word; use `rc`.
- `GetStatisticsAsync`: uses `sys.dm_db_stats_properties` (no elevated permissions needed). Ordered PK stat first, then user/index stats, then `_WA_Sys_*` auto-created stats last (`sys.stats.auto_created`), via a `LEFT JOIN sys.indexes` matched on `(object_id, name)`.
- `GetIndexesAsync`: returns all indexes for the table (`i.index_id > 0` excludes the heap pseudo-row); ordered PK first, then by name. Uses FOR XML PATH subqueries to concatenate key and included columns. Pages/size come from `SUM(sys.dm_db_partition_stats.used_page_count)` per `(object_id, index_id)` — size KB = pages × 8. Fragmentation % comes from `sys.dm_db_index_physical_stats(DB_ID(), object_id, index_id, NULL, 'LIMITED')` — `'LIMITED'` mode only scans parent-level pages, so it's cheap enough to run per-row.
- `RebuildIndexAsync`/`ReorganizeIndexAsync`: run `ALTER INDEX [name] ON [schema].[table] REBUILD/REORGANIZE;`, scoped to one index, triggered directly from a per-row button (no confirmation popup, unlike Update Stats) — `IndexListViewModel` reloads the whole index list on success so Pages/Size/Fragmentation reflect the result immediately. `CommandTimeout = 0` since `REBUILD` on a large table can run long.
- `GetStatisticDetailAsync`: runs `DBCC SHOW_STATISTICS (...) WITH STAT_HEADER, DENSITY_VECTOR, HISTOGRAM` and reads all 3 result sets off one `SqlDataReader` via `NextResultAsync`. DBCC doesn't support `@parameters` for its table/stat arguments, so the command text is built with `QuoteIdentifier`/`EscapeLiteral` helpers instead of string interpolation directly into SQL. Requires the connecting user to have at least `SELECT` permission on the table (SQL 2016 SP1+) or `ALTER`/`db_owner` on older versions — surfaced via the normal `ErrorMessage` pattern if permissions are insufficient, same as any other query. The "Updated" column in `STAT_HEADER` has been observed coming back as a string rather than a native `datetime` on some SQL Server versions — read defensively via `Convert.ToDateTime(value, CultureInfo.InvariantCulture)`, not `reader.GetDateTime`.
- `UpdateStatisticsAsync`: runs `UPDATE STATISTICS [schema].[table] [statname] WITH SAMPLE x PERCENT;` or `WITH FULLSCAN;`, scoped to exactly the one statistic the user has selected (never the whole table). `CommandTimeout = 0` (no timeout) since `FULLSCAN` on a large table is expected to run long and is an explicit, user-triggered maintenance action.
- Per-database queries swap `Database=master` → target DB via `Regex.Replace` in `ConnectionStringForDb`.

## Coding Conventions

- `[ObservableProperty]` fields are `private` and camelCase (e.g. `private string _serverName`); the generated public property is PascalCase (`ServerName`).
- Errors surfaced via `string? ErrorMessage` property; never throw from a ViewModel.
- Connection strings always include `TrustServerCertificate=True` (required for SqlClient 4+).
- Number formatting is split by purpose: row counts/sizes/fragmentation (`TableInfo.RowCountDisplay`/`DataSizeDisplay`/`IndexSizeDisplay`, `IndexInfo.PagesDisplay`/`SizeMBDisplay`/`FragmentationDisplay`) use a `nl-NL` (European) `CultureInfo` — period thousands separator, comma decimal — per explicit user preference; everything from DBCC/DMV detail output (`StatisticInfo.SamplingPercentText`, `StatHeaderInfo`, `DensityVectorInfo`, `HistogramStepInfo`) uses `CultureInfo.InvariantCulture` (period decimal). Don't assume one culture applies everywhere — check the model.
- `DataTypeDisplay` on `ColumnInfo` appends `(n)` or `(max)` for string/binary types; `MaxLength = -1` means `MAX`.
- Raw SQL Server values that come back upper/mixed-case (`IS_NULLABLE` as `YES`/`NO`, `sys.indexes.type_desc` as `CLUSTERED`/`NONCLUSTERED`) get a `*Display` lowercase property (`ColumnInfo.IsNullableDisplay`, `IndexInfo.IndexTypeDisplay`) to match the Columns panel's lowercase style — bind to the `*Display` property in AXAML, never the raw field.

## Known Gotchas

- `Avalonia.Controls.DataGrid` is a separate NuGet package; max version is `12.0.0` (not `12.0.3`). Its Fluent styles must be registered in `App.axaml`.
- `TextBox.Watermark` is obsolete in Avalonia 12 — use `PlaceholderText`.
- The Avalonia template does NOT include `<ImplicitUsings>enable</ImplicitUsings>` — must be added manually.
- `HorizontalContentAlignment` on `ListBox` does not exist in Avalonia 12; set it via `ListBox.Styles` on `ListBoxItem`.
- `BoolConverters.Not` (built into Avalonia) can be used as `{x:Static BoolConverters.Not}` without extra packages.
