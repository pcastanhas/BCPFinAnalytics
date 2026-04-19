using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.GlDetail;

/// <summary>
/// Thin wrapper around <c>IStartingBalanceRepository</c>. All correctness lives
/// in the repository (BALFOR filtering, four-case matrix for account-type and
/// period kind). The service adds logging and ServiceResult error wrapping.
/// </summary>
public class StartingBalanceService : IStartingBalanceService
{
    private readonly IStartingBalanceRepository _repo;
    private readonly ILogger<StartingBalanceService> _logger;

    public StartingBalanceService(
        IStartingBalanceRepository repo,
        ILogger<StartingBalanceService> logger)
    {
        _repo = repo;
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

        try
        {
            var balance = await _repo.GetStartingBalanceAsync(dbKey, drillDown);
            return ServiceResult<decimal>.Success(balance);
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
