using BCPFinAnalytics.DAL;
using BCPFinAnalytics.Services.Helpers;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Reports.Trailing12;

/// <summary>
/// Queries GACC + GLSUM for trailing 12 month activity.
///
/// Single query fetches all 12 periods at once using PERIOD IN @Periods.
/// BALFOR='N' excludes opening balance rollup rows — activity only.
/// Returns one row per ACCTNUM + ENTITYID + PERIOD.
/// Strategy aggregates across entities and pivots by period.
/// </summary>
public class Trailing12Repository : ITrailing12Repository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<Trailing12Repository> _logger;

    public Trailing12Repository(
        IDbConnectionFactory connectionFactory,
        ILogger<Trailing12Repository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger            = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Trailing12RawRow>> GetActivityAsync(
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
                "Trailing12Repository.GetActivityAsync — DbKey={DbKey} " +
                "Periods=[{Periods}] Entities=[{Entities}] Basis=[{Basis}]",
                dbKey,
                string.Join(",", periods),
                string.Join(",", glParams.EntityIds),
                string.Join(",", glParams.BasisList));

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows   = await conn.QueryAsync<Trailing12RawRow>(sql, parameters);
            var result = rows.ToList();

            _logger.LogDebug(
                "Trailing12Repository.GetActivityAsync — {Count} rows DbKey={DbKey}",
                result.Count, dbKey);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Trailing12Repository.GetActivityAsync failed — DbKey={DbKey}", dbKey);
            throw;
        }
    }
}
