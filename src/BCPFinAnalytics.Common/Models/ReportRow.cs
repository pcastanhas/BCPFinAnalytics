using BCPFinAnalytics.Common.Enums;

namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// Represents a single row in a financial report.
///
/// The RowType drives all formatting decisions in every renderer —
/// screen, Excel, and PDF all use RowType to determine styling.
///
/// Cells is a dictionary keyed by ColumnId (matching ReportColumn.ColumnId),
/// supporting both fixed-column reports and dynamic crosstab reports with
/// variable entity columns.
/// </summary>
public class ReportRow
{
    /// <summary>Determines how this row is styled across all renderers.</summary>
    public RowType RowType { get; set; } = RowType.Detail;

    /// <summary>Account or grouping code — e.g. "401-0005".</summary>
    public string AccountCode { get; set; } = string.Empty;

    /// <summary>Account or grouping description — e.g. "RESIDENTIAL RENT - GROSS".</summary>
    public string AccountName { get; set; } = string.Empty;

    /// <summary>
    /// Indentation level for the account name column.
    /// 0 = no indent (section headers), 1 = one level, 2 = two levels, etc.
    /// Renderers multiply this by a fixed pixel/character offset.
    /// </summary>
    public int Indent { get; set; } = 0;

    /// <summary>
    /// Numeric cell values keyed by ColumnId.
    /// Null values are rendered as blank.
    /// Use string.Empty sentinel "N/A" logic is handled by the renderer
    /// based on ColumnDataType.Percent when denominator is zero.
    /// </summary>
    public Dictionary<string, decimal?> Cells { get; set; } = new();

    /// <summary>
    /// Optional override — when set, this text is displayed instead of
    /// a formatted numeric value for a specific column.
    /// Key = ColumnId, Value = display text (e.g. "N/A").
    /// </summary>
    public Dictionary<string, string> CellOverrides { get; set; } = new();
}
