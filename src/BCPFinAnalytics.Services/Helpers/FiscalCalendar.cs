namespace BCPFinAnalytics.Services.Helpers;

/// <summary>
/// Pure C# fiscal calendar helpers.
/// No database calls — all inputs come from DB queries made by the caller.
///
/// MRI period format: YYYYMM (6-char string, e.g. "202504")
/// User input format: MM/YYYY — converted to YYYYMM before passing here.
///
/// KEY CONCEPTS:
///
/// BALFORPD — The beginning-of-year anchor period for balance sheet accounts.
///   Sourced from: SELECT ISNULL(MAX(PERIOD), '200001') FROM PERIOD
///                 WHERE BALFOR='B' AND PERIOD &lt;= @EndPeriod AND ENTITYID = @EntityId
///   '200001' is the sentinel for brand-new entities with no calendar yet.
///
/// BEGYRPD — The first period of the current fiscal year for income accounts.
///   Derived from BALFORPD and EndPeriod using DeriveBegYrPd().
///   This handles both calendar-year (Jan–Dec) and fiscal-year entities (e.g. Jul–Jun).
///
/// PERIOD RANGE by account type (GACC.TYPE):
///   'B' or 'C' (Balance Sheet / Cash) → PERIOD BETWEEN @BALFORPD AND @EndPeriod
///   'I' (Income Statement)            → PERIOD BETWEEN @BEGYRPD AND @EndPeriod
/// </summary>
public static class FiscalCalendar
{
    /// <summary>Sentinel period used when an entity has no B-row in PERIOD table yet.</summary>
    public const string NewEntitySentinel = "200001";

    /// <summary>
    /// Converts a user-entered MM/YYYY period string to MRI YYYYMM format.
    /// e.g. "04/2025" → "202504"
    /// </summary>
    public static string ToMriPeriod(string mmYyyy)
    {
        if (string.IsNullOrWhiteSpace(mmYyyy) || mmYyyy.Length != 7 || mmYyyy[2] != '/')
            throw new ArgumentException(
                $"Period must be in MM/YYYY format. Received: '{mmYyyy}'", nameof(mmYyyy));

        var mm = mmYyyy.Substring(0, 2);
        var yyyy = mmYyyy.Substring(3, 4);
        return $"{yyyy}{mm}";
    }

    /// <summary>
    /// Converts an MRI YYYYMM period string to user-display MM/YYYY format.
    /// e.g. "202504" → "04/2025"
    /// </summary>
    public static string ToDisplayPeriod(string yyyyMm)
    {
        if (string.IsNullOrWhiteSpace(yyyyMm) || yyyyMm.Length != 6)
            throw new ArgumentException(
                $"Period must be in YYYYMM format. Received: '{yyyyMm}'", nameof(yyyyMm));

        return $"{yyyyMm.Substring(4, 2)}/{yyyyMm.Substring(0, 4)}";
    }

    /// <summary>
    /// Derives BEGYRPD — the first period of the current fiscal year for income accounts.
    ///
    /// FORMULA (fiscal-year-aware):
    ///   startMonth = last 2 chars of balForPd   (month the fiscal year opened)
    ///   endMonth   = last 2 chars of endPeriod  (month of the report end period)
    ///
    ///   if endMonth >= startMonth:
    ///     useYear = LEFT(endPeriod, 4)           (same calendar year)
    ///   else:
    ///     useYear = LEFT(endPeriod, 4) - 1       (fiscal year spans calendar years)
    ///
    ///   BEGYRPD = useYear + startMonth
    ///
    /// EXAMPLES:
    ///   Calendar year entity (Jan–Dec), BALFORPD="202501", EndPeriod="202504"
    ///   → startMonth="01", endMonth="04", 04>=01 → useYear="2025"
    ///   → BEGYRPD = "202501"  ✓
    ///
    ///   Fiscal year entity (Jul–Jun), BALFORPD="202407", EndPeriod="202503"
    ///   → startMonth="07", endMonth="03", 03&lt;07 → useYear="2024"
    ///   → BEGYRPD = "202407"  ✓
    ///
    ///   Fiscal year entity (Jul–Jun), BALFORPD="202407", EndPeriod="202509"
    ///   → startMonth="07", endMonth="09", 09>=07 → useYear="2025"
    ///   → BEGYRPD = "202507"  ✓
    /// </summary>
    /// <param name="balForPd">
    /// BALFOR anchor period in YYYYMM format.
    /// From: SELECT ISNULL(MAX(PERIOD), '200001') FROM PERIOD
    ///       WHERE BALFOR='B' AND PERIOD &lt;= @EndPeriod AND ENTITYID = @EntityId
    /// </param>
    /// <param name="endPeriod">Report end period in YYYYMM format.</param>
    /// <returns>BEGYRPD in YYYYMM format.</returns>
    public static string DeriveBegYrPd(string balForPd, string endPeriod)
    {
        if (string.IsNullOrWhiteSpace(balForPd) || balForPd.Length != 6)
            throw new ArgumentException(
                $"balForPd must be YYYYMM. Received: '{balForPd}'", nameof(balForPd));

        if (string.IsNullOrWhiteSpace(endPeriod) || endPeriod.Length != 6)
            throw new ArgumentException(
                $"endPeriod must be YYYYMM. Received: '{endPeriod}'", nameof(endPeriod));

        var startMonth = balForPd.Substring(4, 2);   // MM from BALFORPD
        var endMonth   = endPeriod.Substring(4, 2);  // MM from EndPeriod
        var endYear    = int.Parse(endPeriod.Substring(0, 4));

        int useYear;
        if (string.Compare(endMonth, startMonth, StringComparison.Ordinal) >= 0)
        {
            // EndPeriod month is on or after fiscal year start month
            // → fiscal year started in the same calendar year as EndPeriod
            useYear = endYear;
        }
        else
        {
            // EndPeriod month is before fiscal year start month
            // → fiscal year started in the previous calendar year
            useYear = endYear - 1;
        }

        return $"{useYear:D4}{startMonth}";
    }

    /// <summary>
    /// Compares two YYYYMM period strings chronologically.
    /// Returns negative if a &lt; b, zero if equal, positive if a &gt; b.
    /// </summary>
    public static int ComparePeriods(string a, string b)
        => string.Compare(a, b, StringComparison.Ordinal);

    /// <summary>
    /// Returns true if period a is on or before period b.
    /// Both must be in YYYYMM format.
    /// </summary>
    public static bool IsOnOrBefore(string a, string b)
        => ComparePeriods(a, b) <= 0;

    /// <summary>
    /// Returns the period immediately following the given period.
    /// Handles fiscal year rollover (month 12 → month 01 of next year).
    /// e.g. "202512" → "202601", "202504" → "202505"
    /// </summary>
    public static string NextPeriod(string yyyyMm)
    {
        var year  = int.Parse(yyyyMm.Substring(0, 4));
        var month = int.Parse(yyyyMm.Substring(4, 2));

        month++;
        if (month > 12) { month = 1; year++; }

        return $"{year:D4}{month:D2}";
    }

    /// <summary>
    /// Returns the period immediately preceding the given period.
    /// Handles fiscal year rollover (month 01 → month 12 of previous year).
    /// e.g. "202501" → "202412", "202504" → "202503"
    /// </summary>
    public static string PreviousPeriod(string yyyyMm)
    {
        var year  = int.Parse(yyyyMm.Substring(0, 4));
        var month = int.Parse(yyyyMm.Substring(4, 2));

        month--;
        if (month < 1) { month = 12; year--; }

        return $"{year:D4}{month:D2}";
    }
}
