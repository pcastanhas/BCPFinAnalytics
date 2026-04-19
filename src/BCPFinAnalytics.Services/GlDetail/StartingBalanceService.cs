using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.GlDetail;

/// <summary>
/// Computes the starting balance shown at the top of the GL drill-down modal.
///
/// Semantic: "balance at end of (PeriodFrom - 1)" — the balance rolled into
/// the drill window, such that starting + sum(transactions) = balance at end
/// of PeriodTo, matching the clicked cell.
///
/// Implemented as a thin wrapper around <c>IGlDataRepository.GetGlStartingBalanceAsync</c>:
///   - Calls the primitive with a tight ledger range derived from DrillDownRef.AcctNums
///   - Filters the returned per-account dictionary to exactly the drill's accounts
///   - Sums across accounts to produce the single scalar the dialog displays
///
/// The primitive handles BALFOR filtering and account-type (B/C vs I) anchoring
/// internally, correctly producing the closing balance at (PeriodFrom - 1).
/// </summary>
public class StartingBalanceService : IStartingBalanceService
{
    private readonly IGlDataRepository _glData;
    private readonly ILogger<StartingBalanceService> _logger;

    public StartingBalanceService(
        IGlDataRepository glData,
        ILogger<StartingBalanceService> logger)
    {
        _glData = glData;
        _logger = logger;
    }

    public async Task<ServiceResult<decimal>> GetStartingBalanceAsync(
        string dbKey, DrillDownRef drillDown)
    {
        _logger.LogInformation(
            "StartingBalanceService.GetStartingBalanceAsync — DbKey={DbKey} " +
            "Entities=[{Entities}] AcctNums=[{AcctNums}] PeriodFrom={PeriodFrom}",
            dbKey,
            string.Join(",", drillDown.EntityIds),
            string.Join(",", drillDown.AcctNums),
            drillDown.PeriodFrom);

        if (drillDown.AcctNums.Count == 0 || drillDown.EntityIds.Count == 0
            || drillDown.BasisList.Count == 0
            || string.IsNullOrEmpty(drillDown.PeriodFrom))
        {
            return ServiceResult<decimal>.Success(0m);
        }

        try
        {
            // Derive a SARGable ledger range from the drill accounts. The
            // primitive filters ACCTNUM >= LedgLo AND ACCTNUM < LedgHi; LedgHi
            // is the smallest string strictly greater than the max account
            // (appending '\u0001' gives a sentinel that sorts just above any
            // real ACCTNUM sharing that prefix).
            var ledgLo = drillDown.AcctNums.Min()!;
            var ledgHi = drillDown.AcctNums.Max() + "\u0001";

            var byAcct = await _glData.GetGlStartingBalanceAsync(
                dbKey,
                drillDown.PeriodFrom,
                ledgLo, ledgHi,
                drillDown.EntityIds,
                drillDown.BasisList);

            // Filter to exactly the drill's accounts (the range above may
            // over-fetch if AcctNums isn't contiguous) and sum.
            var wanted = new HashSet<string>(drillDown.AcctNums);
            var matched = byAcct.Where(kvp => wanted.Contains(kvp.Key)).ToList();
            var total   = matched.Sum(kvp => kvp.Value.Amount);

            _logger.LogDebug(
                "StartingBalanceService.GetStartingBalanceAsync — " +
                "DbKey={DbKey} PeriodFrom={PeriodFrom} AccountsMatched={Count} Balance={Balance}",
                dbKey, drillDown.PeriodFrom, matched.Count, total);

            return ServiceResult<decimal>.Success(total);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "StartingBalanceService.GetStartingBalanceAsync failed — " +
                "DbKey={DbKey} PeriodFrom={PeriodFrom}",
                dbKey, drillDown.PeriodFrom);

            return ServiceResult<decimal>.FromException(ex, ErrorCode.DatabaseError);
        }
    }
}
