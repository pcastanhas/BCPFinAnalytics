using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Helpers;

// ══════════════════════════════════════════════════════════════
//  IUnpostedREService
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Computes the unposted retained earnings synthetic row for balance sheet
/// and trial balance reports.
///
/// Unposted retained earnings = net income for the current fiscal year
/// that has not yet been formally closed/posted to GLCD.REARNACC.
///
/// This row is appended after the equity section by report strategies
/// that need it (Balance Sheet, Trial Balance). The row uses
/// RowType.UnpostedRetainedEarnings and is styled distinctively.
///
/// Reports that DO need this: Balance Sheet, Trial Balance
/// Reports that DO NOT need this: P&amp;L, Budget Variance, Crosstab (income-only)
/// </summary>
public interface IUnpostedREService
{
    /// <summary>
    /// Builds the unposted retained earnings ReportRow.
    ///
    /// Returns null Data (not a failure) when the net income is zero
    /// and the row should be suppressed — the caller decides whether
    /// to include a zero row based on report requirements.
    /// </summary>
    Task<ServiceResult<ReportRow?>> BuildRowAsync(
        string dbKey,
        GlQueryParameters glParams,
        string reArnAcct,
        string columnId,
        bool wholeDollars);

    /// <summary>
    /// Builds unposted retained earnings rows keyed by column ID.
    /// Used by consolidated/crosstab reports where each column = one entity.
    /// Returns a single ReportRow with one cell per entity column.
    /// </summary>
    Task<ServiceResult<ReportRow?>> BuildConsolidatedRowAsync(
        string dbKey,
        GlQueryParameters glParams,
        string reArnAcct,
        IReadOnlyList<string> columnIds,
        bool wholeDollars);
}

// ══════════════════════════════════════════════════════════════
//  UnpostedREService
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Builds the synthetic unposted retained earnings row.
/// </summary>
public class UnpostedREService : IUnpostedREService
{
    private readonly IUnpostedRERepository _repo;
    private readonly ILogger<UnpostedREService> _logger;

    private const string RowLabel = "Unposted Retained Earnings";

    public UnpostedREService(
        IUnpostedRERepository repo,
        ILogger<UnpostedREService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ReportRow?>> BuildRowAsync(
        string dbKey,
        GlQueryParameters glParams,
        string reArnAcct,
        string columnId,
        bool wholeDollars)
    {
        try
        {
            _logger.LogDebug(
                "UnpostedREService.BuildRowAsync — DbKey={DbKey} ReArnAcct={ReArn}",
                dbKey, reArnAcct);

            var netIncome = await _repo.GetNetIncomeByEntityAsync(
                dbKey,
                glParams.EntityIds,
                glParams.BegYrPd,
                glParams.EndPeriod,
                glParams.BasisList,
                glParams.LedgLo,
                glParams.LedgHi,
                reArnAcct);

            // Sum across all entities for single-column reports
            var total = netIncome.Values.Sum(v => v ?? 0m);

            if (wholeDollars)
                total = Math.Round(total, 0, MidpointRounding.AwayFromZero);

            _logger.LogInformation(
                "UnpostedREService — NetIncome={NetIncome} ReArnAcct={ReArn} " +
                "Entities=[{Entities}]",
                total, reArnAcct, string.Join(",", glParams.EntityIds));

            var row = BuildRow(columnId, total);
            return ServiceResult<ReportRow?>.Success(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "UnpostedREService.BuildRowAsync failed — DbKey={DbKey}", dbKey);
            return ServiceResult<ReportRow?>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ReportRow?>> BuildConsolidatedRowAsync(
        string dbKey,
        GlQueryParameters glParams,
        string reArnAcct,
        IReadOnlyList<string> columnIds,
        bool wholeDollars)
    {
        try
        {
            _logger.LogDebug(
                "UnpostedREService.BuildConsolidatedRowAsync — DbKey={DbKey} " +
                "Columns={Count} ReArnAcct={ReArn}",
                dbKey, columnIds.Count, reArnAcct);

            var netIncomeByEntity = await _repo.GetNetIncomeByEntityAsync(
                dbKey,
                glParams.EntityIds,
                glParams.BegYrPd,
                glParams.EndPeriod,
                glParams.BasisList,
                glParams.LedgLo,
                glParams.LedgHi,
                reArnAcct);

            // Build a row with one cell per column (entity)
            var row = new ReportRow
            {
                RowType     = RowType.UnpostedRetainedEarnings,
                AccountCode = string.Empty,
                AccountName = RowLabel,
                Indent      = 0,
                Cells       = new Dictionary<string, CellValue>()
            };

            foreach (var colId in columnIds)
            {
                netIncomeByEntity.TryGetValue(colId.ToUpper(), out var val);
                if (wholeDollars && val.HasValue)
                    val = Math.Round(val.Value, 0, MidpointRounding.AwayFromZero);

                // UnpostedRetainedEarnings rows are never drillable
                row.Cells[colId] = new CellValue(val);
            }

            return ServiceResult<ReportRow?>.Success(row);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "UnpostedREService.BuildConsolidatedRowAsync failed — DbKey={DbKey}", dbKey);
            return ServiceResult<ReportRow?>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

    private static ReportRow BuildRow(string columnId, decimal total) =>
        new()
        {
            RowType     = RowType.UnpostedRetainedEarnings,
            AccountCode = string.Empty,
            AccountName = RowLabel,
            Indent      = 0,
            Cells       = new Dictionary<string, CellValue>
            {
                // UnpostedRetainedEarnings rows are never drillable
                [columnId] = new CellValue(total == 0m ? null : total)
            }
        };
}
