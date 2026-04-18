using BCPFinAnalytics.DAL;
using BCPFinAnalytics.Services.Helpers;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Reports.IncomeStatement;

/// <summary>
/// Queries GACC + GLSUM for Income Statement actual and budget data.
///
/// Both queries use PERIOD BETWEEN @StartPeriod AND @EndPeriod
/// with BALFOR='N' to get activity only (no opening balance rollup rows).
///
/// Actual query: BASIS IN @BasisList (user-selected actual basis)
/// Budget query: BASIS = @BudgetBasis (single budget basis code from GBTY)
/// </summary>
public class IncomeStatementRepository : IIncomeStatementRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<IncomeStatementRepository> _logger;

    public IncomeStatementRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<IncomeStatementRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger            = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<IncomeStatementRawRow>> GetActualAsync(
        string dbKey,
        GlQueryParameters glParams,
        string startPeriod,
        string endPeriod)
    {
        const string sql = """
            SELECT
                RTRIM(g.ACCTNUM)   AS AcctNum,
                RTRIM(g.ACCTNAME)  AS AcctName,
                RTRIM(g.TYPE)      AS Type,
                RTRIM(s.ENTITYID)  AS EntityId,
                SUM(s.ACTIVITY)    AS Amount
            FROM GACC g
            JOIN GLSUM s ON s.ACCTNUM = g.ACCTNUM
            WHERE g.ACCTNUM  >= @LedgLo
              AND g.ACCTNUM  <  @LedgHi
              AND s.ENTITYID IN @EntityIds
              AND s.BASIS    IN @BasisList
              AND s.BALFOR   =  'N'
              AND s.PERIOD   BETWEEN @StartPeriod AND @EndPeriod
            GROUP BY g.ACCTNUM, g.ACCTNAME, g.TYPE, s.ENTITYID
            ORDER BY g.ACCTNUM
            """;

        var parameters = new
        {
            LedgLo      = glParams.LedgLo,
            LedgHi      = glParams.LedgHi,
            EntityIds   = glParams.EntityIds,
            BasisList   = glParams.BasisList,
            StartPeriod = startPeriod,
            EndPeriod   = endPeriod
        };

        try
        {
            _logger.LogTrace(
                "IncomeStatementRepository.GetActualAsync — DbKey={DbKey} " +
                "Period={Start}-{End} Entities=[{Entities}]",
                dbKey, startPeriod, endPeriod, string.Join(",", glParams.EntityIds));

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows = await conn.QueryAsync<IncomeStatementRawRow>(sql, parameters);
            var result = rows.ToList();

            _logger.LogDebug(
                "IncomeStatementRepository.GetActualAsync — {Count} rows DbKey={DbKey}",
                result.Count, dbKey);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "IncomeStatementRepository.GetActualAsync failed — DbKey={DbKey}", dbKey);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<IncomeStatementRawRow>> GetBudgetAsync(
        string dbKey,
        GlQueryParameters glParams,
        string startPeriod,
        string endPeriod,
        string budgetType)
    {
        // Budget data comes from the BUDGETS table, not GLSUM.
        // Sum across all BASIS and DEPARTMENT values for the given BUDTYPE.
        // ACCTNUM in BUDGETS is the same format as GACC.ACCTNUM (FK enforced).
        const string sql = """
            SELECT
                RTRIM(g.ACCTNUM)   AS AcctNum,
                RTRIM(g.ACCTNAME)  AS AcctName,
                RTRIM(g.TYPE)      AS Type,
                RTRIM(b.ENTITYID)  AS EntityId,
                SUM(b.ACTIVITY)    AS Amount
            FROM GACC g
            JOIN BUDGETS b ON b.ACCTNUM = g.ACCTNUM
            WHERE g.ACCTNUM  >= @LedgLo
              AND g.ACCTNUM  <  @LedgHi
              AND b.ENTITYID IN @EntityIds
              AND b.BUDTYPE  =  @BudgetType
              AND b.PERIOD   BETWEEN @StartPeriod AND @EndPeriod
            GROUP BY g.ACCTNUM, g.ACCTNAME, g.TYPE, b.ENTITYID
            ORDER BY g.ACCTNUM
            """;

        var parameters = new
        {
            LedgLo      = glParams.LedgLo,
            LedgHi      = glParams.LedgHi,
            EntityIds   = glParams.EntityIds,
            BudgetType  = budgetType,
            StartPeriod = startPeriod,
            EndPeriod   = endPeriod
        };

        try
        {
            _logger.LogTrace(
                "IncomeStatementRepository.GetBudgetAsync — DbKey={DbKey} " +
                "Period={Start}-{End} BudgetType={BudType} Entities=[{Entities}]",
                dbKey, startPeriod, endPeriod, budgetType,
                string.Join(",", glParams.EntityIds));

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows = await conn.QueryAsync<IncomeStatementRawRow>(sql, parameters);
            var result = rows.ToList();

            _logger.LogDebug(
                "IncomeStatementRepository.GetBudgetAsync — {Count} rows DbKey={DbKey}",
                result.Count, dbKey);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "IncomeStatementRepository.GetBudgetAsync failed — DbKey={DbKey}", dbKey);
            throw;
        }
    }
}
