namespace BCPFinAnalytics.Common.Enums;

/// <summary>
/// Maps directly to the TYPE column in MRIGLRW.
/// Drives all parsing, querying, and rendering decisions for each format row.
/// </summary>
public enum FormatRowType
{
    /// <summary>
    /// BL — Blank line. Output one blank line in the report.
    /// No further processing. LINEDEF is ignored entirely regardless of content.
    /// </summary>
    Blank = 0,

    /// <summary>
    /// TI — Title / section header. Label-only row, no data cells.
    /// Label sourced from LINEDEF ~T= token.
    /// O=S means suppress this section header if the section has no activity.
    /// </summary>
    Title = 1,

    /// <summary>
    /// RA — Range. Explodes into one Detail row per matching ACCTNUM in GACC.
    /// Label for each row comes from GACC.ACCTNAME — NOT from LINEDEF ~T=.
    /// ~T= on RA rows is used for report design only and must be ignored.
    /// Account ranges sourced from LINEDEF ~R= (inline ranges or @GRP* group refs).
    /// </summary>
    Range = 2,

    /// <summary>
    /// SM — Summary. Produces exactly one Detail row with all matching accounts summed.
    /// Label comes from LINEDEF ~T=.
    /// Account ranges sourced from LINEDEF ~R= (inline ranges or @GRP* group refs).
    /// </summary>
    Summary = 3,

    /// <summary>
    /// SU — Subtotal. Accumulates all RA/SM rows since the previous SU row (by SORTORD).
    /// SUBTOTID assigns a numeric ID to this subtotal for reference by TO rows.
    /// Label comes from LINEDEF ~T=.
    /// </summary>
    Subtotal = 4,

    /// <summary>
    /// TO — Grand Total. Sums a set of subtotals by SUBTOTID number.
    /// LINEDEF ~R= contains numeric SUBTOTID references (e.g. "1-12" or "1-42, 53-55")
    /// — NOT account ranges. This is a completely different meaning from RA/SM ~R=.
    /// Label comes from LINEDEF ~T=.
    /// </summary>
    GrandTotal = 5
}
