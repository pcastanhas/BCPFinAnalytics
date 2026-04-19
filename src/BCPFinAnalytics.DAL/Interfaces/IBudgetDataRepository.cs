using BCPFinAnalytics.Common.Models;

namespace BCPFinAnalytics.DAL.Interfaces;

/// <summary>
/// The canonical budget data surface. One primitive that every budget-backed
/// column in every report composes from:
///
///   GetBudgetAmount(startPeriod, endPeriod, budgetType, ..., basis)
///     = sum of BUDGETS.ACTIVITY over the period range, filtered by budget
///       type and basis, pre-aggregated across entities.
///
/// Examples of column formulas in terms of this primitive:
///   IS PTD Budget     : GetBudgetAmount(StartPeriod, EndPeriod)
///   IS YTD Budget     : GetBudgetAmount(BegYrPd,     EndPeriod)
///   T12 month budget  : GetBudgetAmount(period, period)
///   FC12 month budget : GetBudgetAmount(period, period)
///
/// Note on basis: unlike the previous per-report budget queries which summed
/// across all basis values, this method filters by the caller's basis list —
/// so PTD Budget reconciles with PTD Actual for the same basis selection.
/// </summary>
public interface IBudgetDataRepository
{
    /// <summary>
    /// Returns the total budget activity per account between startPeriod and
    /// endPeriod (inclusive), for the given budget type, filtered by basis,
    /// pre-aggregated across entities.
    /// </summary>
    Task<IReadOnlyDictionary<string, AccountAmount>> GetBudgetAmountAsync(
        string dbKey,
        string startPeriod,
        string endPeriod,
        string budgetType,
        string ledgLo,
        string ledgHi,
        IReadOnlyList<string> entityIds,
        IReadOnlyList<string> basisList);
}
