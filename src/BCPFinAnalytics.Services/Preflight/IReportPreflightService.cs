using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;

namespace BCPFinAnalytics.Services.Preflight;

/// <summary>
/// Runs pre-execution validation against the database before any report strategy
/// is allowed to execute. All reports go through preflight — no exceptions.
///
/// Called from ReportPage.razor on every Display/Excel/PDF click,
/// before strategy.ExecuteAsync() is invoked.
///
/// On failure: returns ServiceResult.Failure with a user-friendly error message.
/// On success: returns ServiceResult.Success(true) — strategy may proceed.
/// </summary>
public interface IReportPreflightService
{
    /// <summary>
    /// Validates that all entities in the effective selection share the same
    /// ENTITY.YEAREND fiscal year-end. Mixed year-ends make the balance query
    /// ambiguous (BALFORPD and BEGYRPD cannot be scalars), so the report is
    /// rejected before any SQL is executed.
    ///
    /// Validation is always run regardless of report type.
    /// </summary>
    Task<ServiceResult<bool>> ValidateAsync(ReportOptions options);
}
