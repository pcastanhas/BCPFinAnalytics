using BCPFinAnalytics.Common.Enums;

namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// Defines a single column in a report.
/// The column list on ReportResult is built at runtime by each report strategy,
/// enabling both fixed-column and dynamic crosstab reports to use the same model.
/// </summary>
public class ReportColumn
{
    /// <summary>Unique identifier for this column — used as the key in ReportRow.Cells.</summary>
    public string ColumnId { get; set; } = string.Empty;

    /// <summary>Display text shown in the column header.</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>Controls how values in this column are formatted by renderers.</summary>
    public ColumnDataType DataType { get; set; } = ColumnDataType.Currency;

    /// <summary>Optional preferred display width hint (pixels). Renderers may ignore this.</summary>
    public int? Width { get; set; }

    /// <summary>When true, the column header and values are right-aligned.</summary>
    public bool RightAlign { get; set; } = true;
}
