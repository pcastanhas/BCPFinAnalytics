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
