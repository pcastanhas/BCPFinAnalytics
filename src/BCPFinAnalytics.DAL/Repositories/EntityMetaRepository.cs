using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.DAL.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL.Repositories;

/// <summary>
/// Entity-level metadata queries for preflight validation.
///
/// All queries use SARGable WHERE clauses — no functions on indexed columns.
/// The SelectionMode drives which WHERE clause variant is used.
///
/// Range mode passes exactly two IDs: selectedIds[0] = lo, selectedIds[1] = hi.
/// The caller (ReportPreflightService) is responsible for ensuring Range has
/// exactly 2 IDs before calling these methods.
/// </summary>
public class EntityMetaRepository : IEntityMetaRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<EntityMetaRepository> _logger;

    public EntityMetaRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<EntityMetaRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> GetDistinctYearEndCountAsync(
        string dbKey,
        SelectionMode selectionMode,
        IReadOnlyList<string> selectedIds)
    {
        var (sql, parameters) = BuildYearEndQuery(
            "SELECT COUNT(DISTINCT YEAREND)", selectionMode, selectedIds);

        try
        {
            _logger.LogTrace(
                "EntityMetaRepository.GetDistinctYearEndCountAsync — DbKey={DbKey} Mode={Mode} SQL={Sql}",
                dbKey, selectionMode, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var count = await conn.ExecuteScalarAsync<int>(sql, parameters);

            _logger.LogDebug(
                "EntityMetaRepository.GetDistinctYearEndCountAsync — DbKey={DbKey} Mode={Mode} Count={Count}",
                dbKey, selectionMode, count);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EntityMetaRepository.GetDistinctYearEndCountAsync failed — DbKey={DbKey} Mode={Mode}",
                dbKey, selectionMode);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<string>> GetDistinctYearEndsAsync(
        string dbKey,
        SelectionMode selectionMode,
        IReadOnlyList<string> selectedIds)
    {
        var (sql, parameters) = BuildYearEndQuery(
            "SELECT DISTINCT YEAREND", selectionMode, selectedIds);

        // Append ORDER BY for consistent error message output
        sql += " ORDER BY YEAREND";

        try
        {
            _logger.LogTrace(
                "EntityMetaRepository.GetDistinctYearEndsAsync — DbKey={DbKey} Mode={Mode} SQL={Sql}",
                dbKey, selectionMode, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var yearEnds = await conn.QueryAsync<string>(sql, parameters);

            return yearEnds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EntityMetaRepository.GetDistinctYearEndsAsync failed — DbKey={DbKey} Mode={Mode}",
                dbKey, selectionMode);
            throw;
        }
    }

    /// <summary>
    /// Builds the SELECT + FROM + WHERE clause for YEAREND queries.
    /// The SELECT projection is passed in so both COUNT and SELECT DISTINCT
    /// share the same WHERE-clause logic.
    /// </summary>
    private static (string Sql, object? Parameters) BuildYearEndQuery(
        string selectClause,
        SelectionMode selectionMode,
        IReadOnlyList<string> selectedIds)
    {
        return selectionMode switch
        {
            SelectionMode.All =>
                ($"{selectClause} FROM ENTITY", null),

            SelectionMode.Include =>
                ($"{selectClause} FROM ENTITY WHERE ENTITYID IN @Ids",
                 new { Ids = selectedIds }),

            SelectionMode.Exclude =>
                ($"{selectClause} FROM ENTITY WHERE ENTITYID NOT IN @Ids",
                 new { Ids = selectedIds }),

            SelectionMode.Range =>
                ($"{selectClause} FROM ENTITY WHERE ENTITYID BETWEEN @Lo AND @Hi",
                 new { Lo = selectedIds[0], Hi = selectedIds[1] }),

            _ => throw new ArgumentOutOfRangeException(
                     nameof(selectionMode), selectionMode,
                     "Unrecognised SelectionMode in EntityMetaRepository")
        };
    }
}
