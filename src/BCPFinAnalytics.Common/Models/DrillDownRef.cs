namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// Identifies the GL data behind a specific report cell.
/// Passed to the GL Detail modal when the user clicks a drillable cell.
///
/// Supports both simple and consolidated reports:
///   - Single entity, single account   (simple report, one entity column)
///   - Multiple entities, single account  (consolidated column)
///   - Single entity, multiple accounts   (detail row spanning account range)
///   - Multiple entities, multiple accounts (consolidated + account range)
///
/// Only Detail rows carry a DrillDownRef. Total, subtotal, header, and
/// UnpostedRetainedEarnings rows always have DrillDown = null.
///
/// All fields needed to reconstruct the JOURNAL/GHIS UNION ALL query are
/// carried here — the modal never re-derives context from the parent report.
/// </summary>
public sealed record DrillDownRef
{
    /// <summary>
    /// One or more raw account numbers to include in the drill query.
    /// Always the trimmed ACCTNUM values (before display formatting).
    /// A Detail row that summarises a range of accounts passes all of them here.
    /// </summary>
    public IReadOnlyList<string> AcctNums { get; init; } = Array.Empty<string>();

    /// <summary>
    /// One or more entity IDs included in this cell's value.
    /// Single-entity reports pass one ID; consolidated reports pass all
    /// entities that were summed into this cell.
    /// </summary>
    public IReadOnlyList<string> EntityIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Start period for this cell's data range in YYYYMM format.
    /// For income accounts this is BEGYRPD; for balance accounts this is BALFORPD.
    /// </summary>
    public string PeriodFrom { get; init; } = string.Empty;

    /// <summary>End period for this cell's data range in YYYYMM format.</summary>
    public string PeriodTo { get; init; } = string.Empty;

    /// <summary>
    /// User-selected basis values (e.g. ["A"] or ["C"] or ["A","C"]).
    /// The GL Detail repository applies the basis expansion rule:
    ///   if A or C is in the list, B is automatically added to the query.
    /// This matches the behaviour of the provided JOURNAL/GHIS query template.
    /// </summary>
    public IReadOnlyList<string> BasisList { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Display label shown in the modal header.
    /// Pre-formatted by the report strategy so the modal needs no derivation.
    /// Examples:
    ///   "401-0005 · RESIDENTIAL RENT - GROSS"
    ///   "401-0000 · RESIDENTIAL RENT INCOME (3 accounts)"
    ///   "Consolidated · 401-0005 · RESIDENTIAL RENT - GROSS (4 entities)"
    /// </summary>
    public string DisplayLabel { get; init; } = string.Empty;
}
