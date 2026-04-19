using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;

namespace BCPFinAnalytics.Services.BudgetDetail;

/// <summary>
/// Retrieves budget transaction detail for the budget drill-down modal.
/// Wraps <c>IBudgetDrillDownRepository</c> and surfaces errors via
/// <c>ServiceResult</c> so the dialog renders a friendly error state rather
/// than crashing on a DB blip. Mirrors the pattern used by
/// <c>IGlDrillDownService</c> on the GL side.
/// </summary>
public interface IBudgetDrillDownService
{
    /// <summary>
    /// Returns all budget rows matching the drill-down context. Queries the
    /// BUDGETS table (not JOURNAL/GHIS). Basis expansion (A/C → +B) is applied
    /// by the repository.
    /// </summary>
    Task<ServiceResult<IEnumerable<BudgetDetailRow>>> GetTransactionsAsync(
        string dbKey,
        BudgetDrillDownRef drillDown);
}
