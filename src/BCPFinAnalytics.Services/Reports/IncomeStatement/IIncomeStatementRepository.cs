using BCPFinAnalytics.Services.Helpers;

namespace BCPFinAnalytics.Services.Reports.IncomeStatement;

/// <summary>
/// Data access for the Simple Income Statement report.
///
/// Two separate queries are needed:
///   Actual  — GLSUM filtered by actual basis list, PERIOD BETWEEN @StartPeriod AND @EndPeriod
///   Budget  — GLSUM filtered by budget basis code, PERIOD BETWEEN @StartPeriod AND @EndPeriod
///
/// BALFOR='B' rows are excluded from both queries (activity only, no opening balance entries).
/// Income statement accounts (TYPE='I') reset each year — no starting balance needed.
/// </summary>
public interface IIncomeStatementRepository
{
    /// <summary>
    /// Returns actual GL activity for the given period range.
    /// Excludes BALFOR='B' opening balance rows.
    /// </summary>
    Task<IEnumerable<IncomeStatementRawRow>> GetActualAsync(
        string dbKey,
        GlQueryParameters glParams,
        string startPeriod,
        string endPeriod);

    /// <summary>
    /// Returns budget amounts for the given period range from the BUDGETS table.
    /// Filtered by BUDTYPE = budgetType, summed across all BASIS and DEPARTMENT values.
    /// BUDGETS table is separate from GLSUM — budget data is never in GLSUM.
    /// </summary>
    Task<IEnumerable<IncomeStatementRawRow>> GetBudgetAsync(
        string dbKey,
        GlQueryParameters glParams,
        string startPeriod,
        string endPeriod,
        string budgetType);
}
