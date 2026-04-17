using BCPFinAnalytics.Services.Helpers;

namespace BCPFinAnalytics.Services.Reports.TrialBalance;

/// <summary>
/// Data access for the Simple Trial Balance report.
/// One repository per report — per architectural rule.
/// Queries GACC + GLSUM using the fully resolved GlQueryParameters.
/// </summary>
public interface ITrialBalanceRepository
{
    /// <summary>
    /// Returns raw GL balance rows for all accounts matching the resolved
    /// ledger range, entity selection, basis list, and period range.
    ///
    /// Uses the canonical unified query pattern:
    ///   - Balance sheet accounts (TYPE='B','C'): PERIOD BETWEEN @BalForPd AND @EndPeriod
    ///   - Income accounts (TYPE='I'):            PERIOD BETWEEN @BegYrPd AND @EndPeriod
    ///
    /// Returns one row per (ACCTNUM, ENTITYID) combination.
    /// Aggregation across entities is done in the strategy layer.
    /// </summary>
    Task<IEnumerable<TrialBalanceRawRow>> GetBalancesAsync(
        string dbKey,
        GlQueryParameters glParams);
}
