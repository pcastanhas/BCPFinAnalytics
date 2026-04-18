using BCPFinAnalytics.DAL;
using BCPFinAnalytics.Services.Helpers;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Reports.TrialBalanceDC;

/// <summary>
/// Executes the two GL queries needed for the Debit/Credit Trial Balance.
/// Both queries share the same canonical GACC + GLSUM join pattern —
/// they differ only in the period range applied.
/// </summary>
public class TrialBalanceDCRepository : ITrialBalanceDCRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<TrialBalanceDCRepository> _logger;

    public TrialBalanceDCRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<TrialBalanceDCRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TrialBalanceDCRawRow>> GetStartingBalancesAsync(
        string dbKey,
        GlQueryParameters glParams,
        string periodBeforeStart)
    {
        // When StartPeriod == BegYrPd income accounts always have zero starting balance
        // so we skip the query for I accounts in that case — the strategy handles this
        // by only querying B/C accounts when periodBeforeStart < BegYrPd.
        // We still run one unified query and return all rows — zero-balance accounts
        // simply won't appear (GLSUM has no rows for them in this range).

        const string sql = """
            SELECT
                RTRIM(g.ACCTNUM)    AS AcctNum,
                RTRIM(g.ACCTNAME)   AS AcctName,
                RTRIM(g.TYPE)       AS Type,
                RTRIM(s.ENTITYID)   AS EntityId,
                SUM(s.ACTIVITY)     AS Amount
            FROM GACC g
            JOIN GLSUM s ON s.ACCTNUM = g.ACCTNUM
            WHERE g.ACCTNUM  >= @LedgLo
              AND g.ACCTNUM  <  @LedgHi
              AND s.ENTITYID IN @EntityIds
              AND s.BASIS    IN @BasisList
              AND s.PERIOD BETWEEN
                  CASE WHEN g.TYPE IN ('B','C') THEN @BalForPd ELSE @BegYrPd END
                  AND @PeriodBeforeStart
            GROUP BY g.ACCTNUM, g.ACCTNAME, g.TYPE, s.ENTITYID
            ORDER BY g.ACCTNUM
            """;

        var parameters = new
        {
            LedgLo          = glParams.LedgLo,
            LedgHi          = glParams.LedgHi,
            EntityIds        = glParams.EntityIds,
            BasisList        = glParams.BasisList,
            BalForPd         = glParams.BalForPd,
            BegYrPd          = glParams.BegYrPd,
            PeriodBeforeStart = periodBeforeStart
        };

        try
        {
            _logger.LogTrace(
                "TrialBalanceDCRepository.GetStartingBalancesAsync — " +
                "DbKey={DbKey} BalForPd={BalFor} BegYrPd={BegYr} " +
                "PeriodBeforeStart={PeriodBefore} Entities=[{Entities}] SQL={Sql}",
                dbKey, glParams.BalForPd, glParams.BegYrPd,
                periodBeforeStart,
                string.Join(",", glParams.EntityIds), sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows = await conn.QueryAsync<TrialBalanceDCRawRow>(sql, parameters);
            var result = rows.ToList();

            _logger.LogDebug(
                "TrialBalanceDCRepository.GetStartingBalancesAsync — {Count} rows DbKey={DbKey}",
                result.Count, dbKey);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TrialBalanceDCRepository.GetStartingBalancesAsync failed — DbKey={DbKey}",
                dbKey);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TrialBalanceDCRawRow>> GetActivityAsync(
        string dbKey,
        GlQueryParameters glParams,
        string startPeriod)
    {
        // Activity query: net movement between StartPeriod and EndPeriod.
        // Excludes BALFOR='B' rows — those are year-opening balance entries
        // that belong in the starting balance query only, not in Debits/Credits.
        const string sql = """
            SELECT
                RTRIM(g.ACCTNUM)    AS AcctNum,
                RTRIM(g.ACCTNAME)   AS AcctName,
                RTRIM(g.TYPE)       AS Type,
                RTRIM(s.ENTITYID)   AS EntityId,
                SUM(s.ACTIVITY)     AS Amount
            FROM GACC g
            JOIN GLSUM s ON s.ACCTNUM = g.ACCTNUM
            WHERE g.ACCTNUM  >= @LedgLo
              AND g.ACCTNUM  <  @LedgHi
              AND s.ENTITYID IN @EntityIds
              AND s.BASIS    IN @BasisList
              AND s.PERIOD   BETWEEN @StartPeriod AND @EndPeriod
              AND s.BALFOR   = 'N'
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
            EndPeriod   = glParams.EndPeriod
        };

        try
        {
            _logger.LogTrace(
                "TrialBalanceDCRepository.GetActivityAsync — " +
                "DbKey={DbKey} StartPeriod={Start} EndPeriod={End} " +
                "Entities=[{Entities}] SQL={Sql}",
                dbKey, startPeriod, glParams.EndPeriod,
                string.Join(",", glParams.EntityIds), sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows = await conn.QueryAsync<TrialBalanceDCRawRow>(sql, parameters);
            var result = rows.ToList();

            _logger.LogDebug(
                "TrialBalanceDCRepository.GetActivityAsync — {Count} rows DbKey={DbKey}",
                result.Count, dbKey);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TrialBalanceDCRepository.GetActivityAsync failed — DbKey={DbKey}", dbKey);
            throw;
        }
    }
}
