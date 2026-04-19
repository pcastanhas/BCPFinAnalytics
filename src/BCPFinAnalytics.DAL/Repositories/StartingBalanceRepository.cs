using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.DAL.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL.Repositories;

/// <summary>
/// Computes the starting balance for a GL drill-down — the balance as of the
/// period immediately before <see cref="DrillDownRef.PeriodFrom"/>.
///
/// SQL is a port of the canonical MRI starting-balance logic. It differs from
/// the original TrialBalanceDCRepository starting-balance query (now
/// removed) in one critical way: it filters <c>GLSUM.BALFOR</c>. Without that
/// filter, every January fiscal-year-open period double-counts (once for the
/// BALFOR='B' snapshot row and once for the BALFOR='N' activity).
///
/// Four-case matrix — implemented via SQL CASE expressions:
///   B/C account + PeriodFrom IS balfor  → snapshot BALFOR='B' at PeriodFrom
///   B/C account + PeriodFrom NOT balfor → snapshot BALFOR='B' at the prior BalForPd
///   I  account  + PeriodFrom IS balfor  → no rows (no prior YTD — accounts reset)
///   I  account  + PeriodFrom NOT balfor → activity BALFOR='N' from fiscal-year
///                                          start through PeriodFrom-1
///
/// Account type is picked once per drill from the first account's GACC.TYPE
/// (a drill shouldn't mix B/C and I accounts — that would be a report format
/// error). Normalisation: 'C' (capital) is treated as 'B' for this purpose.
/// </summary>
public class StartingBalanceRepository : IStartingBalanceRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<StartingBalanceRepository> _logger;

    public StartingBalanceRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<StartingBalanceRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<decimal> GetStartingBalanceAsync(
        string dbKey, DrillDownRef drillDown)
    {
        if (drillDown.AcctNums.Count == 0
            || drillDown.EntityIds.Count == 0
            || drillDown.BasisList.Count == 0
            || string.IsNullOrEmpty(drillDown.PeriodFrom))
            return 0m;

        await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);

        // Account-type lookup: use the first account's type as representative.
        var acctType = await GetAccountTypeAsync(conn, drillDown.AcctNums[0]);
        var isBalSheet = acctType is 'B' or 'C';

        // Representative entity for the "is period a balfor period" check.
        // Using the first entity is stable and mirrors GlFilterBuilder's pick.
        var repEntity = drillDown.EntityIds[0];
        var isPeriodABalFor = await IsBalForPeriodAsync(
            conn, repEntity, drillDown.PeriodFrom);

        // Anchor periods — computed per entity, but balfor dates align across
        // entities so the representative-entity lookup is sufficient.
        var balForPd = await GetBalForAnchorAsync(
            conn, repEntity, drillDown.PeriodFrom);
        var begYrPd = DeriveBegYrPd(balForPd, drillDown.PeriodFrom);

        // Resolve the matrix.
        string drillStart;
        string drillEnd;
        string balForFilter;
        if (isBalSheet && isPeriodABalFor)
        {
            drillStart   = balForPd;
            drillEnd     = drillDown.PeriodFrom;
            balForFilter = "B";
        }
        else if (isBalSheet && !isPeriodABalFor)
        {
            drillStart   = balForPd;
            drillEnd     = balForPd;
            balForFilter = "B";
        }
        else if (!isBalSheet && isPeriodABalFor)
        {
            // Income account at fiscal-year open → no prior YTD.
            _logger.LogTrace(
                "StartingBalance — income account at balfor period, returning 0");
            return 0m;
        }
        else
        {
            drillStart   = begYrPd;
            drillEnd     = PreviousPeriod(drillDown.PeriodFrom);
            balForFilter = "N";
        }

        // Basis expansion mirrors GL detail repo: A/C selections include 'B'.
        var expandedBasis = ExpandBasis(drillDown.BasisList);

        const string sql = """
            SELECT ISNULL(SUM(s.ACTIVITY), 0)
            FROM GLSUM s
            WHERE s.ACCTNUM  IN @AcctNums
              AND s.ENTITYID IN @EntityIds
              AND s.BASIS    IN @Basis
              AND s.BALFOR   = @BalForFilter
              AND s.PERIOD BETWEEN @DrillStart AND @DrillEnd
            """;

        var parameters = new
        {
            AcctNums     = drillDown.AcctNums,
            EntityIds    = drillDown.EntityIds,
            Basis        = expandedBasis,
            BalForFilter = balForFilter,
            DrillStart   = drillStart,
            DrillEnd     = drillEnd
        };

        try
        {
            _logger.LogTrace(
                "StartingBalanceRepository — DbKey={DbKey} Type={Type} " +
                "PeriodFrom={From} IsBalForPd={IsBalFor} Range={Start}-{End} " +
                "BalFor={BF} Accts={A} Entities={E}",
                dbKey, acctType, drillDown.PeriodFrom, isPeriodABalFor,
                drillStart, drillEnd, balForFilter,
                string.Join(",", drillDown.AcctNums),
                string.Join(",", drillDown.EntityIds));

            var result = await conn.ExecuteScalarAsync<decimal>(sql, parameters);

            _logger.LogDebug(
                "StartingBalanceRepository — DbKey={DbKey} PeriodFrom={From} Result={Balance}",
                dbKey, drillDown.PeriodFrom, result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "StartingBalanceRepository.GetStartingBalanceAsync failed — " +
                "DbKey={DbKey} PeriodFrom={From}",
                dbKey, drillDown.PeriodFrom);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, StartingBalanceRow>> GetStartingBalancesForRangeAsync(
        string dbKey,
        string ledgLo,
        string ledgHi,
        IReadOnlyList<string> entityIds,
        IReadOnlyList<string> basisList,
        string balForPd,
        string begYrPd,
        string periodFrom)
    {
        if (string.IsNullOrEmpty(periodFrom) || entityIds.Count == 0)
            return new Dictionary<string, StartingBalanceRow>();

        await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);

        // Representative entity for the "is PeriodFrom a BALFOR period" check.
        // Using the first entity is stable across a drill. BALFOR periods align
        // across entities in a consolidated report so this single lookup suffices.
        var repEntity = entityIds[0];
        var isPeriodABalFor = await IsBalForPeriodAsync(conn, repEntity, periodFrom);

        var prevPeriod = PreviousPeriod(periodFrom);

        // Single SQL encoding the four-case matrix via CASE expressions on
        // GACC.TYPE. Period range and BALFOR filter both branch on type and
        // on whether PeriodFrom is itself a BALFOR='B' period:
        //   B/C + periodFrom IS balfor  → BALFOR='B' row at PeriodFrom
        //   B/C + periodFrom NOT balfor → BALFOR='B' row at prior BalForPd
        //   I   + periodFrom IS balfor  → no rows (sentinel drill-end '000000')
        //   I   + periodFrom NOT balfor → BALFOR='N' rows from BegYrPd..PrevPeriod
        const string sql = """
            SELECT
                RTRIM(g.ACCTNUM)  AS AcctNum,
                RTRIM(g.ACCTNAME) AS AcctName,
                RTRIM(g.TYPE)     AS Type,
                SUM(s.ACTIVITY)   AS Amount
            FROM GACC g
            JOIN GLSUM s ON s.ACCTNUM = g.ACCTNUM
            WHERE g.ACCTNUM  >= @LedgLo
              AND g.ACCTNUM  <  @LedgHi
              AND s.ENTITYID IN @EntityIds
              AND s.BASIS    IN @BasisList
              AND s.BALFOR   =
                  CASE WHEN g.TYPE IN ('B','C') THEN 'B' ELSE 'N' END
              AND s.PERIOD BETWEEN
                  CASE WHEN g.TYPE IN ('B','C') THEN @BalForPd ELSE @BegYrPd END
                  AND
                  CASE
                      WHEN g.TYPE IN ('B','C') AND @IsPeriodABalFor = 1 THEN @PeriodFrom
                      WHEN g.TYPE IN ('B','C') AND @IsPeriodABalFor = 0 THEN @BalForPd
                      WHEN g.TYPE NOT IN ('B','C') AND @IsPeriodABalFor = 1 THEN '000000'
                      WHEN g.TYPE NOT IN ('B','C') AND @IsPeriodABalFor = 0 THEN @PrevPeriod
                  END
            GROUP BY g.ACCTNUM, g.ACCTNAME, g.TYPE
            """;

        var parameters = new
        {
            LedgLo          = ledgLo,
            LedgHi          = ledgHi,
            EntityIds       = entityIds,
            BasisList       = basisList,
            BalForPd        = balForPd,
            BegYrPd         = begYrPd,
            PeriodFrom      = periodFrom,
            PrevPeriod      = prevPeriod,
            IsPeriodABalFor = isPeriodABalFor ? 1 : 0
        };

        try
        {
            _logger.LogTrace(
                "StartingBalanceRepository.GetStartingBalancesForRangeAsync — " +
                "DbKey={DbKey} PeriodFrom={From} IsBalForPd={Is} " +
                "BalForPd={BF} BegYrPd={BY} Entities=[{E}]",
                dbKey, periodFrom, isPeriodABalFor,
                balForPd, begYrPd, string.Join(",", entityIds));

            var rows = await conn.QueryAsync<(string AcctNum, string AcctName, string Type, decimal Amount)>(
                sql, parameters);

            var result = rows.ToDictionary(
                r => r.AcctNum,
                r => new StartingBalanceRow(r.AcctName, r.Type, r.Amount));

            _logger.LogDebug(
                "StartingBalanceRepository.GetStartingBalancesForRangeAsync — {Count} accounts DbKey={DbKey}",
                result.Count, dbKey);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "StartingBalanceRepository.GetStartingBalancesForRangeAsync failed — DbKey={DbKey} PeriodFrom={From}",
                dbKey, periodFrom);
            throw;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>Returns the first char of GACC.TYPE for the given account.</summary>
    private static async Task<char> GetAccountTypeAsync(
        System.Data.Common.DbConnection conn, string acctNum)
    {
        const string sql = "SELECT TOP 1 RTRIM(TYPE) FROM GACC WHERE ACCTNUM = @AcctNum";
        var type = await conn.ExecuteScalarAsync<string>(sql, new { AcctNum = acctNum });
        return string.IsNullOrEmpty(type) ? 'I' : type[0];
    }

    /// <summary>True if the period is marked BALFOR='B' for the entity in PERIOD.</summary>
    private static async Task<bool> IsBalForPeriodAsync(
        System.Data.Common.DbConnection conn, string entityId, string period)
    {
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1 FROM PERIOD
                WHERE ENTITYID = @EntityId
                  AND PERIOD   = @Period
                  AND BALFOR   = 'B'
            ) THEN 1 ELSE 0 END AS BIT)
            """;
        return await conn.ExecuteScalarAsync<bool>(
            sql, new { EntityId = entityId, Period = period });
    }

    /// <summary>Max BALFOR='B' period on or before the given period for the entity.</summary>
    private static async Task<string> GetBalForAnchorAsync(
        System.Data.Common.DbConnection conn, string entityId, string period)
    {
        const string sql = """
            SELECT ISNULL(MAX(PERIOD), '200001')
            FROM PERIOD
            WHERE BALFOR   = 'B'
              AND PERIOD   <= @Period
              AND ENTITYID  = @EntityId
            """;
        var result = await conn.ExecuteScalarAsync<string>(
            sql, new { EntityId = entityId, Period = period });
        return result ?? "200001";
    }

    /// <summary>
    /// Fiscal-year-start period: takes the MONTH portion of balForPd and
    /// applies it to the correct calendar year relative to period.
    /// Mirrors FiscalCalendar.DeriveBegYrPd but kept local since the
    /// Services helper isn't referenced from DAL.
    /// </summary>
    private static string DeriveBegYrPd(string balForPd, string period)
    {
        var startMonth = balForPd.Substring(4, 2);
        var endMonth   = period.Substring(4, 2);
        var endYear    = int.Parse(period.Substring(0, 4));

        var useYear = string.Compare(endMonth, startMonth, StringComparison.Ordinal) >= 0
            ? endYear
            : endYear - 1;

        return $"{useYear:D4}{startMonth}";
    }

    /// <summary>Returns the previous YYYYMM period, handling year wrap.</summary>
    private static string PreviousPeriod(string yyyyMm)
    {
        var year  = int.Parse(yyyyMm.Substring(0, 4));
        var month = int.Parse(yyyyMm.Substring(4, 2));
        month -= 1;
        if (month == 0) { month = 12; year -= 1; }
        return $"{year:D4}{month:D2}";
    }

    /// <summary>
    /// MRI basis expansion: if user selected A or C, also include B.
    /// Mirrors GlDrillDownRepository.ExpandBasis so drill totals reconcile.
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
