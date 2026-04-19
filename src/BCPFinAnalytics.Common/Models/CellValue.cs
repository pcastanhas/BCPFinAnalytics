namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// Bundles a cell's numeric value and its optional drill-down reference.
///
/// Replaces bare decimal? in ReportRow.Cells so that the display value
/// and the drill-down context always travel together — it is impossible
/// to have one without the other.
///
/// Usage in report strategies:
///   // Drillable detail row cell:
///   row.Cells[colId] = new CellValue(123.45m, new DrillDownRef { ... });
///
///   // Non-drillable cell (total, header, computed):
///   row.Cells[colId] = new CellValue(123.45m);
///
/// Usage in renderers:
///   var cell = row.Cells[col.ColumnId];
///   // Display:
///   FormatCurrency(cell.Amount)
///   // Drill-down:
///   if (cell.IsDrillable) { ... cell.DrillDown ... }
/// </summary>
public readonly record struct CellValue
{
    /// <summary>
    /// The numeric amount for this cell.
    /// Null renders as blank (not zero).
    /// </summary>
    public decimal? Amount { get; init; }

    /// <summary>
    /// Drill-down context for this cell.
    /// Null means the cell is not drillable — no click handler rendered.
    /// </summary>
    public DrillDownRef? DrillDown { get; init; }

    /// <summary>
    /// Optional CSS class for cell-level styling (e.g. variance color coding).
    /// Rendered as a class on the td element.
    /// e.g. "variance-green", "variance-yellow", "variance-red"
    /// </summary>
    public string? CssClass { get; init; }

    /// <summary>
    /// Budget drill-down context. Set on PTD/YTD Budget cells in IS report.
    /// Opens BudgetDetailDialog instead of GLDrillDownDialog.
    /// </summary>
    public BudgetDrillDownRef? BudgetDrillDown { get; init; }

    /// <summary>True when this cell supports GL drill-down.</summary>
    public bool IsDrillable => DrillDown is not null;

    /// <summary>True when this cell supports budget drill-down.</summary>
    public bool IsBudgetDrillable => BudgetDrillDown is not null;

    /// <summary>Creates a drillable cell with both amount and drill-down context.</summary>
    public CellValue(decimal? amount, DrillDownRef drillDown)
    {
        Amount   = amount;
        DrillDown = drillDown;
    }

    /// <summary>Creates a non-drillable cell — amount only, no drill-down.</summary>
    public CellValue(decimal? amount)
    {
        Amount    = amount;
        DrillDown = null;
    }

    /// <summary>Convenience — a blank, non-drillable cell.</summary>
    public static CellValue Empty => new(null);
}
