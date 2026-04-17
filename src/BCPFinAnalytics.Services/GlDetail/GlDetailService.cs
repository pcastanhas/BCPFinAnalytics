using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.GlDetail;

/// <summary>
/// Wraps IGlDetailRepository — provides GL transaction detail to the drill-down modal.
/// </summary>
public class GlDetailService : IGlDetailService
{
    private readonly IGlDetailRepository _repo;
    private readonly ILogger<GlDetailService> _logger;

    public GlDetailService(
        IGlDetailRepository repo,
        ILogger<GlDetailService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IEnumerable<GlDetailRow>>> GetDetailAsync(
        string dbKey,
        DrillDownRef drillDown)
    {
        _logger.LogInformation(
            "GlDetailService.GetDetailAsync — DbKey={DbKey} " +
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
            var rows = await _repo.GetDetailAsync(dbKey, drillDown);
            return ServiceResult<IEnumerable<GlDetailRow>>.Success(rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GlDetailService.GetDetailAsync failed — DbKey={DbKey} " +
                "Entities=[{Entities}] AcctNums=[{AcctNums}]",
                dbKey,
                string.Join(",", drillDown.EntityIds),
                string.Join(",", drillDown.AcctNums));

            return ServiceResult<IEnumerable<GlDetailRow>>.FromException(
                ex, ErrorCode.DatabaseError);
        }
    }
}
