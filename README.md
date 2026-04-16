# BCPFinAnalytics

Financial analytics reporting dashboard for MRI PMX 10.5.

## Overview

BCPFinAnalytics is a .NET 10 Blazor Server application that provides advanced financial reporting on top of MRI PMX 10.5 data. It is launched directly from within MRI via a hyperlink and supports on-screen display, Excel export, and PDF export.

## Solution Structure

```
BCPFinAnalytics.sln
└── src/
    ├── BCPFinAnalytics.UI          Blazor Server frontend (MudBlazor)
    ├── BCPFinAnalytics.Services    Business logic, renderers, session
    ├── BCPFinAnalytics.DAL         Dapper + raw SQL data access
    └── BCPFinAnalytics.Common      Shared models, DTOs, interfaces, enums
```

## Technology Stack

| Concern | Technology |
|---|---|
| Framework | .NET 10 |
| UI | Blazor Server + MudBlazor |
| Data Access | Dapper (no Entity Framework) |
| Logging | Serilog — rolling file, 30-day retention |
| Excel Export | ClosedXML |
| PDF Export | QuestPDF |
| Hosting | IIS (internal) |

## Launch URL

The application is launched from MRI with query string parameters:

```
https://yourserver/BCPFinAnalytics?db=PROD&userid=paul
```

| Parameter | Description |
|---|---|
| `db` | Named connection string key in `appsettings.json` |
| `userid` | MRI user identity — trusted as-is |

## Configuration

Copy `appsettings.json` and fill in your connection strings:

```json
{
  "ConnectionStrings": {
    "PROD": "Server=...;Database=...;",
    "TEST": "Server=...;Database=...;"
  }
}
```

## Database Setup

Run the SQL script against your MRI PMX database before first use:

```
src/BCPFinAnalytics.DAL/Scripts/CreateTables.sql
```

## Version

**1.0** — Infrastructure complete, reports in development.
