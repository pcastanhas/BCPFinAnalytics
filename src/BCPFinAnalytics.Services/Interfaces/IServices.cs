using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Interfaces;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;

namespace BCPFinAnalytics.Services.Interfaces;

// ══════════════════════════════════════════════════════════════
//  ILookupService
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Provides all dropdown/lookup data to the UI options panel.
/// Wraps ILookupRepository — applies no business logic, just
/// passes data through and handles errors via ServiceResult.
/// </summary>
public interface ILookupService
{
    Task<ServiceResult<IEnumerable<FormatDto>>> GetFormatsAsync(string dbKey);
    Task<ServiceResult<IEnumerable<BudgetDto>>> GetBudgetsAsync(string dbKey);
    Task<ServiceResult<IEnumerable<SFTypeDto>>> GetSFTypesAsync(string dbKey);
    Task<ServiceResult<IEnumerable<BasisDto>>> GetBasisAsync(string dbKey);
    Task<ServiceResult<IEnumerable<EntityDto>>> GetEntitiesAsync(string dbKey);
    Task<ServiceResult<IEnumerable<ProjectDto>>> GetProjectsAsync(string dbKey);
}

// ══════════════════════════════════════════════════════════════
//  ISavedSettingsService
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Manages saved report settings.
/// Handles JSON serialization/deserialization of SavedSettingOptions.
/// </summary>
public interface ISavedSettingsService
{
    Task<ServiceResult<IEnumerable<SavedSettingDto>>> GetAllForUserAsync(string dbKey, string userId);
    Task<ServiceResult<SavedSettingOptions>> GetSettingOptionsAsync(string dbKey, int settingId);
    Task<ServiceResult<int>> SaveAsync(string dbKey, string userId, string settingName, bool isPublic, SavedSettingOptions options);
    Task<ServiceResult<bool>> DeleteAsync(string dbKey, int settingId);
}

// ══════════════════════════════════════════════════════════════
//  IReportStrategyResolver
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Resolves the correct IReportStrategy implementation for a given report code.
/// All strategy implementations are registered here.
/// </summary>
public interface IReportStrategyResolver
{
    IReportStrategy Resolve(string reportCode);
    IEnumerable<(string Code, string Name)> GetAllReportTypes();
}

// ══════════════════════════════════════════════════════════════
//  IPivotService
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Provides reusable C# pivot logic for crosstab reports.
/// Takes flat/normalized rows from DAL and pivots into ReportResult columns.
/// SQL PIVOT is never used — pivoting always happens here in C#.
/// </summary>
public interface IPivotService
{
    /// <summary>
    /// Pivots a flat list of rows into a ReportResult with dynamic columns.
    /// </summary>
    /// <param name="flatRows">Normalized rows: EntityId, AccountCode, AccountGroup, Value</param>
    /// <param name="columnHeaders">Ordered list of (ColumnId, DisplayHeader) pairs</param>
    ReportResult Pivot(
        IEnumerable<PivotFlatRow> flatRows,
        IEnumerable<(string ColumnId, string Header)> columnHeaders,
        ReportMetadata metadata);
}

// ══════════════════════════════════════════════════════════════
//  IExcelRenderer
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Renders a ReportResult to an Excel workbook as a byte array.
/// Uses ClosedXML. Never calls database or services layer.
/// </summary>
public interface IExcelRenderer
{
    byte[] Render(ReportResult reportResult);

    /// <summary>
    /// Renders multiple ReportResults as named worksheets in a single workbook.
    /// Used by V2 Report Packages.
    /// </summary>
    byte[] RenderPackage(IEnumerable<(string SheetName, ReportResult Result)> sheets);
}

// ══════════════════════════════════════════════════════════════
//  IPdfRenderer
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Renders a ReportResult to a PDF document as a byte array.
/// Uses QuestPDF. Never calls database or services layer.
/// </summary>
public interface IPdfRenderer
{
    byte[] Render(ReportResult reportResult);
}

// ══════════════════════════════════════════════════════════════
//  IScreenReportMapper
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Prepares a ReportResult for consumption by the Blazor screen renderer.
/// Applies any display-only transformations — e.g. formatting numeric values
/// as strings, resolving N/A display logic.
/// Never calls database or services layer.
/// </summary>
public interface IScreenReportMapper
{
    ReportResult Prepare(ReportResult reportResult);
}
