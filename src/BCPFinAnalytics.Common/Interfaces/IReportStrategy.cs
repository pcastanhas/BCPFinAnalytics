using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;

namespace BCPFinAnalytics.Common.Interfaces;

/// <summary>
/// Contract for all report strategy implementations.
///
/// Each of the 20-25 report types implements this interface.
/// The strategy pattern ensures:
///   - Each report is fully self-contained
///   - Adding a new report = adding one new class, nothing else changes
///   - The UI and Services layer never need to know which report is running
///   - ReportOptionsConfig drives UI control state without any if/switch in the UI
///
/// IMPORTANT: Execute() must return a fully self-contained ReportResult.
/// No renderer should ever need to call the database or services layer.
/// </summary>
public interface IReportStrategy
{
    /// <summary>
    /// The report type code — matches the code from GUSR.
    /// Used by ReportStrategyResolver to find the correct strategy.
    /// </summary>
    string ReportCode { get; }

    /// <summary>Display name of the report — e.g. "Trial Balance".</summary>
    string ReportName { get; }

    /// <summary>
    /// Returns the options panel configuration for this report type.
    /// Called by the UI immediately when the user selects a report type,
    /// to enable/disable the appropriate controls.
    /// Hardcoded in each strategy implementation — no DB or config lookup.
    /// </summary>
    ReportOptionsConfig GetOptionsConfig();

    /// <summary>
    /// Executes the report with the given user options and returns
    /// a fully populated, output-agnostic ReportResult.
    ///
    /// Responsibilities:
    ///   - Validate options (report-specific validation rules)
    ///   - Query the MRI database via DAL
    ///   - Build ReportResult with correct columns, rows, and metadata
    ///   - Log all SQL queries and user options via Serilog
    ///   - Return ServiceResult.Failure on any error — never throw to UI
    /// </summary>
    Task<ServiceResult<ReportResult>> ExecuteAsync(ReportOptions options);
}
