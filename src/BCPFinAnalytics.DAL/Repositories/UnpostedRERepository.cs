using BCPFinAnalytics.DAL.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL.Repositories;

/// <summary>
/// Queries GLSUM for unposted retained earnings (net income) per entity.
/// </summary>
public class UnpostedRERepository : IUnpostedRERepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<UnpostedRERepository> _logger;

    public UnpostedRERepository(
        IDbConnectionFactory connectionFactory,
        ILogger<UnpostedRERepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, decimal?>> GetNetIncomeByEntityAsync(
        string dbKey,
        IReadOnlyList<string> entityIds,
        string begYrPd,
        string endPeriod,
        IReadOnlyList<string> basisList,
        string ledgLo,
        string ledgHi,
        string reArnAcct)
    {
        // Sum all income statement accounts (TYPE='I') for the period.
        // Exclude the retained earnings account itself to avoid double-counting.
        const string sql = """
            SELECT
                s.ENTITYID          AS EntityId,
                SUM(s.ACTIVITY)     AS NetIncome
            FROM GACC g
            JOIN GLSUM s ON s.ACCTNUM = g.ACCTNUM
            WHERE g.ACCTNUM  >= @LedgLo
              AND g.ACCTNUM  <  @LedgHi
              AND g.TYPE      = 'I'
              AND g.ACCTNUM  <> @ReArnAcct
              AND s.ENTITYID  IN @EntityIds
              AND s.BASIS     IN @BasisList
              AND s.PERIOD    BETWEEN @BegYrPd AND @EndPeriod
            GROUP BY s.ENTITYID
            """;

        var parameters = new
        {
            LedgLo    = ledgLo,
            LedgHi    = ledgHi,
            ReArnAcct = reArnAcct,
            EntityIds = entityIds,
            BasisList = basisList,
            BegYrPd   = begYrPd,
            EndPeriod = endPeriod
        };

        try
        {
            _logger.LogTrace(
                "UnpostedRERepository.GetNetIncomeByEntityAsync — " +
                "DbKey={DbKey} Entities=[{Entities}] BegYrPd={BegYr} " +
                "EndPeriod={End} ReArnAcct={ReArn} SQL={Sql}",
                dbKey, string.Join(",", entityIds),
                begYrPd, endPeriod, reArnAcct, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows = await conn.QueryAsync<(string EntityId, decimal? NetIncome)>(
                sql, parameters);

            var result = rows.ToDictionary(
                r => r.EntityId.Trim().ToUpper(),
                r => r.NetIncome);

            _logger.LogDebug(
                "UnpostedRERepository.GetNetIncomeByEntityAsync — " +
                "DbKey={DbKey} Results={Count}",
                dbKey, result.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "UnpostedRERepository.GetNetIncomeByEntityAsync failed — DbKey={DbKey}",
                dbKey);
            throw;
        }
    }
}
