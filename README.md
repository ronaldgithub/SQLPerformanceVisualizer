# SQL Performance Visualizer

A desktop tool for SQL Server database maintenance. Connect to any SQL Server instance and drill into databases, tables, columns, indexes, and statistics — down to index fragmentation and the statistics histogram — so you can make informed decisions about index rebuilds, reorganizes, and statistics updates, and trigger them directly from the app. An AI tab surfaces the top resource-consuming queries from Query Store and can capture a query's actual execution plan and have it analyzed by Claude, producing a saved markdown report. A second AI tab runs that same analysis ad-hoc against any `.sqlplan` file, and a dedicated Animation tab renders that plan as an animated, native flow diagram — no SQL connection or Claude required for either.

## Features

- **Connect** to any SQL Server using Windows Authentication (default) or SQL Authentication
- **Red/green indicator** shows connection status at a glance
- **Browse databases** — system and `DW*` databases filtered out automatically
- **Tables tab** — tables with row count, data size (MB), and index size (MB); columns panel alongside with data type and nullability
- **Indexes tab** — every index on the selected table, with page count, size (MB), and live fragmentation %
  - **Rebuild** / **Reorganize** buttons on each row run `ALTER INDEX ... REBUILD` / `REORGANIZE` directly, with a small red dot shown while that index is being worked on
- **Statistics tab** — every statistic on the table (primary key first, then user/index stats, then auto-created `_WA_Sys_*` stats last), with a detail panel showing:
  - the equivalent `DBCC SHOW_STATISTICS` command
  - the stat header (rows, rows sampled, steps, density, average key length, filter expression, ...)
  - the density vector and the full histogram
  - an **Update Stats** button to run `UPDATE STATISTICS` on just that statistic, with a choice of `SAMPLE x PERCENT` or `FULLSCAN` — a red dot marks the row that was just updated
- **Column highlight** — click a statistics row to highlight the columns it covers, back on the Tables tab
- **AI (QueryStore) tab** — top 25 resource-consuming queries from **Query Store**, ranked by a selectable metric (total duration / total CPU / logical reads / execution count), with executions, avg duration/CPU (ms), reads, and last execution time per plan
  - **Execute + Analyze** re-runs the selected query with `SET STATISTICS XML ON` (after an explicit confirmation popup — the query really executes), captures the **actual** execution plan, and saves it to `Documents\SQLPerformanceVisualizer\plans\<database>\query_<id>_<timestamp>.sqlplan` (the Query Store estimated plan is saved alongside as `.estimated.sqlplan`)
  - The plan is then analyzed headlessly by the **Claude Code CLI** using Erik Darling's [`query-plan-analysis` skill](https://github.com/erikdarlingdata/claude-plugins); the report is saved as a matching `.md` file and rendered with markdown formatting in the app
  - Step-by-step progress (1/3 → 3/3), a live elapsed clock, and red (running) / green (analyzed) row dots
- **AI (Executionplans) tab** — the same Claude-powered analysis, ad-hoc: point it at a folder of `.sqlplan` files (no SQL connection needed)
  - **Analyze** runs the full Claude analysis and saves the report
  - **Open** loads a report that's already been analyzed straight off disk — instant, no Claude call
- **Executionplan Animation tab** — an animated, native diagram of whichever plan is selected in the Execution Plans tab (no external renderer or charting library):
  - Operators laid out as a flow diagram — scans on the left, the plan's root on the right — with small dots flowing from each scan through the tree, discarded or passed on at each operator based on its actual row count vs. what its children fed in
  - Live **elapsed / rows scanned / rows out** counters, and a **self-elapsed-time-by-operator** bar (serial/DOP-1 plans only — a parallel plan's operator self-times overlap across threads and don't sum to a clean total)
  - Any operator that ran in parallel gets a small per-thread row-count panel under its box, with each thread's count animating up from 0 — makes thread skew (one worker doing most of the work) directly visible
  - **▶ Replay the scan** / **⏸ Pause** / **▶ Go** — pause freezes the animation exactly where it is; resuming continues rather than restarting
