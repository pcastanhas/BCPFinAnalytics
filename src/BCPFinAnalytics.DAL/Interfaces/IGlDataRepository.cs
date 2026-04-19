using BCPFinAnalytics.Common.Models;

namespace BCPFinAnalytics.DAL.Interfaces;

/// <summary>
/// The canonical GL data surface. Every GL-backed number in every report is
/// one of these two primitives (or a composition):
///
///   - Starting balance at a point in time
///   - Activity summed over a period range
///
/// Examples of column formulas in terms of these primitives:
///   Balance Sheet ending  : GetGlStartingBalance(BalForPd) + GetGlActivity(BalForPd, EndPeriod)
///   IS PTD                : GetGlActivity(StartPeriod, EndPeriod)
///   IS YTD                : GetGlActivity(BegYrPd, EndPeriod)
///   Simple TB B/C balance : GetGlStartingBalance(BalForPd) + GetGlActivity(BalForPd, EndPeriod)
///   Simple TB I balance   : GetGlActivity(BegYrPd, EndPeriod)
///   TBDC Starting         : GetGlStartingBalance(StartPeriod)
///   TBDC Ending           : GetGlStartingBalance(StartPeriod) + GetGlActivity(StartPeriod, EndPeriod)
///   T12 month cell        : GetGlActivity(period, period)
///   FC12 actual cell      : GetGlActivity(period, period)
///
/// Both methods return one row per account, pre-aggregated across all entities
/// in scope. Reports that need per-entity breakdowns will require a separate
/// variant in the future (none currently do).
///
/// Both methods correctly honour GLSUM.BALFOR:
///   GetGlStartingBalance — encodes the four-case (account-type × is-balfor-
///                          period) matrix so B/C snapshot rows are used at
///                          BALFOR periods and activity rows at all other times.
///   GetGlActivity        — filters BALFOR='N' to exclude year-opening snapshots.
/// </summary>
public interface IGlDataRepository
{
    /// <summary>
    /// Returns the GL starting balance for each account — the balance as of
    /// the end of the period immediately before <paramref name="period"/>.
    /// Returns an empty dictionary entry (or no entry) for accounts with no
    /// activity prior to <paramref name="period"/>.
    ///
    /// "Starting balance" semantics:
    ///   B/C (balance sheet) accounts: cumulative since BALFOR anchor
    ///   I   (income) accounts:        cumulative since fiscal-year open
    ///                                 (returns 0 when period IS the fiscal-year open)
    /// </summary>
    Task<IReadOnlyDictionary<string, AccountAmount>> GetGlStartingBalanceAsync(
        string dbKey,
        string period,
        string ledgLo,
        string ledgHi,
        IReadOnlyList<string> entityIds,
        IReadOnlyList<string> basisList);

    /// <summary>
    /// Returns net GL activity summed between <paramref name="startPeriod"/>
    /// and <paramref name="endPeriod"/> inclusive, per account, aggregated
    /// across the entities in scope.
    ///
    /// For a single-month breakdown, callers pass the same period for start
    /// and end. For multi-month trends, callers fire multiple parallel calls
    /// via Task.WhenAll rather than asking for a period-keyed breakdown.
    /// </summary>
    Task<IReadOnlyDictionary<string, AccountAmount>> GetGlActivityAsync(
        string dbKey,
        string startPeriod,
        string endPeriod,
        string ledgLo,
        string ledgHi,
        IReadOnlyList<string> entityIds,
        IReadOnlyList<string> basisList);
}
