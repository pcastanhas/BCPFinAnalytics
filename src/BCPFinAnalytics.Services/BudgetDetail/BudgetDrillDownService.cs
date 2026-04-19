using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.BudgetDetail;

/// <summary>
/// Thin wrapper around <c>IBudgetDrillDownRepository</c>. All SQL lives in the
/// repository; the service layer adds logging and ServiceResult error wrapping,
/// matching <c>GlDrillDownService</c> on the GL side.
/// </summary>
public class BudgetDrillDownService : IBudgetDrillDownService
{
    private readonly IBudgetDrillDownRepository _repo;
    private readonly ILogger<BudgetDrillDownService> _logger;

    public BudgetDrillDownService(
        IBudgetDrillDownRepository repo,
        ILogger<BudgetDrillDownService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IEnumerable<BudgetDetailRow>>> GetTransactionsAsync(
        string dbKey,
        BudgetDrillDownRef drillDown)
    {
        _logger.LogInformation(
            "BudgetDrillDownService.GetTransactionsAsync — DbKey={DbKey} " +
            "Entities=[{Entities}] AcctNums=[{AcctNums}] " +
            "Period={From}-{To} BudgetType={Type} Label={Label}",
            dbKey,
            string.Join(",", drillDown.EntityIds),
            string.Join(",", drillDown.AcctNums),
            drillDown.PeriodFrom,
            drillDown.PeriodTo,
            drillDown.BudgetType,
            drillDown.DisplayLabel);

        try
        {
            var rows = await _repo.GetTransactionsAsync(dbKey, drillDown);
            return ServiceResult<IEnumerable<BudgetDetailRow>>.Success(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "BudgetDrillDownService.GetTransactionsAsync failed — DbKey={DbKey} " +
                "Entities=[{Entities}] AcctNums=[{AcctNums}]",
                dbKey,
                string.Join(",", drillDown.EntityIds),
                string.Join(",", drillDown.AcctNums));

            return ServiceResult<IEnumerable<BudgetDetailRow>>.FromException(
                ex, ErrorCode.DatabaseError);
        }
    }
}
