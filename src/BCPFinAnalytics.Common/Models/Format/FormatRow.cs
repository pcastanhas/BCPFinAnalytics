using BCPFinAnalytics.Common.Enums;

namespace BCPFinAnalytics.Common.Models.Format;

/// <summary>
/// A single fully-parsed and resolved row from MRIGLRW.
/// Produced by FormatLoader after parsing LINEDEF and resolving all @GRP references.
///
/// All rows are stored in FormatDefinition.Rows ordered by SORTORD.
/// BL rows are included — the renderer emits a blank line for them.
///
/// Field applicability by RowType:
///
///   RowType    | Label | Options | Ranges | SubtotId | SubtotRefs
///   -----------|-------|---------|--------|----------|----------
///   Blank      |       |         |        |          |
///   Title      |  ✓    |  ✓(S,P) |        |          |
///   Range      |       |  ✓      |  ✓     |          |
///   Summary    |  ✓    |  ✓      |  ✓     |          |
///   Subtotal   |  ✓    |  ✓      |        |  ✓       |
///   GrandTotal |  ✓    |  ✓      |        |          |  ✓
/// </summary>
public sealed class FormatRow
{
    /// <summary>Row type — drives all parsing, querying, and rendering decisions.</summary>
    public FormatRowType RowType { get; init; }

    /// <summary>Display order within the format — from MRIGLRW.SORTORD.</summary>
    public int SortOrd { get; init; }

    /// <summary>
    /// Subtotal accumulator ID — from MRIGLRW.SUBTOTID.
    /// Meaningful only on Subtotal rows: assigns the numeric ID that TO rows reference.
    /// 0 on all other row types.
    /// </summary>
    public int SubtotId { get; init; }

    /// <summary>
    /// Display label for this row.
    /// Sources:
    ///   Title, Summary, Subtotal, GrandTotal → from LINEDEF ~T= token
    ///   Range → EMPTY — label comes from GACC.ACCTNAME at query time
    ///   Blank → EMPTY
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Sign convention from MRIGLRW.DEBCRED column.
    /// 'D' = debit normal, 'C' = credit normal, null = not set.
    /// Interacts with O= FlipSign and ReverseSign flags.
    /// </summary>
    public string? DebCred { get; init; }

    /// <summary>
    /// Parsed options from LINEDEF ~O= flag string.
    /// FormatOptions.None when no O= token present.
    /// </summary>
    public FormatOptions Options { get; init; } = FormatOptions.None;

    /// <summary>
    /// Resolved account ranges for Range and Summary rows.
    /// All @GRP* references have been expanded to concrete BEGACCT/ENDACCT pairs via GARR.
    /// Exclusions (@EXC*) are flagged with IsExclusion=true.
    /// Empty for Blank, Title, Subtotal, and GrandTotal rows.
    /// </summary>
    public IReadOnlyList<ResolvedAccountRange> Ranges { get; init; }
        = Array.Empty<ResolvedAccountRange>();

    /// <summary>
    /// Subtotal ID ranges for GrandTotal rows only.
    /// Parsed from LINEDEF ~R= numeric references e.g. "1-42, 53-55" → [(1,42),(53,55)].
    /// Each tuple is (Lo, Hi) inclusive — sum all SU rows whose SubtotId is in any range.
    /// Empty for all other row types.
    /// </summary>
    public IReadOnlyList<(int Lo, int Hi)> SubtotRefs { get; init; }
        = Array.Empty<(int, int)>();
}
