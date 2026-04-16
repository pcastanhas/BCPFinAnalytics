namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// The fully self-contained, output-agnostic result of running a report.
///
/// ARCHITECTURAL RULE:
/// This object is the only thing passed between the report generation layer
/// (IReportStrategy.Execute) and the rendering layer (ExcelRenderer, PdfRenderer,
/// ScreenReportMapper). No renderer ever calls the database or services layer.
/// If a renderer needs data, that data must already be in this object.
///
/// This design enables V2 Report Packages, where multiple ReportResults
/// are assembled into a single Excel workbook with one sheet per report.
/// </summary>
public class ReportResult
{
    /// <summary>
    /// Ordered list of data columns.
    /// For fixed reports: always the same columns.
    /// For crosstab reports: built dynamically at runtime from entity selection.
    /// The first two columns (AccountCode, AccountName) are implicit and
    /// not included here — they are always rendered from ReportRow properties.
    /// </summary>
    public List<ReportColumn> Columns { get; set; } = new();

    /// <summary>
    /// Ordered list of rows. Row order matches the intended display order.
    /// Each row's RowType determines its formatting in all renderers.
    /// </summary>
    public List<ReportRow> Rows { get; set; } = new();

    /// <summary>
    /// All contextual information about this report run —
    /// title, periods, user, options snapshot, etc.
    /// </summary>
    public ReportMetadata Metadata { get; set; } = new();

    /// <summary>Convenience — true if the report has data rows to display.</summary>
    public bool HasData => Rows.Any();
}
