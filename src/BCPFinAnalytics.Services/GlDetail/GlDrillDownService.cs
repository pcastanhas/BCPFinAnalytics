using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.GlDetail;

/// <summary>
/// Wraps IGlDrillDownRepository — provides GL transaction detail to the drill-down modal.
/// </summary>
public class GlDrillDownService : IGlDrillDownService
{
    private readonly IGlDrillDownRepository _repo;
    private readonly ILogger<GlDrillDownService> _logger;

    public GlDrillDownService(
        IGlDrillDownRepository repo,
        ILogger<GlDrillDownService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IEnumerable<GlDetailRow>>> GetTransactionsAsync(
        string dbKey,
        DrillDownRef drillDown)
    {
        _logger.LogInformation(
            "GlDrillDownService.GetTransactionsAsync — DbKey={DbKey} " +
            "Entities=[{Entities}] AcctNums=[{AcctNums}] " +
            "Period={From}-{To} Basis=[{Basis}] Label={Label}",
            dbKey,
            string.Join(",", drillDown.EntityIds),
            string.Join(",", drillDown.AcctNums),
            drillDown.PeriodFrom,
            drillDown.PeriodTo,
            string.Join(",", drillDown.BasisList),
            drillDown.DisplayLabel);

        try
        {
            var rows = await _repo.GetTransactionsAsync(dbKey, drillDown);
            return ServiceResult<IEnumerable<GlDetailRow>>.Success(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GlDrillDownService.GetTransactionsAsync failed — DbKey={DbKey} " +
                "Entities=[{Entities}] AcctNums=[{AcctNums}]",
                dbKey,
                string.Join(",", drillDown.EntityIds),
                string.Join(",", drillDown.AcctNums));

            return ServiceResult<IEnumerable<GlDetailRow>>.FromException(
                ex, ErrorCode.DatabaseError);
        }
    }
}
