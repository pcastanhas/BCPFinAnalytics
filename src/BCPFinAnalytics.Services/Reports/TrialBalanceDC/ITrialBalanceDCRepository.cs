using BCPFinAnalytics.Services.Helpers;

namespace BCPFinAnalytics.Services.Reports.TrialBalanceDC;

/// <summary>
/// Data access for the Trial Balance with Debit and Credit Columns report.
///
/// TWO QUERIES are required:
///   1. GetStartingBalancesAsync  — balance accumulated up to PeriodBeforeStart
///   2. GetActivityAsync          — net activity between StartPeriod and EndPeriod
///
/// Both use the canonical period-range logic:
///   B/C accounts: accumulate from BALFORPD
///   I   accounts: accumulate from BEGYRPD (or return zero if StartPeriod == BEGYRPD)
/// </summary>
public interface ITrialBalanceDCRepository
{
    /// <summary>
    /// Returns accumulated balances up to (but not including) StartPeriod.
    /// Used to populate the Starting Balance column.
    ///
    /// For B/C accounts: PERIOD BETWEEN @BalForPd AND @PeriodBeforeStart
    /// For I   accounts: PERIOD BETWEEN @BegYrPd  AND @PeriodBeforeStart
    ///                   Returns zero rows when StartPeriod == BegYrPd
    ///                   (income resets at fiscal year start)
    /// </summary>
    Task<IEnumerable<TrialBalanceDCRawRow>> GetStartingBalancesAsync(
        string dbKey,
        GlQueryParameters glParams,
        string periodBeforeStart);

    /// <summary>
    /// Returns net activity between StartPeriod and EndPeriod (inclusive).
    /// Used to populate the Debits or Credits column.
    ///
    /// For all account types: PERIOD BETWEEN @StartPeriod AND @EndPeriod
    /// </summary>
    Task<IEnumerable<TrialBalanceDCRawRow>> GetActivityAsync(
        string dbKey,
        GlQueryParameters glParams,
        string startPeriod);
}