- European (`nl-NL`) number formatting for row counts, sizes, pages, and fragmentation percentages

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- SQL Server instance (local or remote) — any edition including Express
- For Rebuild/Reorganize and Update Stats: a login with `ALTER` permission on the table (or `db_owner`); for the statistics detail panel (`DBCC SHOW_STATISTICS`), at least `SELECT` permission on the table (SQL Server 2016 SP1+) or `ALTER`/`db_owner` on older versions
- For the **AI (QueryStore)** tab: **Query Store enabled** on the database — `ALTER DATABASE <db> SET QUERY_STORE = ON`
- For **Analyze** on either AI tab (not needed for **Open**, or for the Executionplan Animation tab — those are fully offline/native):
  - [Claude Code CLI](https://claude.com/claude-code) installed and on `PATH` (`claude.exe` or the npm `claude.cmd` shim are both detected)
  - Erik Darling's [claude-plugins](https://github.com/erikdarlingdata/claude-plugins) marketplace installed with the `sqlserver-query-plans` plugin (provides the `query-plan-analysis` skill), plus Python for its plan extractor
  - Each analysis is a real (billed) Claude Code invocation and typically takes a few minutes

## Build & Run

```bash
git clone https://github.com/ronaldgithub/SQLPerformanceVisualizer.git
cd SQLPerformanceVisualizer
dotnet run --project SQLPerformanceVisualizer
```

Or build first:

```bash
dotnet build
dotnet run --project SQLPerformanceVisualizer
```

## Usage

1. Enter the server name or IP in the top bar (defaults to `localhost`)
2. The dot turns **red** (disconnected) or **green** (connected)
3. Click **Connect** — the Database combo box populates
4. Pick a **database** — the tables list populates and the first table is selected automatically
5. On the **Tables** tab, click a table — its columns load and the first column is selected automatically
6. Switch to the **Indexes** tab to see every index on that table, with size, fragmentation, and Rebuild/Reorganize buttons
7. Switch to the **Statistics** tab and click a row to see its `DBCC SHOW_STATISTICS` detail (header, density vector, histogram); click **Update Stats** to refresh it with a sample percent or full scan
8. Switch to the **AI (QueryStore)** tab, pick a ranking metric, select a query, and click **Execute + Analyze** — review the SQL in the confirmation popup (the query really runs, including any data modifications), then watch the three steps complete and read the rendered report; the `.sqlplan` and `.md` files land in `Documents\SQLPerformanceVisualizer\plans\<database>\`
9. Switch to the **AI (Executionplans)** tab (no connection needed), point the plan folder at any directory of `.sqlplan` files, select one, and either **Analyze** it or, if it's already been analyzed, **Open** the saved report instantly
10. Switch to the **Executionplan Animation** tab to see that same plan as an animated flow diagram — press **▶ Replay the scan** and watch rows flow from each scan through the tree to the output (or get discarded along the way); **⏸ Pause** / **▶ Go** freeze and resume it in place

## UI Layout

```text
┌──────────────────────────────────────────────────────────────────────┐
│  SQL Server: [localhost] ● [Connect]      Database: [▼ combo]        │
├──────────────────────────────────────────────────────────────────────┤
│  [Tables] [Indexes] [Statistics] [AI (QueryStore)]                    │
│  [AI (Executionplans)] [Executionplan Animation]                      │
├──────────────────────────────────────────────────────────────────────┤
│  Tables tab:                                                          │
│  ┌──────────────────────────────┬───────────────────────────────┐   │
│  │ Table     Rows  Data  Index  │ Column     Type      Null      │   │
│  │ dbo.A    1.234    12      3  │ Id         int       no        │   │
│  │ dbo.B      567     4      1  │ Name       nvarchar  yes       │   │
│  └──────────────────────────────┴───────────────────────────────┘   │
│                                                                        │
│  Indexes tab:                                                         │
│  Index Name  [Rebuild][Reorg]●  Type  Pages  Size  Frag%  Unique  PK  │
│                                              Key Columns   Included   │
│                                                                        │
│  Statistics tab:                                                      │
│  ┌──────────────┬───────────────────────────────────────────────┐   │
│  │ ● Stat list  │ Statistic Detail              [ Update Stats ] │   │
│  │ (red dot =   │ DBCC SHOW_STATISTICS('schema.table','stat');   │   │
│  │  just        │ Stat Header / Density Vector / Histogram       │   │
│  │  updated)    │                                                 │   │
│  └──────────────┴───────────────────────────────────────────────┘   │
│                                                                        │
│  AI (QueryStore) tab:                                                 │
│  ┌──────────────┬───────────────────────────────────────────────┐   │
│  │ Query Store  │ AI Plan Analysis  ▓▓▓ 1:23  [Execute+Analyze] │   │
│  │ top queries  │ SQL of the selected query                      │   │
│  │ [metric ▼]   │ AI Analysis Report (rendered markdown)         │   │
│  │ ● = busy     │  — actual plan captured, saved, analyzed by    │   │
│  │ ● = analyzed │    the Claude Code query-plan-analysis skill   │   │
│  └──────────────┴───────────────────────────────────────────────┘   │
│                                                                        │
│  AI (Executionplans) tab — same analysis, ad-hoc, no connection:     │
│  ┌──────────────┬───────────────────────────────────────────────┐   │
│  │ Plan folder  │ AI Plan Analysis            [Open] [Analyze]  │   │
│  │ + file list  │ Plan File path                                 │   │
│  │ ● = busy     │ AI Analysis Report (rendered markdown)         │   │
│  │ ● = analyzed │  — Open reads an existing report instantly,    │   │
│  │              │    Analyze runs Claude fresh                   │   │
│  └──────────────┴───────────────────────────────────────────────┘   │
│                                                                        │
│  Executionplan Animation tab — animates the plan picked above:       │
│  ┌───────────────────────────────────────────────────────────────┐   │
│  │ ELAPSED  ROWS SCANNED  ROWS OUT       [▶ Replay / ⏸ / ▶ Go]  │   │
│  │  [Scan A] ─dots─▶ [Join] ─dots─▶ [Filter] ─dots─▶ [ROWS OUT]  │   │
│  │  (thread grid under any parallel operator, counts animate up  │   │
│  │   from 0 — skewed threads visibly race ahead of the rest)     │   │
│  └───────────────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────┘
```

## Tech Stack

- [Avalonia UI](https://avaloniaui.net/) 12 — cross-platform .NET UI framework
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) — MVVM source generators (`[ObservableProperty]`, `[RelayCommand]`)
- [Microsoft.Data.SqlClient](https://github.com/dotnet/SqlClient) — SQL Server data access
- [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) — DI container
- [Claude Code CLI](https://claude.com/claude-code) (external, optional) — headless AI analysis of execution plans on either AI tab; markdown reports are rendered by a small built-in viewer, and the Executionplan Animation tab parses showplan XML and renders its diagram natively — no extra NuGet packages for either
