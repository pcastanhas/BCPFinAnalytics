using BCPFinAnalytics.Services.Helpers;

namespace BCPFinAnalytics.Services.Reports.TrialBalanceDC;

/// <summary>
/// Data access for the Trial Balance with Debit and Credit Columns report.
///
/// Only the activity query lives here — starting balances come from the shared
/// <c>IStartingBalanceRepository.GetStartingBalancesForRangeAsync</c> so the
/// correct BALFOR filtering (avoiding the January double-count) is applied
/// consistently across all reports.
/// </summary>
public interface ITrialBalanceDCRepository
{
    /// <summary>
    /// Returns net activity between StartPeriod and EndPeriod (inclusive).
    /// Used to populate the Debits or Credits column.
    ///
    /// For all account types: PERIOD BETWEEN @StartPeriod AND @EndPeriod
    /// Filters GLSUM.BALFOR = 'N' to exclude year-opening snapshot rows.
    /// </summary>
    Task<IEnumerable<TrialBalanceDCRawRow>> GetActivityAsync(
        string dbKey,
        GlQueryParameters glParams,
        string startPeriod);
}
