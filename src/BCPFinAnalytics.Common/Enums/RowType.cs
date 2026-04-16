namespace BCPFinAnalytics.Common.Enums;

/// <summary>
/// Defines the display type of a row in a financial report.
/// Used by the screen renderer, Excel renderer, and PDF renderer
/// to apply the correct formatting to each row.
/// </summary>
public enum RowType
{
    /// <summary>Top-level section header — e.g. "REVENUE". Bold, colored, no data cells.</summary>
    SectionHeader = 0,

    /// <summary>Sub-section header — e.g. "RESIDENTIAL RENT INCOME". Bold, no data cells.</summary>
    SubHeader = 1,

    /// <summary>Individual account detail line. Normal weight, indented.</summary>
    Detail = 2,

    /// <summary>Subtotal row — e.g. "TOTAL RESIDENTIAL RENT INCOME". Bold, top border.</summary>
    Total = 3,

    /// <summary>Grand total row. Bold, double border, highlighted background.</summary>
    GrandTotal = 4
}
