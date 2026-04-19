using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.DAL.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL.Repositories;

/// <summary>
/// The canonical GL data layer. Two SQL queries total, both shared by every
/// GL-backed report column in every strategy.
///
/// BALFOR correctness:
///   GetGlStartingBalance encodes the four-case matrix ported from MRI's
///     canonical starting-balance SQL (see <see cref="StartingBalanceRepository"/>
///     which this implementation largely supersedes).
///   GetGlActivity filters BALFOR='N' so year-opening snapshot rows never
///     double-count alongside January activity rows.
/// </summary>
public class GlDataRepository : IGlDataRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<GlDataRepository> _logger;

    public GlDataRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<GlDataRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, AccountAmount>> GetGlStartingBalanceAsync(
        string dbKey,
        string period,
        string ledgLo,
        string ledgHi,
        IReadOnlyList<string> entityIds,
        IReadOnlyList<string> basisList)
    {
        if (string.IsNullOrEmpty(period) || entityIds.Count == 0 || basisList.Count == 0)
            return new Dictionary<string, AccountAmount>();

        await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);

        // Representative entity for the "is period a BALFOR period" check.
        // Using the first entity is stable; BALFOR periods align across
        // entities in a consolidated report so this single lookup suffices.
        var repEntity = entityIds[0];
        var isPeriodABalFor = await IsBalForPeriodAsync(conn, repEntity, period);

        // Anchor periods for the row range.
        var balForPd = await GetBalForAnchorAsync(conn, repEntity, period);
        var begYrPd  = DeriveBegYrPd(balForPd, period);
        var prevPeriod = PreviousPeriod(period);

        // Basis expansion mirrors GlDrillDownRepository: if the caller passed
        // A or C, also include B rows. Keeps totals reconcilable with drills.
        var expandedBasis = ExpandBasis(basisList);

        // Single SQL encoding the four-case (account-type × is-balfor-period)
        // matrix via CASE expressions on GACC.TYPE:
        //   B/C account + period IS balfor  → BALFOR='B' snapshot at period
        //   B/C account + period NOT balfor → BALFOR='B' snapshot at prior BalForPd
        //   I  account  + period IS balfor  → sentinel '000000' (no rows)
        //   I  account  + period NOT balfor → BALFOR='N' activity BegYrPd..prev
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
              AND s.BASIS    IN @Basis
              AND s.BALFOR   =
                  CASE WHEN g.TYPE IN ('B','C') THEN 'B' ELSE 'N' END
              AND s.PERIOD BETWEEN
                  CASE WHEN g.TYPE IN ('B','C') THEN @BalForPd ELSE @BegYrPd END
                  AND
                  CASE
                      WHEN g.TYPE IN ('B','C') AND @IsPeriodABalFor = 1 THEN @Period
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
            Basis           = expandedBasis,
            BalForPd        = balForPd,
            BegYrPd         = begYrPd,
            Period          = period,
            PrevPeriod      = prevPeriod,
            IsPeriodABalFor = isPeriodABalFor ? 1 : 0
        };

        try
        {
            _logger.LogTrace(
                "GlDataRepository.GetGlStartingBalanceAsync — DbKey={DbKey} " +
                "Period={Period} IsBalForPd={Is} BalForPd={BF} BegYrPd={BY} " +
                "Entities=[{E}]",
                dbKey, period, isPeriodABalFor, balForPd, begYrPd,
                string.Join(",", entityIds));

            var rows = await conn.QueryAsync<(string AcctNum, string AcctName, string Type, decimal Amount)>(
                sql, parameters);

            var result = rows.ToDictionary(
                r => r.AcctNum,
                r => new AccountAmount(r.AcctNum, r.AcctName, r.Type, r.Amount));

            _logger.LogDebug(
                "GlDataRepository.GetGlStartingBalanceAsync — {Count} accounts DbKey={DbKey}",
                result.Count, dbKey);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GlDataRepository.GetGlStartingBalanceAsync failed — DbKey={DbKey} Period={Period}",
                dbKey, period);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, AccountAmount>> GetGlActivityAsync(
        string dbKey,
        string startPeriod,
        string endPeriod,
        string ledgLo,
        string ledgHi,
        IReadOnlyList<string> entityIds,
        IReadOnlyList<string> basisList)
    {
        if (string.IsNullOrEmpty(startPeriod) || string.IsNullOrEmpty(endPeriod)
            || entityIds.Count == 0 || basisList.Count == 0)
            return new Dictionary<string, AccountAmount>();

        var expandedBasis = ExpandBasis(basisList);

        // BALFOR='N' excludes fiscal-year-open snapshot rows so January
        // never double-counts.
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
              AND s.BASIS    IN @Basis
              AND s.BALFOR   =  'N'
              AND s.PERIOD BETWEEN @StartPeriod AND @EndPeriod
            GROUP BY g.ACCTNUM, g.ACCTNAME, g.TYPE
            """;

        var parameters = new
        {
            LedgLo      = ledgLo,
            LedgHi      = ledgHi,
            EntityIds   = entityIds,
            Basis       = expandedBasis,
            StartPeriod = startPeriod,
            EndPeriod   = endPeriod
        };

        try
        {
            _logger.LogTrace(
                "GlDataRepository.GetGlActivityAsync — DbKey={DbKey} " +
                "Period={Start}-{End} Entities=[{E}]",
                dbKey, startPeriod, endPeriod, string.Join(",", entityIds));

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows = await conn.QueryAsync<(string AcctNum, string AcctName, string Type, decimal Amount)>(
                sql, parameters);

            var result = rows.ToDictionary(
                r => r.AcctNum,
                r => new AccountAmount(r.AcctNum, r.AcctName, r.Type, r.Amount));

            _logger.LogDebug(
                "GlDataRepository.GetGlActivityAsync — {Count} accounts DbKey={DbKey}",
                result.Count, dbKey);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GlDataRepository.GetGlActivityAsync failed — DbKey={DbKey} Period={S}-{E}",
                dbKey, startPeriod, endPeriod);
            throw;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────
    // Intentionally not shared with StartingBalanceRepository — that repo
    // is still used by the GL drill-down dialog for per-drill starting
    // balances. Both will be consolidated once all 5 strategies are
    // migrated onto this new surface and the old repo is deleted.

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

    private static string DeriveBegYrPd(string balForPd, string period)
    {
        var startMonth = balForPd.Substring(4, 2);
        var endMonth   = period.Substring(4, 2);
        var endYear    = int.Parse(period.Substring(0, 4));
        var useYear    = string.Compare(endMonth, startMonth, StringComparison.Ordinal) >= 0
            ? endYear
            : endYear - 1;
        return $"{useYear:D4}{startMonth}";
    }

    private static string PreviousPeriod(string yyyyMm)
    {
        var year  = int.Parse(yyyyMm.Substring(0, 4));
        var month = int.Parse(yyyyMm.Substring(4, 2));
        month -= 1;
        if (month == 0) { month = 12; year -= 1; }
        return $"{year:D4}{month:D2}";
    }

    /// <summary>
    /// MRI basis expansion: if user selected A or C, also include B. Mirrors
    /// GlDrillDownRepository.ExpandBasis so drill totals reconcile with report
    /// totals exactly.
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
