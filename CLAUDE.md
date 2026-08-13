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
├── Controls/        — MarkdownViewer (dependency-free markdown renderer for AI reports; Markdown.Avalonia does NOT support Avalonia 12); ExecutionPlanAnimationControl (dependency-free animated diagram of a showplan operator tree — Canvas + DispatcherTimer, no charting/animation library)
├── Converters/      — HighlightBrushConverter (bool → blue tint brush for column highlight)
├── Models/          — plain C# records: DatabaseInfo, TableInfo, ColumnInfo, IndexInfo, StatisticInfo, StatisticDetailInfo (+ StatHeaderInfo/DensityVectorInfo/HistogramStepInfo), QueryStoreQueryInfo, QueryStoreMetric(+Item), PlanOperatorNode(+ThreadRowStat), ExecutionPlanTree
├── Services/        — ISqlServerService + SqlServerService (all SQL queries live here); IPlanAnalysisService + PlanAnalysisService (all file I/O and Claude CLI process spawning — nowhere else); ShowPlanXmlParser (pure showplan-XML → PlanOperatorNode tree parser, no I/O, called by PlanAnalysisService)
├── ViewModels/      — one ViewModel per panel + ColumnRow/StatisticRow/IndexRow/QueryRow/PlanFileRow wrappers + MainWindowViewModel (coordinator)
└── Views/           — MainWindow.axaml (single window, top bar + tabbed content: Tables / Indexes / Statistics / AI (QueryStore) / AI (Executionplans) / Executionplan Animation)
```

## UI Layout

```text
┌──────────────────────────────────────────────────────────────────────┐
│  SQL Server: [localhost] ● [Connect]   Database: [▼ combo]           │
├──────────────────────────────────────────────────────────────────────┤
│  [Tables][Indexes][Statistics][AI (QueryStore)][AI (Executionplans)] │
│  [Executionplan Animation]                                            │
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
│                                                                        │
│  AI (QueryStore) tab:                                                 │
│  ┌──────────────┬───────────────────────────────────────────────────┐│
│  │ Query Store  │  AI Plan Analysis      [Execute + Analyze]        ││
│  │ top 25       │  SQL (full text of selected query, monospace)     ││
│  │ queries      │  AI Analysis Report (markdown as plain text)      ││
│  │ (metric      │                                                   ││
│  │  dropdown;   │  Button opens a confirmation popup (query really  ││
│  │  red dot =   │  runs!), then: capture actual plan → save files → ││
│  │  running,    │  claude -p runs the query-plan-analysis skill →   ││
│  │  green dot = │  report saved as .md + shown in the pane.         ││
│  │  analyzed)   │                                                   ││
│  └──────────────┴───────────────────────────────────────────────────┘│
│                                                                        │
│  AI (Executionplans) tab — ad-hoc analysis of any .sqlplan dropped   │
│  into a folder (no SQL connection needed):                            │
│  ┌──────────────┬───────────────────────────────────────────────────┐│
│  │ Plan folder  │  AI Plan Analysis          [Open] [Analyze]       ││
│  │ + Results    │  Plan File (full path, monospace)                 ││
│  │ folder       │  AI Analysis Report (rendered markdown)            ││
│  │ (TextBoxes)  │                                                   ││
│  │ Plan files   │  Analyze: same claude -p flow as AI (QueryStore). ││
│  │ (red dot =   │  Open: reads the existing results/<name>.md       ││
│  │  busy, green │  straight off disk — no Claude call, instant.     ││
│  │  = analyzed) │                                                   ││
│  └──────────────┴───────────────────────────────────────────────────┘│
│                                                                        │
│  Executionplan Animation tab — driven by whichever plan is selected  │
│  in AI (Executionplans); no picker of its own:                        │
│  ┌───────────────────────────────────────────────────────────────────┐│
│  │  Plan: <filename>                              [▶ Replay/⏸/▶Go] ││
│  │  ELAPSED  ROWS SCANNED  ROWS OUT      (live stats, tick on Play) ││
│  │  SELF ELAPSED TIME, BY OPERATOR  (stacked bar; serial plans only)││
│  │  ┌────────┐      ┌────────┐      ┌────────┐      ┌───────────┐  ││
│  │  │ Scan A │─────▶│  Join  │─────▶│ Filter │─────▶│ ROWS OUT  │  ││
│  │  └────────┘      └────────┘      └────────┘      └───────────┘  ││
│  │  ┌────────┐ (thread grid, only under parallel operators —       ││
│  │  │ALL THR.│  header + one row per worker thread, counts animate ││
│  │  │Thread 0│  up from 0 on Play; e.g. one thread at 10M rows     ││
│  │  │Thread 1│  while its siblings sit at 0 = visible skew)        ││
│  │  └────────┘                                                      ││
│  │  Dots flow scan→scan→join→...→ROWS OUT, discarded (flash) or    ││
│  │  passed on per operator's actual-rows-vs-children-rows ratio.    ││
│  └───────────────────────────────────────────────────────────────────┘│
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
- A custom control that owns real interactive/animated state (not just rendering, like `MarkdownViewer`) — see `ExecutionPlanAnimationControl` — still keeps all its logic in its own C# class, not a View's code-behind; the ViewModel only supplies data (`Plan`) and playback intent (`IsPlaying`/`IsPaused`) via two-way `StyledProperty`s, never frame-by-frame animation logic.
- When a control needs to programmatically change its own two-way-bound `StyledProperty` (e.g. flipping `IsPlaying` back to `false` when an animation finishes on its own) without breaking the binding, use `SetCurrentValue`, not the property setter/`SetValue` — and guard against the control re-handling its own resulting `OnPropertyChanged` call with a simple reentrancy flag (see `ExecutionPlanAnimationControl._internalUpdate`/`SetPlaybackState`).

