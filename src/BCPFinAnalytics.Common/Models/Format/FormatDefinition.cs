namespace BCPFinAnalytics.Common.Models.Format;

/// <summary>
/// The complete parsed and resolved format definition for a single GUSR format.
/// Produced by IFormatLoader — immutable, fully resolved, ready for report execution.
///
/// All @GRP* account group references in MRIGLRW have been expanded to concrete
/// BEGACCT/ENDACCT pairs from GARR. No further database calls are needed to
/// interpret this object.
///
/// Loaded fresh on every report run (not cached) because users may update
/// format definitions in another MRI tab between runs.
/// </summary>
public sealed class FormatDefinition
{
    /// <summary>Format identifier — GUSR.CODE. e.g. "SOPDET"</summary>
    public string FormatId { get; init; } = string.Empty;

    /// <summary>Format display name — GUSR.NAME.</summary>
    public string FormatName { get; init; } = string.Empty;

    /// <summary>Financial type — GUSR.FINANTYP. 'B' = Balance Sheet, 'I' = Income/All.</summary>
    public string FinanTyp { get; init; } = string.Empty;

    /// <summary>
    /// GL ledger code this format belongs to — GUSR.LEDGCODE.
    /// Used to filter GARR when resolving @GRP* references.
    /// Must match the user's selected ledger for the report to be valid.
    /// </summary>
    public string LedgCode { get; init; } = string.Empty;

    /// <summary>
    /// All rows in display order (ascending SORTORD).
    /// BL (Blank) rows are included — the renderer emits blank lines for them.
    /// No rows are stripped during loading.
    /// </summary>
    public IReadOnlyList<FormatRow> Rows { get; init; }
        = Array.Empty<FormatRow>();

    /// <summary>Convenience — only the Range rows (RA type).</summary>
    public IEnumerable<FormatRow> RangeRows =>
        Rows.Where(r => r.RowType == Enums.FormatRowType.Range);

    /// <summary>Convenience — only the Summary rows (SM type).</summary>
    public IEnumerable<FormatRow> SummaryRows =>
        Rows.Where(r => r.RowType == Enums.FormatRowType.Summary);

    /// <summary>Convenience — only the Subtotal rows (SU type).</summary>
    public IEnumerable<FormatRow> SubtotalRows =>
        Rows.Where(r => r.RowType == Enums.FormatRowType.Subtotal);

    /// <summary>
    /// All data-bearing rows (Range + Summary) that will produce GL queries.
    /// In SORTORD order.
    /// </summary>
    public IEnumerable<FormatRow> DataRows =>
        Rows.Where(r => r.RowType is Enums.FormatRowType.Range
                                  or Enums.FormatRowType.Summary);
}
