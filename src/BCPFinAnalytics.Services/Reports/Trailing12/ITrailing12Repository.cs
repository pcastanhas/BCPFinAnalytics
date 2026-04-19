using BCPFinAnalytics.Services.Helpers;

namespace BCPFinAnalytics.Services.Reports.Trailing12;

/// <summary>
/// Data access for the Trailing 12 Month Income Statement.
///
/// One query fetches all 12 months in a single round trip using
/// PERIOD IN @MonthList. The strategy pivots the results by period.
/// BALFOR='N' — activity only, no opening balance rollup rows.
/// </summary>
public interface ITrailing12Repository
{
    /// <summary>
    /// Returns GL activity for the given list of periods.
    /// Returns one row per ACCTNUM + ENTITYID + PERIOD.
    /// </summary>
    Task<IEnumerable<Trailing12RawRow>> GetActivityAsync(
        string dbKey,
        GlQueryParameters glParams,
        IReadOnlyList<string> periods);

    /// <summary>
    /// Returns budget amounts from the BUDGETS table for the given list of periods.
    /// Filtered by BUDTYPE = budgetType, summed across all BASIS and DEPARTMENT.
    /// Returns one row per ACCTNUM + ENTITYID + PERIOD.
    /// </summary>
    Task<IEnumerable<Trailing12RawRow>> GetBudgetAsync(
        string dbKey,
        GlQueryParameters glParams,
        IReadOnlyList<string> periods,
        string budgetType);
}
