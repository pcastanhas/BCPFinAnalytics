namespace BCPFinAnalytics.Common.Models.Format;

/// <summary>
/// A single resolved account range segment from a LINEDEF ~R= value.
///
/// All range sources — inline ranges (MR400000000-MR499909999) and
/// named group references (@GRPP_SRVCST → GARR lookup) — are normalized
/// into this flat structure during format loading.
///
/// The query builder generates:
///   INCLUDE ranges → (ACCTNUM >= BegAcct AND ACCTNUM &lt; EndAcct)
///   EXCLUSION ranges → NOT (ACCTNUM >= BegAcct AND ACCTNUM &lt; EndAcct)
///
/// Note: MRI account ranges use a HALF-OPEN interval convention —
/// the end account in MRIGLRW/GARR is the last account IN the range,
/// but the WHERE clause should use &lt; on the next account or treat the
/// stored EndAcct as inclusive depending on how the data was entered.
/// Verify with actual data — common MRI convention is the EndAcct is inclusive.
/// </summary>
public sealed record ResolvedAccountRange
{
    /// <summary>
    /// Account range start — char(11), left-padded, as stored in GACC/GLSUM.
    /// e.g. "MR400000000"
    /// </summary>
    public string BegAcct { get; init; } = string.Empty;

    /// <summary>
    /// Account range end — char(11), inclusive.
    /// e.g. "MR499909999"
    /// </summary>
    public string EndAcct { get; init; } = string.Empty;

    /// <summary>
    /// When true this range should be EXCLUDED from the query (sourced from @EXC prefix).
    /// When false this range should be INCLUDED.
    /// </summary>
    public bool IsExclusion { get; init; }

    /// <summary>
    /// The source that produced this range — for logging/debugging.
    /// e.g. "inline", "@GRPP_SRVCST", "@EXC@GRPT_RETHG"
    /// </summary>
    public string SourceRef { get; init; } = string.Empty;
}
