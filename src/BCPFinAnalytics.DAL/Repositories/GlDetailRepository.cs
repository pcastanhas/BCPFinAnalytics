using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.DAL.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL.Repositories;

/// <summary>
/// Retrieves GL transaction detail for the drill-down modal.
///
/// Query pattern (from MRI specification):
///   JOURNAL (open/current periods, STATUS = 'P')
///   UNION ALL
///   GHIS (closed periods, BALFOR = 'N')
///
/// Basis expansion rule:
///   User selects A (Accrual) or C (Cash) → B (Both) is automatically added.
///   User selects B directly → only B rows returned.
///   This matches how MRI's own reports handle basis filtering.
///
/// Supports consolidated reports:
///   AcctNums and EntityIds are both lists — Dapper IN clause handles multi-value.
/// </summary>
public class GlDetailRepository : IGlDetailRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<GlDetailRepository> _logger;

    public GlDetailRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<GlDetailRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<GlDetailRow>> GetDetailAsync(
        string dbKey,
        DrillDownRef drillDown)
    {
        // ── Basis expansion ────────────────────────────────────────────
        // If A or C is selected, B is automatically included per MRI convention.
        var expandedBasis = ExpandBasis(drillDown.BasisList);

        // ── Build SQL ──────────────────────────────────────────────────
        // Both JOURNAL and GHIS share identical column sets.
        // JOURNAL = open/current periods (STATUS = 'P' posted only)
        // GHIS    = closed periods (BALFOR = 'N' excludes beginning balance rows)
        const string sql = """
            -- Basis expansion: include B if A or C is selected
            DECLARE @basis_tbl TABLE (basis CHAR(1));
            INSERT INTO @basis_tbl
                SELECT DISTINCT b FROM (VALUES {0}) AS v(b);

            -- Open periods (JOURNAL)
            SELECT
                X.PERIOD        AS Period,
                X.REF           AS Ref,
                X.SOURCE        AS Source,
                X.BASIS         AS Basis,
                X.ENTITYID      AS EntityId,
                X.ACCTNUM       AS AcctNum,
                X.DEPARTMENT    AS Department,
                X.ITEM          AS Item,
                X.JOBCODE       AS JobCode,
                X.ENTRDATE      AS EntrDate,
                X.DESCRPTN      AS Descrptn,
                X.AMT           AS Amt
            FROM JOURNAL X
            JOIN @basis_tbl B ON B.basis = X.BASIS
            WHERE X.STATUS   = 'P'
              AND X.ENTITYID IN @EntityIds
              AND X.ACCTNUM  IN @AcctNums
              AND X.PERIOD   BETWEEN @PeriodFrom AND @PeriodTo

            UNION ALL

            -- Closed periods (GHIS)
            SELECT
                X.PERIOD        AS Period,
                X.REF           AS Ref,
                X.SOURCE        AS Source,
                X.BASIS         AS Basis,
                X.ENTITYID      AS EntityId,
                X.ACCTNUM       AS AcctNum,
                X.DEPARTMENT    AS Department,
                X.ITEM          AS Item,
                X.JOBCODE       AS JobCode,
                X.ENTRDATE      AS EntrDate,
                X.DESCRPTN      AS Descrptn,
                X.AMT           AS Amt
            FROM GHIS X
            JOIN @basis_tbl B ON B.basis = X.BASIS
            WHERE X.BALFOR   = 'N'
              AND X.ENTITYID IN @EntityIds
              AND X.ACCTNUM  IN @AcctNums
              AND X.PERIOD   BETWEEN @PeriodFrom AND @PeriodTo

            ORDER BY Period, Ref, Item;
            """;

        // Build the VALUES list for the table variable
        // e.g. ('A'),('C'),('B')
        var valuesClause = string.Join(",",
            expandedBasis.Select(b => $"('{b}')"));
        var finalSql = string.Format(sql, valuesClause);

        var parameters = new
        {
            EntityIds  = drillDown.EntityIds.ToList(),
            AcctNums   = drillDown.AcctNums.ToList(),
            PeriodFrom = drillDown.PeriodFrom,
            PeriodTo   = drillDown.PeriodTo
        };

        try
        {
            _logger.LogTrace(
                "GlDetailRepository.GetDetailAsync — DbKey={DbKey} " +
                "Entities=[{Entities}] AcctNums=[{AcctNums}] " +
                "Period={From}-{To} Basis=[{Basis}] SQL={Sql}",
                dbKey,
                string.Join(",", drillDown.EntityIds),
                string.Join(",", drillDown.AcctNums),
                drillDown.PeriodFrom, drillDown.PeriodTo,
                string.Join(",", expandedBasis),
                finalSql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows = await conn.QueryAsync<GlDetailRow>(finalSql, parameters);

            var result = rows.ToList();

            _logger.LogDebug(
                "GlDetailRepository.GetDetailAsync — returned {Count} rows " +
                "DbKey={DbKey} Entities=[{Entities}]",
                result.Count, dbKey, string.Join(",", drillDown.EntityIds));

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GlDetailRepository.GetDetailAsync failed — DbKey={DbKey} " +
                "Entities=[{Entities}] AcctNums=[{AcctNums}]",
                dbKey,
                string.Join(",", drillDown.EntityIds),
                string.Join(",", drillDown.AcctNums));
            throw;
        }
    }

    /// <summary>
    /// Applies the MRI basis expansion rule:
    /// If A (Accrual) or C (Cash) is in the list, B (Both) is automatically added.
    /// Duplicates are removed.
    /// </summary>
    private static IReadOnlyList<string> ExpandBasis(IReadOnlyList<string> basisList)
    {
        var expanded = new HashSet<string>(basisList, StringComparer.OrdinalIgnoreCase);

        if (expanded.Contains("A") || expanded.Contains("C"))
            expanded.Add("B");

        return expanded.OrderBy(b => b).ToList();
    }
}
