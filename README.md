# SQL Performance Visualizer

A desktop tool for SQL Server database maintenance. Connect to any SQL Server instance and drill into databases, tables, columns, indexes, and statistics — down to index fragmentation and the statistics histogram — so you can make informed decisions about index rebuilds, reorganizes, and statistics updates, and trigger them directly from the app.

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
- European (`nl-NL`) number formatting for row counts, sizes, pages, and fragmentation percentages

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- SQL Server instance (local or remote) — any edition including Express
- For Rebuild/Reorganize and Update Stats: a login with `ALTER` permission on the table (or `db_owner`); for the statistics detail panel (`DBCC SHOW_STATISTICS`), at least `SELECT` permission on the table (SQL Server 2016 SP1+) or `ALTER`/`db_owner` on older versions

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

## UI Layout

```text
┌──────────────────────────────────────────────────────────────────────┐
│  SQL Server: [localhost] ● [Connect]      Database: [▼ combo]        │
├──────────────────────────────────────────────────────────────────────┤
│  [ Tables ]  [ Indexes ]  [ Statistics ]                              │
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
└──────────────────────────────────────────────────────────────────────┘
```

## Tech Stack

- [Avalonia UI](https://avaloniaui.net/) 12 — cross-platform .NET UI framework
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) — MVVM source generators (`[ObservableProperty]`, `[RelayCommand]`)
- [Microsoft.Data.SqlClient](https://github.com/dotnet/SqlClient) — SQL Server data access
- [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) — DI container
