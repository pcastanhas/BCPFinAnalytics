namespace BCPFinAnalytics.DAL.Interfaces;

/// <summary>
/// Queries the net income (unposted retained earnings) for a set of entities
/// for use in balance sheet and trial balance reports.
///
/// Unposted retained earnings = sum of all income statement account activity
/// for the current fiscal year that has not yet been formally closed/posted
/// to the retained earnings account (GLCD.REARNACC).
/// </summary>
public interface IUnpostedRERepository
{
    /// <summary>
    /// Returns the sum of all income statement (TYPE='I') account activity
    /// for the given entities, between BEGYRPD and EndPeriod.
    /// Keyed by EntityId — one decimal per entity (null if no activity).
    ///
    /// The retained earnings account (REARNACC) is excluded from the sum
    /// to avoid double-counting on formats that already show it directly.
    /// </summary>
    Task<Dictionary<string, decimal?>> GetNetIncomeByEntityAsync(
        string dbKey,
        IReadOnlyList<string> entityIds,
        string begYrPd,
        string endPeriod,
        IReadOnlyList<string> basisList,
        string ledgLo,
        string ledgHi,
        string reArnAcct);
}
