namespace BCPFinAnalytics.Services.Helpers;

/// <summary>
/// Fully resolved set of parameters for a GL balance query (GLSUM).
/// Produced by GlFilterBuilder after all C# computations are complete.
/// Passed directly to Dapper as the parameter object for the canonical query.
///
/// All fiscal calendar math (BALFORPD, BEGYRPD) and entity/basis expansion
/// are computed before the SQL executes — the SQL itself is straightforward.
/// </summary>
public sealed class GlQueryParameters
{
    /// <summary>SARGable lower bound for ledger account range (inclusive). e.g. "MR000000000"</summary>
    public string LedgLo { get; init; } = string.Empty;

    /// <summary>SARGable upper bound for ledger account range (exclusive). e.g. "MS000000000"</summary>
    public string LedgHi { get; init; } = string.Empty;

    /// <summary>
    /// BALFOR anchor period — start of accumulation for balance sheet accounts (TYPE='B','C').
    /// From: SELECT ISNULL(MAX(PERIOD),'200001') FROM PERIOD
    ///       WHERE BALFOR='B' AND PERIOD &lt;= EndPeriod AND ENTITYID = RepresentativeEntity
    /// </summary>
    public string BalForPd { get; init; } = string.Empty;

    /// <summary>
    /// Beginning of current fiscal year — start of accumulation for income accounts (TYPE='I').
    /// Derived via FiscalCalendar.DeriveBegYrPd(BalForPd, EndPeriod).
    /// </summary>
    public string BegYrPd { get; init; } = string.Empty;

    /// <summary>Report end period in YYYYMM format.</summary>
    public string EndPeriod { get; init; } = string.Empty;

    /// <summary>
    /// Effective entity ID list — already resolved from SelectionMode by EntitySelectionResolver.
    /// Used as IN @EntityIds in the WHERE clause.
    /// </summary>
    public IReadOnlyList<string> EntityIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Expanded basis list — user selection with 'B' added if 'A' or 'C' is present.
    /// Used as IN @BasisList in the WHERE clause.
    /// </summary>
    public IReadOnlyList<string> BasisList { get; init; } = Array.Empty<string>();
}
