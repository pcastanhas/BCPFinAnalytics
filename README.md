# BCPFinAnalytics

Financial analytics reporting dashboard for MRI PMX 10.5.

## Overview

BCPFinAnalytics is a .NET 10 Blazor Server application that provides advanced financial reporting on top of MRI PMX 10.5 data. It is launched directly from within MRI via a hyperlink and supports on-screen display, Excel export, and PDF export.

---

## Solution Structure

```
BCPFinAnalytics.sln
└── src/
    ├── BCPFinAnalytics.UI          Blazor Server frontend (MudBlazor)
    ├── BCPFinAnalytics.Services    Business logic, renderers, helpers, reports
    │   └── Reports/
    │       ├── TrialBalance/       Simple Trial Balance
    │       └── TrialBalanceDC/     Trial Balance with Debit & Credit columns
    ├── BCPFinAnalytics.DAL         Dapper + raw SQL data access
    └── BCPFinAnalytics.Common      Shared models, DTOs, interfaces, enums
```

---

## Technology Stack

| Concern | Technology |
|---|---|
| Framework | .NET 10 |
| UI | Blazor Server + MudBlazor 7 |
| Data Access | Dapper (no Entity Framework) |
| Logging | Serilog — rolling file, 30-day retention |
| Excel Export | ClosedXML (renderer stub — not yet implemented) |
| PDF Export | QuestPDF (renderer stub — not yet implemented) |
| Hosting | IIS (internal) |

---

## Running the App in Visual Studio for Debugging

### Prerequisites

- Visual Studio 2022 (17.8 or later)
- .NET 10 SDK installed
- Access to a MRI PMX 10.5 SQL Server database

### Step 1 — Configure Connection Strings

Open `src/BCPFinAnalytics.UI/appsettings.json` and fill in your database connection strings:

```json
{
  "ConnectionStrings": {
    "PROD": "Server=YOUR_SERVER;Database=YOUR_DB;User Id=YOUR_USER;Password=YOUR_PASS;TrustServerCertificate=True;",
    "TEST": "Server=YOUR_SERVER;Database=YOUR_DB;User Id=YOUR_USER;Password=YOUR_PASS;TrustServerCertificate=True;"
  }
}
```

> ⚠️ **Never commit appsettings.json with real credentials.** The file is in `.gitignore` for production values. Use `appsettings.Development.json` for local overrides.

### Step 2 — Set Startup Project

In Visual Studio:
1. Right-click **BCPFinAnalytics.UI** in Solution Explorer
2. Select **Set as Startup Project**

### Step 3 — Set the Launch URL

The app requires `db` and `userid` query string parameters on launch.

**Option A — Set in launchSettings.json (recommended for debugging)**

Open `src/BCPFinAnalytics.UI/Properties/launchSettings.json` and add the parameters to the `applicationUrl`:

```json
{
  "profiles": {
    "BCPFinAnalytics.UI": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": true,
      "launchUrl": "/?db=TEST&userid=yourusername",
      "applicationUrl": "https://localhost:7xxx;http://localhost:5xxx",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

Replace `db=TEST` with a key that matches one of your connection strings, and `userid=yourusername` with any identifier.

**Option B — Enter manually in browser**

After the app launches, navigate to:
```
https://localhost:7xxx/?db=TEST&userid=yourusername
```

### Step 4 — Run

Press **F5** to start debugging, or **Ctrl+F5** to run without the debugger.

The app will:
1. Parse the `db` and `userid` URL parameters
2. Initialize `UserSessionService` with those values
3. Load the main report page
4. Populate all dropdowns from the MRI database on first load

### Step 5 — Database Setup (first run only)

Before using the app for the first time, run the table creation script against your MRI PMX database:

```
src/BCPFinAnalytics.DAL/Scripts/CreateTables.sql
```

This creates the `BCPFinAnalyticsSavedSettings` table used to store saved report configurations.

---

## Launch URL (Production / MRI Integration)

In production the app is launched from a hyperlink within MRI:

```
https://yourserver/BCPFinAnalytics?db=PROD&userid=paul
```

| Parameter | Description |
|---|---|
| `db` | Named connection string key in `appsettings.json` (e.g. `PROD`, `TEST`) |
| `userid` | MRI user identity — passed through and trusted as-is |

---

## Logging

Logs are written to rolling daily text files in the `logs/` folder next to the application:

```
logs/BCPFinAnalytics-20250417.txt
```

- **Retention:** 30 days
- **Level:** Verbose (all report executions log user options + SQL queries)
- **Location:** Configured in `appsettings.json` under `Serilog`

---

## Adding a New Report

1. Create a new folder under `src/BCPFinAnalytics.Services/Reports/YourReportName/`
2. Add: `IYourReportRepository.cs`, `YourReportRepository.cs`, `YourReportStrategy.cs`
3. Implement `IReportStrategy` in the strategy class
4. Register in `ServiceRegistration.cs`:
   ```csharp
   services.AddScoped<IYourReportRepository, YourReportRepository>();
   services.AddScoped<IReportStrategy, YourReportStrategy>();
   ```
5. The `ReportStrategyResolver` picks it up automatically via `IEnumerable<IReportStrategy>`

---

## Reports Built

| Report | Strategy Code | Columns |
|---|---|---|
| Simple Trial Balance | `SIMPLETB` | Account # · Description · Balance at MM/YYYY |
| Trial Balance D/C | `TBDC` | Account # · Description · Starting Balance · Debits · Credits · Ending Balance |

---

## Known Stubs (Not Yet Implemented)

| Feature | Status |
|---|---|
| Excel export | `ExcelRenderer.Render()` throws `NotImplementedException` |
| PDF export | `PdfRenderer.Render()` throws `NotImplementedException` |
| GL Detail drill-down modal | `OnCellClicked()` is a placeholder — modal not wired |

---

## Version

**1.0** — 2 reports complete. Excel/PDF/Drill-down pending.