## Cascade Flow

```text
ConnectVm.Connected         → DatabaseListVm.LoadAsync()
DatabaseListVm.Selected     → TableListVm.LoadAsync(db)
                            → AiVm.LoadAsync(db)                       [parallel; Query Store is DB-scoped]
TableListVm.Selected        → ColumnListVm.LoadAsync(db, schema, table)
                            → IndexListVm.LoadAsync(db, schema, table)    [parallel]
                            → StatisticsVm.LoadAsync(db, schema, table)   [parallel]
StatisticsVm.Selected       → ColumnListVm.HighlightColumns(stat.Info.Columns)
                            → StatisticDetailVm.LoadAsync(db, schema, table, stat.Info.StatName)
StatisticDetailVm.StatisticsUpdated → StatisticsVm.LoadAsync(db, schema, table) → StatisticsVm.MarkJustUpdated(statName)
ExecutionPlansVm.SelectedPlanFile → ExecutionPlanAnimationVm.LoadAsync(planPath)   [no SQL connection needed — either flow works standalone]
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
- `GetQueryStoreTopQueriesAsync`: first checks `sys.database_query_store_options.actual_state` and `THROW`s a friendly "Query Store is not enabled" error (surfaces via `ErrorMessage`). Then top 25 rows joining `sys.query_store_query/query_text/plan/runtime_stats`, grouped per `(query_id, plan_id)` — one row per plan. Averages are execution-weighted (`SUM(avg_x * count_executions) / SUM(count_executions)`); durations converted µs → ms. `ORDER BY` expression comes from a C# `switch` on `QueryStoreMetric` (never user input). `last_execution_time` is `datetimeoffset` — read via a type switch, not `GetDateTime`.
- `ExecuteQueryCaptureActualPlanAsync`: prepends `SET STATISTICS XML ON;` to the Query Store SQL text and re-executes it for real (`CommandTimeout = 0`). Iterates all result sets; the showplan set has a single column whose name contains `Showplan` (full name: `Microsoft SQL Server 2005 XML Showplan`) — data rows are drained and discarded, the last plan XML wins. **Limitation**: auto-parameterized Query Store texts starting with `(@p1 ...)` fail with a syntax error when re-executed; the error surfaces in the popup's `RunErrorMessage`.
- `GetQueryStorePlanXmlAsync`: fetches the *estimated* (compiled) plan XML for one `plan_id` from `sys.query_store_plan`.
- Per-database queries swap `Database=master` → target DB via `Regex.Replace` in `ConnectionStringForDb`.

## AI Tab / PlanAnalysisService

- `PlanAnalysisService` owns all file I/O and process spawning; nothing else in the app touches `System.IO.File` or `Process`.
- Plans and reports are saved to `Documents\SQLPerformanceVisualizer\plans\<database>\query_<queryid>_<yyyyMMdd_HHmmss>.sqlplan` (+ `.estimated.sqlplan` with the Query Store compiled plan, and the analysis as a matching `.md`). Database name is sanitized for invalid filename chars.
- Analysis shells out to the Claude Code CLI headlessly: `claude -p "<prompt>" --output-format text --allowedTools Skill Read Glob Grep "Bash(python *)"`, working directory = the plans folder, prompt names the `query-plan-analysis` skill (erikdarling `sqlserver-query-plans` plugin) and the absolute plan path. stdout is the markdown report.
- CLI resolution: probes `%USERPROFILE%\.local\bin` then every `PATH` entry for `claude.exe`/`claude.cmd`. A `.exe` is started directly; the npm `claude.cmd` shim must be launched via `cmd.exe /c` (CreateProcess can't run `.cmd` with `UseShellExecute=false`).
- The tool allowlist is deliberately minimal — plan XML is untrusted input (the skill itself warns about this); never pass `--dangerously-skip-permissions`/`bypassPermissions` here.
- Analysis is slow (minutes): the Run flow keeps per-row `IsBusy` (red dot) / `IsAnalyzed` (green dot) on `QueryRow`, stage progress in `StatusText` ("Step n/3 — …"), a live elapsed clock in `ElapsedText` (Avalonia `DispatcherTimer`, 1s tick — note the 3-arg ctor auto-starts the timer, so `Stop()` immediately after constructing), an indeterminate `ProgressBar` bound to `IsAnalyzing`, and errors in `RunErrorMessage` — the shared `ErrorMessage` is only for list loading.
- The report is rendered by `Controls/MarkdownViewer` (custom `ContentControl` with a `Markdown` styled property): headings, bold/italic/inline code, fenced code blocks, lists, and pipe tables. Keep it dependency-free.
- Numeric grid columns use `Width="Auto"` + `CellStyleClasses="numeric"` (right-aligns the cell `TextBlock` via a `DataGrid.Styles` selector `DataGridCell.numeric TextBlock`) — fixed widths truncated Dutch-formatted numbers mid-value, making the decimal comma look like a thousands comma. `QueryStoreQueryInfo.FormatMetric` shows whole numbers (`N0`, nl-NL) and only keeps one decimal below 10.

## AI (Executionplans) Tab

- `ExecutionPlansViewModel` lists `*.sqlplan` files from a configurable folder (default `executionplans/` under the repo root) as `PlanFileRow`s (`IsBusy`/`IsAnalyzed`, same wrapper pattern as `StatisticRow`/`IndexRow`); no SQL connection is required for this tab.
- **Analyze** calls `IPlanAnalysisService.AnalyzePlanAsync` — the same headless Claude CLI flow as the AI (QueryStore) tab, writing the report to `<resultsFolder>/<planFileNameWithoutExtension>.md`.
- **Open** calls `IPlanAnalysisService.ReadReportAsync(reportPath)` — reads that same `.md` path straight off disk with no Claude invocation, so a previously analyzed plan can be reviewed again instantly (and for free). Throws a friendly `FileNotFoundException`-derived message (surfaced via `RunErrorMessage`) if no report exists yet for that plan.

## Executionplan Animation Tab

Driven entirely by `ExecutionPlansVm.SelectedPlanFile` (see Cascade Flow) — it has no file picker of its own.

- `ShowPlanXmlParser.Parse(XDocument)` (pure, stateless — file I/O stays in `PlanAnalysisService.ParsePlanTreeAsync`) turns showplan XML into an `ExecutionPlanTree` (`PlanOperatorNode` root + `DegreeOfParallelism` from `<QueryPlan DegreeOfParallelism="...">`).
  - Recurses `<RelOp>` elements via `DescendantsBounded` — a depth-first walk that yields (but does not descend past) nested `<RelOp>` boundaries, so a node's own `<Object Table="...">` reference can be found generically across every operator type (scan/seek/lookup/…) without special-casing each one and without picking up a child operator's content.
  - `RunTimeCountersPerThread` can appear multiple times per `<RelOp>` — one row per parallel worker thread. Rows are **summed** for `ActualRows`, and **maxed** for `ActualElapsedms`/`ActualCPUms` (wall-clock time across concurrent threads isn't additive; row counts are). All per-thread rows are also kept as `PlanOperatorNode.ThreadRows` for the animation's thread-grid panel.
  - `SelfElapsedMs` = this node's own cumulative elapsed − Σ(children's cumulative elapsed), clamped ≥ 0 (falls back to `EstimatedTotalSubtreeCost` shares when there's no `RunTimeInformation`, i.e. an estimated-only plan). Root operators that carry no `RunTimeInformation` of their own (e.g. a trivial `Compute Scalar` root) fall back to the statement-level `<QueryTimeStats ElapsedTime="..." CpuTime="...">`, which is what the root's cumulative time represents anyway.
  - Showplan files are UTF-16 (`encoding="utf-16"` in the XML declaration) — loaded via `XDocument.LoadAsync(File.OpenRead(...))`, which respects the declared encoding, not `File.ReadAllTextAsync` + `Parse`.
- `ExecutionPlanAnimationControl` (pure C# `ContentControl`, no `.axaml`) builds a native diagram, no charting/animation package:
  - **Layout**: leaves (scans) get sequential rows; a node's column = `1 + max(children columns)` so scans sit left and the root sits right; a non-leaf's row = the average of its children's rows, so a join centers between its inputs. Row height grows per-plan (not just per-node) to fit the tallest thread-grid panel anywhere in the tree, so simple serial plans stay compact.
  - **Particle flow**: only leaves spawn particles (rate ∝ that leaf's `SelfElapsedMs` share of the plan). On arrival at a node, `ComputePassRatio` (this node's rows ÷ Σ children's rows, floored at 0.08 when non-zero so rare survivors stay visible, but a true zero stays zero) decides pass-on vs. discard (brief red border flash). Illustrative, not a literal per-row simulation — same spirit as Brent Ozar–style "database animation" posts.
  - **Self-elapsed-time-by-operator bar**: only rendered when `IsSerialPlan` (DOP 1) — on a parallel plan, operator self-times overlap across threads and don't sum to a meaningful total, so the control shows an explanatory note instead of a misleading bar.
  - **Thread grid**: any node with `ThreadRows.Count > 1` (i.e. that specific operator ran parallel, independent of overall plan DOP) gets a small property-grid-style panel — header "ALL THREADS" total + one row per `Thread N` — positioned directly under its box. Values animate from 0 up to their real counts on the same elapsed-fraction clock as the top stats, so a skewed thread (e.g. one thread scanning 10M rows while its 8 siblings scan 0) visibly races ahead.
  - **Playback**: `Plan`/`IsPlaying`/`IsPaused`/`IsSerialPlan` are all `StyledProperty`s (`IsPlaying`/`IsPaused` two-way). Three states — Idle (`▶ Replay the scan`), Playing (`⏸ Pause`), Paused (`▶ Go`) — driven by `ExecutionPlanAnimationViewModel.PlayButtonText`. `Pause` just stops the `DispatcherTimer` in place (particles/counters freeze exactly where they are); `Resume` shifts `_playStart` forward by the paused wall-clock duration so the timeline continues rather than jumping. See the `SetCurrentValue`/reentrancy-guard rule above.

## Coding Conventions

- `[ObservableProperty]` fields are `private` and camelCase (e.g. `private string _serverName`); the generated public property is PascalCase (`ServerName`).
- Errors surfaced via `string? ErrorMessage` property; never throw from a ViewModel.
- Connection strings always include `TrustServerCertificate=True` (required for SqlClient 4+).
- Number formatting is split by purpose: row counts/sizes/fragmentation (`TableInfo.RowCountDisplay`/`DataSizeDisplay`/`IndexSizeDisplay`, `IndexInfo.PagesDisplay`/`SizeMBDisplay`/`FragmentationDisplay`) use a `nl-NL` (European) `CultureInfo` — period thousands separator, comma decimal — per explicit user preference; everything from DBCC/DMV detail output (`StatisticInfo.SamplingPercentText`, `StatHeaderInfo`, `DensityVectorInfo`, `HistogramStepInfo`) uses `CultureInfo.InvariantCulture` (period decimal). Don't assume one culture applies everywhere — check the model.
- `DataTypeDisplay` on `ColumnInfo` appends `(n)` or `(max)` for string/binary types; `MaxLength = -1` means `MAX`.
- The window `Title` deliberately includes the user's email (`SQL Performance Visualizer — ronald.de.groot@opendata.nl`) — keep it when editing `MainWindow.axaml`.
- Raw SQL Server values that come back upper/mixed-case (`IS_NULLABLE` as `YES`/`NO`, `sys.indexes.type_desc` as `CLUSTERED`/`NONCLUSTERED`) get a `*Display` lowercase property (`ColumnInfo.IsNullableDisplay`, `IndexInfo.IndexTypeDisplay`) to match the Columns panel's lowercase style — bind to the `*Display` property in AXAML, never the raw field.

## Known Gotchas

- `Avalonia.Controls.DataGrid` is a separate NuGet package; max version is `12.0.0` (not `12.0.3`). Its Fluent styles must be registered in `App.axaml`.
- `TextBox.Watermark` is obsolete in Avalonia 12 — use `PlaceholderText`.
- The Avalonia template does NOT include `<ImplicitUsings>enable</ImplicitUsings>` — must be added manually.
- `HorizontalContentAlignment` on `ListBox` does not exist in Avalonia 12; set it via `ListBox.Styles` on `ListBoxItem`.
- `BoolConverters.Not` (built into Avalonia) can be used as `{x:Static BoolConverters.Not}` without extra packages.
