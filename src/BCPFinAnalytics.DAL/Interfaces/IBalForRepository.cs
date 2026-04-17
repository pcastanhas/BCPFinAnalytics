using BCPFinAnalytics.DAL;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL.Interfaces;

// ══════════════════════════════════════════════════════════════
//  IBalForRepository
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Queries the PERIOD table to resolve the BALFORPD anchor —
/// the beginning-of-year period for balance sheet account accumulation.
/// </summary>
public interface IBalForRepository
{
    /// <summary>
    /// Returns the BALFOR anchor period for a given entity and end period.
    ///
    /// Query:
    ///   SELECT ISNULL(MAX(PERIOD), '200001')
    ///   FROM PERIOD
    ///   WHERE BALFOR = 'B'
    ///     AND PERIOD &lt;= @EndPeriod
    ///     AND ENTITYID = @EntityId
    ///
    /// Returns '200001' (sentinel) when the entity has no B-row in PERIOD
    /// (brand-new entity with no closed fiscal years yet).
    ///
    /// This query is always scoped to a single representative entity —
    /// preflight has guaranteed all selected entities share the same YEAREND,
    /// so any entity will yield the same BALFORPD.
    /// </summary>
    Task<string> GetBalForAnchorAsync(string dbKey, string entityId, string endPeriod);
}

// ══════════════════════════════════════════════════════════════
//  BalForRepository
// ══════════════════════════════════════════════════════════════

namespace BCPFinAnalytics.DAL.Repositories;

/// <summary>
/// Queries the PERIOD table for the BALFOR anchor period.
/// </summary>
public class BalForRepository : IBalForRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<BalForRepository> _logger;

    public BalForRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<BalForRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetBalForAnchorAsync(
        string dbKey, string entityId, string endPeriod)
    {
        const string sql = """
            SELECT ISNULL(MAX(PERIOD), '200001')
            FROM PERIOD
            WHERE BALFOR  = 'B'
              AND PERIOD  <= @EndPeriod
              AND ENTITYID = @EntityId
            """;

        try
        {
            _logger.LogTrace(
                "BalForRepository.GetBalForAnchorAsync — DbKey={DbKey} " +
                "EntityId={EntityId} EndPeriod={EndPeriod} SQL={Sql}",
                dbKey, entityId, endPeriod, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var result = await conn.ExecuteScalarAsync<string>(
                sql, new { EntityId = entityId, EndPeriod = endPeriod });

            // ExecuteScalar can return null if PERIOD table is empty — fall back to sentinel
            var balForPd = result ?? Services.Helpers.FiscalCalendar.NewEntitySentinel;

            _logger.LogDebug(
                "BalForRepository.GetBalForAnchorAsync — DbKey={DbKey} " +
                "EntityId={EntityId} EndPeriod={EndPeriod} BalForPd={BalForPd}",
                dbKey, entityId, endPeriod, balForPd);

            return balForPd;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "BalForRepository.GetBalForAnchorAsync failed — DbKey={DbKey} " +
                "EntityId={EntityId} EndPeriod={EndPeriod}",
                dbKey, entityId, endPeriod);
            throw;
        }
    }
}
