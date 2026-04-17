using BCPFinAnalytics.DAL;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL.Interfaces;

// ══════════════════════════════════════════════════════════════
//  IUnpostedRERepository
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Queries the net income (unposted retained earnings) for a set of entities
/// for use in balance sheet and trial balance reports.
///
/// Unposted retained earnings = sum of all income statement account activity
/// for the current fiscal year that has not yet been formally closed/posted
/// to the retained earnings account (GLCD.REARNACC).
///
/// This is a synthetic value computed from GLSUM — it represents what would
/// be posted to retained earnings if the books were closed today.
/// </summary>
public interface IUnpostedRERepository
{
    /// <summary>
    /// Returns the sum of all income statement (TYPE='I') account activity
    /// for the given entities, between BEGYRPD and EndPeriod.
    /// Keyed by EntityId — one decimal per entity (null if no activity).
    ///
    /// The retained earnings account (REARNACC) for the ledger is passed in
    /// so it can be excluded from the sum (to avoid double-counting on
    /// formats that already show the RE account directly).
    /// </summary>
    Task<Dictionary<string, decimal?>> GetNetIncomeByEntityAsync(
        string dbKey,
        IReadOnlyList<string> entityIds,
        string begYrPd,
        string endPeriod,
        IReadOnlyList<string> basisList,
        string ledgLo,
        string ledgHi,
        string reArnAcct);
}

// ══════════════════════════════════════════════════════════════
//  UnpostedRERepository
// ══════════════════════════════════════════════════════════════

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
        // Sum all income statement accounts (TYPE='I') for the period
        // Exclude the retained earnings account itself to avoid double-counting
        const string sql = """
            SELECT
                s.ENTITYID                  AS EntityId,
                SUM(s.ACTIVITY)             AS NetIncome
            FROM GACC g
            JOIN GLSUM s ON s.ACCTNUM = g.ACCTNUM
            WHERE g.ACCTNUM >= @LedgLo
              AND g.ACCTNUM <  @LedgHi
              AND g.TYPE    =  'I'
              AND g.ACCTNUM <> @ReArnAcct
              AND s.ENTITYID IN @EntityIds
              AND s.BASIS    IN @BasisList
              AND s.PERIOD   BETWEEN @BegYrPd AND @EndPeriod
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
                "DbKey={DbKey} Entities=[{Entities}] BegYrPd={BegYr} EndPeriod={End} " +
                "ReArnAcct={ReArn} SQL={Sql}",
                dbKey,
                string.Join(",", entityIds),
                begYrPd, endPeriod, reArnAcct, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows = await conn.QueryAsync<(string EntityId, decimal? NetIncome)>(sql, parameters);

            var result = rows.ToDictionary(
                r => r.EntityId.Trim().ToUpper(),
                r => r.NetIncome);

            _logger.LogDebug(
                "UnpostedRERepository.GetNetIncomeByEntityAsync — " +
                "DbKey={DbKey} Entities=[{Entities}] Results={Count}",
                dbKey, string.Join(",", entityIds), result.Count);

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
