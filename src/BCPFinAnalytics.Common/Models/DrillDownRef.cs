namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// Identifies the GL data behind a specific report cell.
/// Passed to the GL Detail modal when the user clicks a drillable cell.
///
/// All fields needed to reconstruct the GLSUM/GLTRANS query are carried here
/// so the modal never needs to re-derive context from the parent report.
///
/// Null DrillDownRef on a CellValue means the cell is not drillable
/// (e.g. header rows, total rows, computed rows like UnpostedRetainedEarnings).
/// </summary>
public sealed record DrillDownRef
{
    /// <summary>The raw account number — e.g. "04010005" (before display formatting).</summary>
    public string AcctNum { get; init; } = string.Empty;

    /// <summary>Entity ID this cell belongs to.</summary>
    public string EntityId { get; init; } = string.Empty;

    /// <summary>
    /// Start period for this cell's data range in YYYYMM format.
    /// For income accounts this is BEGYRPD; for balance accounts this is BALFORPD.
    /// </summary>
    public string PeriodFrom { get; init; } = string.Empty;

    /// <summary>End period for this cell's data range in YYYYMM format.</summary>
    public string PeriodTo { get; init; } = string.Empty;

    /// <summary>Basis values used to filter GLSUM/GLTRANS for this cell.</summary>
    public IReadOnlyList<string> BasisList { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Display label shown in the modal header — e.g. "401-0005 · RESIDENTIAL RENT - GROSS".
    /// Pre-formatted so the modal doesn't need to re-derive it.
    /// </summary>
    public string DisplayLabel { get; init; } = string.Empty;
}
