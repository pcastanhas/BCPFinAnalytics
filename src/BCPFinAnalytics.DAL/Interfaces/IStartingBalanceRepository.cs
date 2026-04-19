using BCPFinAnalytics.Common.Models;

namespace BCPFinAnalytics.DAL.Interfaces;

/// <summary>
/// Computes the starting balance for a drill-down — the balance as of the
/// period immediately BEFORE <see cref="DrillDownRef.PeriodFrom"/>.
///
/// Correctly honours GLSUM.BALFOR, the column that distinguishes
/// year-opening snapshot rows ('B') from ordinary activity rows ('N').
/// Ignoring BALFOR would double-count balances every fiscal January for
/// balance-sheet accounts.
///
/// The SQL encodes a four-case matrix based on:
///     (account type)  ×  (is PeriodFrom itself a BALFOR='B' period?)
///
/// Simplifying assumption (matches MRI's source SQL): all accounts in the
/// drill are the same type family. A drill that genuinely mixes 'B/C' and
/// 'I' accounts (e.g. a grand-total row crossing NOI into Net Income) is
/// an anti-pattern in the format definitions and not supported here.
/// The account type used for the SQL is derived from the first account's
/// TYPE in GACC.
/// </summary>
public interface IStartingBalanceRepository
{
    /// <summary>
    /// Returns the net starting balance (summed across entities, accounts and
    /// basis values in the drill) as of the period before PeriodFrom.
    /// Returns 0 when there is no prior activity to accumulate.
    /// </summary>
    Task<decimal> GetStartingBalanceAsync(string dbKey, DrillDownRef drillDown);

    /// <summary>
    /// Batched variant — returns per-account starting balances for every account
    /// in the ledger range. Used by reports that render a "Starting Balance"
    /// column (e.g. Trial Balance DC) and need one number per account, not per
    /// drill.
    ///
    /// Encodes the same four-case (account-type × is-balfor-period) matrix as
    /// the per-drill variant, but account type is resolved per-row inside the
    /// SQL via CASE on GACC.TYPE. Returns AcctName and Type alongside the
    /// amount so callers don't need a separate metadata query for dormant
    /// accounts that carry a balance forward but have no period activity.
    /// </summary>
    /// <param name="ledgLo">SARGable ledger range lower bound (inclusive).</param>
    /// <param name="ledgHi">SARGable ledger range upper bound (exclusive).</param>
    /// <param name="entityIds">Entity IDs to include in the sum.</param>
    /// <param name="basisList">Basis values to include. Caller is responsible
    /// for expanding A/C → +B if they want that behaviour.</param>
    /// <param name="balForPd">Current-year BALFOR anchor — start period for
    /// B/C accounts.</param>
    /// <param name="begYrPd">Fiscal-year-start period — start period for
    /// I accounts.</param>
    /// <param name="periodFrom">The drill window start. Starting balance runs
    /// through the period before this.</param>
    Task<IReadOnlyDictionary<string, StartingBalanceRow>> GetStartingBalancesForRangeAsync(
        string dbKey,
        string ledgLo,
        string ledgHi,
        IReadOnlyList<string> entityIds,
        IReadOnlyList<string> basisList,
        string balForPd,
        string begYrPd,
        string periodFrom);
}

/// <summary>
/// One account's starting balance plus the metadata needed to reconstruct
/// dormant accounts that carry a balance forward with no current activity.
/// </summary>
public sealed record StartingBalanceRow(string AcctName, string Type, decimal Amount);
