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
///   GetGlStartingBalance encodes the BALFOR-aware range logic to return the
///     true "balance at end of (period-1)" for both B/C (snapshot + activity
///     from anchor) and I (activity from fiscal-year start) account types.
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

        // Anchor periods for this report context. We look them up relative to
        // `period` itself (the fiscal year containing period-1):
        //   BalForPd: most recent BALFOR='B' period on or before period — the
        //             start of the fiscal year for B/C accounts.
        //   BegYrPd:  month portion of BalForPd applied to the right calendar
        //             year — the start of the fiscal year for I accounts.
        var repEntity = entityIds[0];
        var balForPd  = await GetBalForAnchorAsync(conn, repEntity, period);
        var begYrPd   = DeriveBegYrPd(balForPd, period);
        var prevPeriod = PreviousPeriod(period);

        // Basis expansion mirrors GlDrillDownRepository — A/C include 'B'.
        var expandedBasis = ExpandBasis(basisList);

        // Semantic: balance at end of (period - 1).
        //   B/C accounts: BALFOR='B' snapshot at BalForPd + BALFOR='N' activity
        //                 from BalForPd through (period - 1).
        //   I   accounts: BALFOR='N' activity from BegYrPd through (period - 1).
        //                 Income resets each fiscal year — no snapshot carried.
        //
        // BALFOR='B' snapshots on fiscal-year opens embed the prior year's
        // closing balance. Including both 'B' AND 'N' rows for B/C is what
        // makes the sum correct — the 'B' row captures everything up to the
        // start of the current year, 'N' rows layer on the current year's
        // activity. Ignoring BALFOR (as the old TrialBalanceRepository did)
        // double-counted those snapshot rows.
        //
        // Edge case: period == BalForPd. PrevPeriod < BalForPd, so the
        // 'N'-activity clause is suppressed (via @PrevPeriod >= @BalForPd).
        // The 'B' snapshot clause still fires, returning just the snapshot
        // which IS the balance at end of (BalForPd - 1).
        //
        // Edge case: period == BegYrPd (I account). PrevPeriod < BegYrPd,
        // so the 'N' range is empty → account returns 0 (income reset).
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
              AND (
                  -- B/C accounts: include the BALFOR='B' snapshot row at
                  -- BalForPd PLUS BALFOR='N' activity rows from BalForPd
                  -- through prev. The snapshot captures everything up to the
                  -- start of the current fiscal year; the 'N' rows layer on
                  -- the current year's activity to date.
                  --
                  -- Edge case: period == BalForPd. Then PrevPeriod < BalForPd
                  -- and we want balance at end of (BalForPd - 1), which IS
                  -- the snapshot alone. The N activity row at BalForPd must
                  -- NOT be included — it represents the new fiscal year's
                  -- opening-month activity, not the prior year's close.
                  (g.TYPE IN ('B','C') AND (
                      -- The snapshot: always included for B/C.
                      (s.BALFOR = 'B' AND s.PERIOD = @BalForPd)
                      OR
                      -- Current-year activity through prev period, but only
                      -- when prev period is on or after the anchor (otherwise
                      -- we'd be in the balance-at-end-of-prior-year case).
                      (s.BALFOR = 'N'
                       AND s.PERIOD BETWEEN @BalForPd AND @PrevPeriod
                       AND @PrevPeriod >= @BalForPd)
                  ))
                  OR
                  -- I accounts: BALFOR='N' activity only, from fiscal-year
                  -- start through prev. Range empty if period == BegYrPd →
                  -- returns 0 for that account (income accounts reset yearly).
                  (g.TYPE NOT IN ('B','C')
                   AND s.BALFOR = 'N'
                   AND s.PERIOD BETWEEN @BegYrPd AND @PrevPeriod)
              )
            GROUP BY g.ACCTNUM, g.ACCTNAME, g.TYPE
            """;

        var parameters = new
        {
            LedgLo     = ledgLo,
            LedgHi     = ledgHi,
            EntityIds  = entityIds,
            Basis      = expandedBasis,
            BalForPd   = balForPd,
            BegYrPd    = begYrPd,
            PrevPeriod = prevPeriod
        };

        try
        {
            _logger.LogTrace(
                "GlDataRepository.GetGlStartingBalanceAsync — DbKey={DbKey} " +
                "Period={Period} PrevPeriod={PrevPeriod} BalForPd={BF} BegYrPd={BY} " +
                "Entities=[{E}]",
                dbKey, period, prevPeriod, balForPd, begYrPd,
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
