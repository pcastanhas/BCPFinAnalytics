using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.DAL.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL.Repositories;

/// <summary>
/// The canonical budget data layer. One SQL query — every budget-column in
/// every report composes from it.
///
/// Differs from the prior per-report budget queries (IncomeStatementRepository,
/// Trailing12Repository, Forecast12Repository) in one way: this query FILTERS
/// BY BASIS. The old queries summed across all basis values for a given
/// BUDTYPE — which could produce a budget total that didn't reconcile against
/// an actual total for the same basis selection. Budget-column reports (IS,
/// T12 Budget, FC12) will show numbers that shift (correctly) after this
/// lands. Needs MRI verification as part of QA.
/// </summary>
public class BudgetDataRepository : IBudgetDataRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<BudgetDataRepository> _logger;

    public BudgetDataRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<BudgetDataRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, AccountAmount>> GetBudgetAmountAsync(
        string dbKey,
        string startPeriod,
        string endPeriod,
        string budgetType,
        string ledgLo,
        string ledgHi,
        IReadOnlyList<string> entityIds,
        IReadOnlyList<string> basisList)
    {
        if (string.IsNullOrEmpty(startPeriod) || string.IsNullOrEmpty(endPeriod)
            || string.IsNullOrEmpty(budgetType)
            || entityIds.Count == 0 || basisList.Count == 0)
            return new Dictionary<string, AccountAmount>();

        var expandedBasis = ExpandBasis(basisList);

        const string sql = """
            SELECT
                RTRIM(g.ACCTNUM)  AS AcctNum,
                RTRIM(g.ACCTNAME) AS AcctName,
                RTRIM(g.TYPE)     AS Type,
                ISNULL(SUM(b.ACTIVITY), 0) AS Amount
            FROM GACC g
            JOIN BUDGETS b ON b.ACCTNUM = g.ACCTNUM
            WHERE g.ACCTNUM  >= @LedgLo
              AND g.ACCTNUM  <  @LedgHi
              AND b.ENTITYID IN @EntityIds
              AND b.BASIS    IN @Basis
              AND b.BUDTYPE  =  @BudgetType
              AND b.PERIOD   BETWEEN @StartPeriod AND @EndPeriod
            GROUP BY g.ACCTNUM, g.ACCTNAME, g.TYPE
            """;

        var parameters = new
        {
            LedgLo      = ledgLo,
            LedgHi      = ledgHi,
            EntityIds   = entityIds,
            Basis       = expandedBasis,
            BudgetType  = budgetType,
            StartPeriod = startPeriod,
            EndPeriod   = endPeriod
        };

        try
        {
            _logger.LogTrace(
                "BudgetDataRepository.GetBudgetAmountAsync — DbKey={DbKey} " +
                "Period={Start}-{End} BudType={BT} Basis=[{B}] Entities=[{E}]",
                dbKey, startPeriod, endPeriod, budgetType,
                string.Join(",", expandedBasis),
                string.Join(",", entityIds));

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows = await conn.QueryAsync<(string AcctNum, string AcctName, string Type, decimal Amount)>(
                sql, parameters);

            var result = rows.ToDictionary(
                r => r.AcctNum,
                r => new AccountAmount(r.AcctNum, r.AcctName, r.Type, r.Amount));

            _logger.LogDebug(
                "BudgetDataRepository.GetBudgetAmountAsync — {Count} accounts DbKey={DbKey}",
                result.Count, dbKey);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "BudgetDataRepository.GetBudgetAmountAsync failed — DbKey={DbKey} " +
                "Period={Start}-{End} BudType={BT}",
                dbKey, startPeriod, endPeriod, budgetType);
            throw;
        }
    }

    /// <summary>
    /// MRI basis expansion: if caller selected A or C, also include B. Mirrors
    /// GlDataRepository and GlDrillDownRepository so budget totals reconcile
    /// with actuals and drill detail exactly.
    /// </summary>
    private static IReadOnlyList<string> ExpandBasis(IReadOnlyList<string> basis)
    {
        if (basis.Contains("A") || basis.Contains("C"))
        {
            var expanded = basis.ToList();
            if (!expanded.Contains("B")) expanded.Add("B");
            return expanded;
        }
        return basis;
    }
}
