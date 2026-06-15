# SQL Performance Visualizer

A desktop tool for SQL Server database maintenance. Connect to any SQL Server instance and explore databases, tables, columns, indexes, and statistics side by side — so you can make informed decisions about index rebuilds and statistics updates.

## Features

- **Connect** to any SQL Server using Windows Authentication (default) or SQL Authentication
- **Red/green indicator** shows connection status at a glance
- **Browse databases** — system and DW* databases filtered out automatically
- **Explore tables** with live row counts
- **Inspect columns** with data types and sizes (e.g. `nvarchar(200)`, `varchar(max)`)
- **View indexes** that include a selected column — key columns, included columns, unique/PK flags
- **Check statistics** — last updated, rows, sampling percentage (period as decimal separator), modification counter
- **Column highlight** — click a statistics row to highlight the columns it covers in the Columns panel

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- SQL Server instance (local or remote) — any edition including Express

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
5. Click a **table** — columns and statistics load; first column is selected automatically
6. Click a **column** — the indexes panel shows all indexes that include that column
7. Click a **statistics row** — related columns light up blue in the Columns panel

## UI Layout

```text
┌──────────────────────────────────────────────────────────────────────┐
│  SQL Server: [localhost] ● [Connect]      Database: [▼ combo]        │
├──────────────┬───────────────┬────────────────────────────────────────┤
│  Tables      │  Columns      │  Indexes                               │
│  dbo.A [..]  │  Id   int  NO │  PK_...  CLUSTERED  ✓  □  Id          │
│  dbo.B [..]  │  Name nva(…)  ├────────────────────────────────────────┤
│  dbo.C [..]  │               │  Statistics                            │
│              │               │  _WA_Sys_…  2025-06-15  372928  1.77  │
└──────────────┴───────────────┴────────────────────────────────────────┘
```

## Tech Stack

- [Avalonia UI](https://avaloniaui.net/) 12 — cross-platform .NET UI framework
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) — MVVM source generators
- [Microsoft.Data.SqlClient](https://github.com/dotnet/SqlClient) — SQL Server data access
- [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) — DI container
