using BCPFinAnalytics.DAL;
using BCPFinAnalytics.Services.Helpers;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Reports.TrialBalance;

/// <summary>
/// Queries GACC + GLSUM for trial balance data.
///
/// Uses the canonical unified query pattern with a CASE expression
/// to apply the correct period range per account type:
///   TYPE IN ('B','C') → accumulate from BALFORPD (balance sheet accounts)
///   TYPE = 'I'        → accumulate from BEGYRPD  (income accounts)
///
/// Returns one row per (ACCTNUM, ENTITYID) — the strategy aggregates
/// across entities when multiple are selected.
/// </summary>
public class TrialBalanceRepository : ITrialBalanceRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<TrialBalanceRepository> _logger;

    public TrialBalanceRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<TrialBalanceRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<TrialBalanceRawRow>> GetBalancesAsync(
        string dbKey,
        GlQueryParameters glParams)
    {
        // ── Canonical unified GL query ──────────────────────────────────────
        // CASE drives the period range per account type:
        //   B/C accounts accumulate from fiscal year open (BALFORPD)
        //   I   accounts accumulate from current year start (BEGYRPD)
        const string sql = """
            SELECT
                RTRIM(g.ACCTNUM)    AS AcctNum,
                RTRIM(g.ACCTNAME)   AS AcctName,
                RTRIM(g.TYPE)       AS Type,
                RTRIM(s.ENTITYID)   AS EntityId,
                SUM(s.ACTIVITY)     AS Balance
            FROM GACC g
            JOIN GLSUM s ON s.ACCTNUM = g.ACCTNUM
            WHERE g.ACCTNUM   >= @LedgLo
              AND g.ACCTNUM   <  @LedgHi
              AND s.ENTITYID  IN @EntityIds
              AND s.BASIS     IN @BasisList
              AND s.PERIOD BETWEEN
                  CASE WHEN g.TYPE IN ('B','C') THEN @BalForPd ELSE @BegYrPd END
                  AND @EndPeriod
            GROUP BY g.ACCTNUM, g.ACCTNAME, g.TYPE, s.ENTITYID
            ORDER BY g.ACCTNUM
            """;

        var parameters = new
        {
            LedgLo    = glParams.LedgLo,
            LedgHi    = glParams.LedgHi,
            EntityIds = glParams.EntityIds,
            BasisList = glParams.BasisList,
            BalForPd  = glParams.BalForPd,
            BegYrPd   = glParams.BegYrPd,
            EndPeriod = glParams.EndPeriod
        };

        try
        {
            _logger.LogTrace(
                "TrialBalanceRepository.GetBalancesAsync — " +
                "DbKey={DbKey} LedgLo={Lo} LedgHi={Hi} " +
                "BalForPd={BalFor} BegYrPd={BegYr} EndPeriod={End} " +
                "Entities=[{Entities}] Basis=[{Basis}] SQL={Sql}",
                dbKey,
                glParams.LedgLo, glParams.LedgHi,
                glParams.BalForPd, glParams.BegYrPd, glParams.EndPeriod,
                string.Join(",", glParams.EntityIds),
                string.Join(",", glParams.BasisList),
                sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows = await conn.QueryAsync<TrialBalanceRawRow>(sql, parameters);

            var result = rows.ToList();

            _logger.LogDebug(
                "TrialBalanceRepository.GetBalancesAsync — returned {Count} rows " +
                "DbKey={DbKey} EndPeriod={End}",
                result.Count, dbKey, glParams.EndPeriod);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TrialBalanceRepository.GetBalancesAsync failed — " +
                "DbKey={DbKey} EndPeriod={End}",
                dbKey, glParams.EndPeriod);
            throw;
        }
    }
}
