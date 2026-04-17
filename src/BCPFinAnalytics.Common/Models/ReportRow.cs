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
    /// Cell values keyed by ColumnId (matching ReportColumn.ColumnId).
    /// Each CellValue bundles the numeric Amount with an optional DrillDownRef.
    ///
    /// Non-drillable cells (headers, totals, computed rows):
    ///   row.Cells[colId] = new CellValue(amount);
    ///
    /// Drillable detail cells:
    ///   row.Cells[colId] = new CellValue(amount, new DrillDownRef { ... });
    ///
    /// Missing key or CellValue.Empty renders as blank.
    /// </summary>
    public Dictionary<string, CellValue> Cells { get; set; } = new();

    /// <summary>
    /// Optional display override — when set, this text is shown instead of
    /// the formatted numeric value for a specific column.
    /// Key = ColumnId, Value = display text (e.g. "N/A").
    /// Takes precedence over Cells[colId].Amount.
    /// </summary>
    public Dictionary<string, string> CellOverrides { get; set; } = new();
}
