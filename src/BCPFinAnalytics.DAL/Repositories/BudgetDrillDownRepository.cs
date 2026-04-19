using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.DAL.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL.Repositories;

/// <summary>
/// Retrieves budget detail rows from the BUDGETS table.
/// Returns one row per ACCTNUM + ENTITYID + DEPARTMENT + BASIS + BUDTYPE + PERIOD.
/// </summary>
public class BudgetDrillDownRepository : IBudgetDrillDownRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<BudgetDrillDownRepository> _logger;

    public BudgetDrillDownRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<BudgetDrillDownRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger            = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<BudgetDetailRow>> GetTransactionsAsync(
        string dbKey,
        BudgetDrillDownRef drillDown)
    {
        const string sql = """
            SELECT
                RTRIM(b.PERIOD)     AS Period,
                RTRIM(b.ENTITYID)   AS EntityId,
                RTRIM(b.ACCTNUM)    AS AcctNum,
                RTRIM(b.DEPARTMENT) AS Department,
                RTRIM(b.BASIS)      AS Basis,
                RTRIM(b.BUDTYPE)    AS BudType,
                ISNULL(b.ACTIVITY, 0) AS Activity
            FROM BUDGETS b
            WHERE b.ENTITYID IN @EntityIds
              AND b.ACCTNUM   IN @AcctNums
              AND b.BUDTYPE   =  @BudgetType
              AND b.PERIOD    BETWEEN @PeriodFrom AND @PeriodTo
            ORDER BY b.PERIOD, b.ACCTNUM, b.ENTITYID, b.DEPARTMENT, b.BASIS
            """;

        var parameters = new
        {
            EntityIds  = drillDown.EntityIds.ToList(),
            AcctNums   = drillDown.AcctNums.ToList(),
            BudgetType = drillDown.BudgetType,
            PeriodFrom = drillDown.PeriodFrom,
            PeriodTo   = drillDown.PeriodTo
        };

        try
        {
            _logger.LogTrace(
                "BudgetDrillDownRepository.GetTransactionsAsync — DbKey={DbKey} " +
                "Entities=[{Entities}] AcctNums=[{AcctNums}] " +
                "BudgetType={BudType} Period={From}-{To}",
                dbKey,
                string.Join(",", drillDown.EntityIds),
                string.Join(",", drillDown.AcctNums),
                drillDown.BudgetType,
                drillDown.PeriodFrom, drillDown.PeriodTo);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows = await conn.QueryAsync<BudgetDetailRow>(sql, parameters);
            var result = rows.ToList();

            _logger.LogDebug(
                "BudgetDrillDownRepository.GetTransactionsAsync — {Count} rows DbKey={DbKey}",
                result.Count, dbKey);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "BudgetDrillDownRepository.GetTransactionsAsync failed — DbKey={DbKey}", dbKey);
            throw;
        }
    }
}
