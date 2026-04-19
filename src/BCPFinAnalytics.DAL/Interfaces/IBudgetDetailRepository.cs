using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;

namespace BCPFinAnalytics.DAL.Interfaces;

/// <summary>
/// Retrieves budget detail rows from the BUDGETS table for the drill-down modal.
/// One row per ACCTNUM + ENTITYID + DEPARTMENT + BASIS + BUDTYPE + PERIOD combination.
/// </summary>
public interface IBudgetDetailRepository
{
    Task<IEnumerable<BudgetDetailRow>> GetDetailAsync(
        string dbKey,
        BudgetDrillDownRef drillDown);
}
