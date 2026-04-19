using BCPFinAnalytics.DAL;
using BCPFinAnalytics.Services.Helpers;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Reports.Forecast12;

public class Forecast12Repository : IForecast12Repository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<Forecast12Repository> _logger;

    public Forecast12Repository(
        IDbConnectionFactory connectionFactory,
        ILogger<Forecast12Repository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger            = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Forecast12RawRow>> GetActualAsync(
        string dbKey,
        GlQueryParameters glParams,
        IReadOnlyList<string> periods)
    {
        const string sql = """
            SELECT
                RTRIM(g.ACCTNUM)   AS AcctNum,
                RTRIM(g.ACCTNAME)  AS AcctName,
                RTRIM(g.TYPE)      AS Type,
                RTRIM(s.ENTITYID)  AS EntityId,
                RTRIM(s.PERIOD)    AS Period,
                SUM(s.ACTIVITY)    AS Amount
            FROM GACC g
            JOIN GLSUM s ON s.ACCTNUM = g.ACCTNUM
            WHERE g.ACCTNUM  >= @LedgLo
              AND g.ACCTNUM  <  @LedgHi
              AND s.ENTITYID IN @EntityIds
              AND s.BASIS    IN @BasisList
              AND s.BALFOR   =  'N'
              AND s.PERIOD   IN @Periods
            GROUP BY g.ACCTNUM, g.ACCTNAME, g.TYPE, s.ENTITYID, s.PERIOD
            ORDER BY g.ACCTNUM, s.PERIOD
            """;

        var parameters = new
        {
            LedgLo    = glParams.LedgLo,
            LedgHi    = glParams.LedgHi,
            EntityIds = glParams.EntityIds,
            BasisList = glParams.BasisList,
            Periods   = periods.ToList()
        };

        try
        {
            _logger.LogTrace(
                "Forecast12Repository.GetActualAsync — DbKey={DbKey} Periods=[{Periods}]",
                dbKey, string.Join(",", periods));

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            return (await conn.QueryAsync<Forecast12RawRow>(sql, parameters)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Forecast12Repository.GetActualAsync failed — DbKey={DbKey}", dbKey);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Forecast12RawRow>> GetBudgetAsync(
        string dbKey,
        GlQueryParameters glParams,
        IReadOnlyList<string> periods,
        string budgetType)
    {
        const string sql = """
            SELECT
                RTRIM(g.ACCTNUM)   AS AcctNum,
                RTRIM(g.ACCTNAME)  AS AcctName,
                RTRIM(g.TYPE)      AS Type,
                RTRIM(b.ENTITYID)  AS EntityId,
                RTRIM(b.PERIOD)    AS Period,
                ISNULL(SUM(b.ACTIVITY), 0) AS Amount
            FROM GACC g
            JOIN BUDGETS b ON b.ACCTNUM = g.ACCTNUM
            WHERE g.ACCTNUM  >= @LedgLo
              AND g.ACCTNUM  <  @LedgHi
              AND b.ENTITYID IN @EntityIds
              AND b.BUDTYPE  =  @BudgetType
              AND b.PERIOD   IN @Periods
            GROUP BY g.ACCTNUM, g.ACCTNAME, g.TYPE, b.ENTITYID, b.PERIOD
            ORDER BY g.ACCTNUM, b.PERIOD
            """;

        var parameters = new
        {
            LedgLo     = glParams.LedgLo,
            LedgHi     = glParams.LedgHi,
            EntityIds  = glParams.EntityIds,
            BudgetType = budgetType,
            Periods    = periods.ToList()
        };

        try
        {
            _logger.LogTrace(
                "Forecast12Repository.GetBudgetAsync — DbKey={DbKey} Budget={Budget} Periods=[{Periods}]",
                dbKey, budgetType, string.Join(",", periods));

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            return (await conn.QueryAsync<Forecast12RawRow>(sql, parameters)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Forecast12Repository.GetBudgetAsync failed — DbKey={DbKey}", dbKey);
            throw;
        }
    }
}
