using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Services.Interfaces;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Rendering;

/// <summary>
/// Renders a ReportResult to an Excel workbook using ClosedXML.
///
/// STYLING RULES (consistent with screen renderer):
///   SectionHeader        — bold, light-grey background, no account code column
///   Detail               — normal weight, indented account name, account code visible
///   Total                — bold, top border, slightly darker background
///   GrandTotal           — bold, double top border, darker background
///   UnpostedRetainedEarnings — italic, blue text, no account code
///
/// COLUMN LAYOUT:
///   A: Account Code   (hidden when empty for all non-Detail rows)
///   B: Description    (indented via spaces — Excel indent property)
///   C+: Data columns  (one per ReportColumn, right-aligned, currency format)
///
/// NUMBER FORMAT:
///   Whole dollars:  #,##0_);(#,##0);"-"
///   With cents:     #,##0.00_);(#,##0.00);"-"
///   Zeros display as "-" per financial report convention
///
/// NEVER calls the database or services layer.
/// </summary>
public class ExcelRenderer : IExcelRenderer
{
    private readonly ILogger<ExcelRenderer> _logger;

    // Column indices (1-based for ClosedXML)
    private const int ColAccountCode = 1;
    private const int ColDescription = 2;
    private const int FirstDataCol   = 3;

    // Row heights
    private const double RowHeightDefault = 15;
    private const double RowHeightHeader  = 18;

    // Fonts
    private const string FontName = "Arial";
    private const int    FontSize = 10;

    // Colors
    private static readonly XLColor ColorHeaderBg    = XLColor.FromHtml("#D9E1F2"); // light blue-grey
    private static readonly XLColor ColorTotalBg     = XLColor.FromHtml("#F2F2F2"); // light grey
    private static readonly XLColor ColorGrandBg     = XLColor.FromHtml("#D6DCE4"); // medium grey
    private static readonly XLColor ColorUnpostedFg  = XLColor.FromHtml("#0070C0"); // blue
    private static readonly XLColor ColorReportTitle = XLColor.FromHtml("#1F3864"); // dark navy
    private static readonly XLColor ColorBorder      = XLColor.FromHtml("#9DC3E6"); // soft blue border

    public ExcelRenderer(ILogger<ExcelRenderer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Renders a single ReportResult to an Excel workbook byte array.
    /// </summary>
    public byte[] Render(ReportResult reportResult)
    {
        _logger.LogInformation(
            "ExcelRenderer.Render — ReportCode={Code} Rows={Rows} Columns={Cols}",
            reportResult.Metadata.ReportCode,
            reportResult.Rows.Count,
            reportResult.Columns.Count);

        using var workbook = new XLWorkbook();
        var sheetName = SanitizeSheetName(reportResult.Metadata.ReportTitle);
        var ws = workbook.Worksheets.Add(sheetName);

        var numFmt = reportResult.Metadata.WholeDollars
            ? @"#,##0_);(#,##0);""-"""
            : @"#,##0.00_);(#,##0.00);""-""";

        int currentRow = 1;
        currentRow = WriteReportHeader(ws, reportResult.Metadata, currentRow);
        currentRow = WriteColumnHeaders(ws, reportResult.Columns, currentRow);
        currentRow = WriteDataRows(ws, reportResult, numFmt, currentRow);
        WriteFooter(ws, reportResult.Metadata, currentRow + 1);

        ApplyColumnWidths(ws, reportResult.Columns);
        FreezeHeaderRows(ws, currentRow: 3); // freeze title + column header rows

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        _logger.LogInformation(
            "ExcelRenderer.Render — Complete. Bytes={Bytes}",
            stream.Length);

        return stream.ToArray();
    }

    /// <summary>
    /// Renders multiple ReportResults into a single multi-sheet workbook.
    /// One sheet per report — used for V2 Report Packages.
    /// </summary>
    public byte[] RenderPackage(IEnumerable<(string SheetName, ReportResult Result)> sheets)
    {
        var sheetList = sheets.ToList();
        _logger.LogInformation(
            "ExcelRenderer.RenderPackage — {Count} sheets", sheetList.Count);

        using var workbook = new XLWorkbook();

        foreach (var (sheetName, result) in sheetList)
        {
            var safeName = SanitizeSheetName(sheetName);
            var ws = workbook.Worksheets.Add(safeName);

            var numFmt = result.Metadata.WholeDollars
                ? @"#,##0_);(#,##0);""-"""
                : @"#,##0.00_);(#,##0.00);""-""";

            int row = 1;
            row = WriteReportHeader(ws, result.Metadata, row);
            row = WriteColumnHeaders(ws, result.Columns, row);
            row = WriteDataRows(ws, result, numFmt, row);
            WriteFooter(ws, result.Metadata, row + 1);
            ApplyColumnWidths(ws, result.Columns);
            FreezeHeaderRows(ws, currentRow: 3);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ══════════════════════════════════════════════════════════════
    //  Section writers
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Writes the report title block (rows 1–2).
    /// Returns the next available row number.
    /// </summary>
    private int WriteReportHeader(IXLWorksheet ws, ReportMetadata meta, int startRow)
    {
        int totalCols = FirstDataCol; // will expand dynamically — placeholder

        // Row 1: Report title
        var titleCell = ws.Cell(startRow, ColAccountCode);
        titleCell.Value = meta.ReportTitle;
        titleCell.Style.Font.Bold      = true;
        titleCell.Style.Font.FontSize  = 14;
        titleCell.Style.Font.FontName  = FontName;
        titleCell.Style.Font.FontColor = ColorReportTitle;
        ws.Row(startRow).Height = 22;
        startRow++;

        // Row 2: Entity | Period | Run info
        var periodText = string.IsNullOrEmpty(meta.StartPeriod)
            ? $"Period: {meta.EndPeriod}"
            : $"Period: {meta.StartPeriod} – {meta.EndPeriod}";

        var subCell = ws.Cell(startRow, ColAccountCode);
        subCell.Value = $"{meta.EntityName}     {periodText}     Run: {meta.RunDate:MM/dd/yyyy HH:mm}     DB: {meta.DbKey}     User: {meta.RunByUserId}";
        subCell.Style.Font.FontSize  = 9;
        subCell.Style.Font.FontName  = FontName;
        subCell.Style.Font.FontColor = XLColor.Gray;
        ws.Row(startRow).Height = 14;
        startRow++;

        // Blank separator row
        ws.Row(startRow).Height = 6;
        startRow++;

        return startRow;
    }

    /// <summary>
    /// Writes the column header row.
    /// Returns the next available row number.
    /// </summary>
    private int WriteColumnHeaders(
        IXLWorksheet ws, List<ReportColumn> columns, int startRow)
    {
        // Account Code header
        var codeCell = ws.Cell(startRow, ColAccountCode);
        codeCell.Value = "Account #";
        StyleColumnHeader(codeCell);

        // Description header
        var descCell = ws.Cell(startRow, ColDescription);
        descCell.Value = "Description";
        StyleColumnHeader(descCell);

        // Data column headers
        for (int i = 0; i < columns.Count; i++)
        {
            var col  = columns[i];
            var cell = ws.Cell(startRow, FirstDataCol + i);
            cell.Value = col.Header;
            StyleColumnHeader(cell);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            cell.Style.Alignment.WrapText   = true;
        }

        ws.Row(startRow).Height = RowHeightHeader;

        // Bottom border on header row
        var headerRange = ws.Range(startRow, ColAccountCode, startRow, FirstDataCol + columns.Count - 1);
        headerRange.Style.Border.BottomBorder      = XLBorderStyleValues.Medium;
        headerRange.Style.Border.BottomBorderColor = ColorBorder;

        return startRow + 1;
    }

    /// <summary>
    /// Writes all data rows from the ReportResult.
    /// Returns the next available row number after the last data row.
    /// </summary>
    private int WriteDataRows(
        IXLWorksheet ws, ReportResult report, string numFmt, int startRow)
    {
        int row = startRow;

        foreach (var reportRow in report.Rows)
        {
            switch (reportRow.RowType)
            {
                case RowType.SectionHeader:
                    row = WriteSectionHeaderRow(ws, reportRow, report.Columns, row);
                    break;

                case RowType.Detail:
                    row = WriteDetailRow(ws, reportRow, report.Columns, numFmt, row);
                    break;

                case RowType.Total:
                    row = WriteTotalRow(ws, reportRow, report.Columns, numFmt, row);
                    break;

                case RowType.GrandTotal:
                    row = WriteGrandTotalRow(ws, reportRow, report.Columns, numFmt, row);
                    break;

                case RowType.UnpostedRetainedEarnings:
                    row = WriteUnpostedRERow(ws, reportRow, report.Columns, numFmt, row);
                    break;
            }
        }

        return row;
    }

    // ══════════════════════════════════════════════════════════════
    //  Row type writers
    // ══════════════════════════════════════════════════════════════

    private int WriteSectionHeaderRow(
        IXLWorksheet ws, ReportRow reportRow,
        List<ReportColumn> columns, int row)
    {
        // Blank row for visual spacing
        if (string.IsNullOrWhiteSpace(reportRow.AccountName))
        {
            ws.Row(row).Height = 8;
            return row + 1;
        }

        // Merge account code + description into one label cell
        var cell = ws.Cell(row, ColAccountCode);
        cell.Value = reportRow.AccountName.ToUpper();
        cell.Style.Font.Bold        = true;
        cell.Style.Font.FontSize    = FontSize;
        cell.Style.Font.FontName    = FontName;
        cell.Style.Fill.BackgroundColor = ColorHeaderBg;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        // Shade the entire row
        var fullRange = ws.Range(row, ColAccountCode, row, FirstDataCol + columns.Count - 1);
        fullRange.Style.Fill.BackgroundColor = ColorHeaderBg;

        // Merge account code + description cells for the label
        ws.Range(row, ColAccountCode, row, ColDescription).Merge();

        ws.Row(row).Height = RowHeightHeader;
        return row + 1;
    }

    private int WriteDetailRow(
        IXLWorksheet ws, ReportRow reportRow,
        List<ReportColumn> columns, string numFmt, int row)
    {
        // Account code
        var codeCell = ws.Cell(row, ColAccountCode);
        codeCell.Value = reportRow.AccountCode;
        codeCell.Style.Font.FontName = FontName;
        codeCell.Style.Font.FontSize = FontSize;
        codeCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

        // Description — indented
        var descCell = ws.Cell(row, ColDescription);
        descCell.Value = reportRow.AccountName;
        descCell.Style.Font.FontName = FontName;
        descCell.Style.Font.FontSize = FontSize;
        descCell.Style.Alignment.Indent = reportRow.Indent;

        // Data cells
        for (int i = 0; i < columns.Count; i++)
        {
            var col  = columns[i];
            var cell = ws.Cell(row, FirstDataCol + i);

            if (reportRow.Cells.TryGetValue(col.ColumnId, out var cv) && cv.Amount.HasValue)
                cell.Value = (double)cv.Amount.Value;

            cell.Style.NumberFormat.Format = numFmt;
            cell.Style.Font.FontName       = FontName;
            cell.Style.Font.FontSize       = FontSize;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        ws.Row(row).Height = RowHeightDefault;
        return row + 1;
    }

    private int WriteTotalRow(
        IXLWorksheet ws, ReportRow reportRow,
        List<ReportColumn> columns, string numFmt, int row)
    {
        // Top border line before subtotal
        var borderRange = ws.Range(row, ColAccountCode, row, FirstDataCol + columns.Count - 1);
        borderRange.Style.Border.TopBorder      = XLBorderStyleValues.Thin;
        borderRange.Style.Border.TopBorderColor = XLColor.Black;

        // Description
        var descCell = ws.Cell(row, ColDescription);
        descCell.Value = reportRow.AccountName;
        descCell.Style.Font.Bold     = true;
        descCell.Style.Font.FontName = FontName;
        descCell.Style.Font.FontSize = FontSize;

        // Shade
        var fullRange = ws.Range(row, ColAccountCode, row, FirstDataCol + columns.Count - 1);
        fullRange.Style.Fill.BackgroundColor = ColorTotalBg;

        // Data cells
        for (int i = 0; i < columns.Count; i++)
        {
            var col  = columns[i];
            var cell = ws.Cell(row, FirstDataCol + i);

            if (reportRow.Cells.TryGetValue(col.ColumnId, out var cv) && cv.Amount.HasValue)
                cell.Value = (double)cv.Amount.Value;

            cell.Style.NumberFormat.Format  = numFmt;
            cell.Style.Font.Bold            = true;
            cell.Style.Font.FontName        = FontName;
            cell.Style.Font.FontSize        = FontSize;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            cell.Style.Fill.BackgroundColor = ColorTotalBg;
        }

        ws.Row(row).Height = RowHeightDefault;
        return row + 1;
    }

    private int WriteGrandTotalRow(
        IXLWorksheet ws, ReportRow reportRow,
        List<ReportColumn> columns, string numFmt, int row)
    {
        // Double top border for grand total
        var borderRange = ws.Range(row, ColAccountCode, row, FirstDataCol + columns.Count - 1);
        borderRange.Style.Border.TopBorder      = XLBorderStyleValues.Double;
        borderRange.Style.Border.TopBorderColor = XLColor.Black;

        // Description
        var descCell = ws.Cell(row, ColDescription);
        descCell.Value = reportRow.AccountName;
        descCell.Style.Font.Bold     = true;
        descCell.Style.Font.FontName = FontName;
        descCell.Style.Font.FontSize = FontSize;

        // Shade
        var fullRange = ws.Range(row, ColAccountCode, row, FirstDataCol + columns.Count - 1);
        fullRange.Style.Fill.BackgroundColor = ColorGrandBg;

        // Data cells
        for (int i = 0; i < columns.Count; i++)
        {
            var col  = columns[i];
            var cell = ws.Cell(row, FirstDataCol + i);

            if (reportRow.Cells.TryGetValue(col.ColumnId, out var cv) && cv.Amount.HasValue)
                cell.Value = (double)cv.Amount.Value;

            cell.Style.NumberFormat.Format  = numFmt;
            cell.Style.Font.Bold            = true;
            cell.Style.Font.FontName        = FontName;
            cell.Style.Font.FontSize        = FontSize;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            cell.Style.Fill.BackgroundColor = ColorGrandBg;

            // Bottom border after grand total
            cell.Style.Border.BottomBorder      = XLBorderStyleValues.Double;
            cell.Style.Border.BottomBorderColor = XLColor.Black;
        }

        ws.Row(row).Height = RowHeightDefault;
        return row + 1;
    }

    private int WriteUnpostedRERow(
        IXLWorksheet ws, ReportRow reportRow,
        List<ReportColumn> columns, string numFmt, int row)
    {
        // Description — italic, blue
        var descCell = ws.Cell(row, ColDescription);
        descCell.Value = reportRow.AccountName;
        descCell.Style.Font.Italic   = true;
        descCell.Style.Font.FontColor = ColorUnpostedFg;
        descCell.Style.Font.FontName = FontName;
        descCell.Style.Font.FontSize = FontSize;

        // Data cells
        for (int i = 0; i < columns.Count; i++)
        {
            var col  = columns[i];
            var cell = ws.Cell(row, FirstDataCol + i);

            if (reportRow.Cells.TryGetValue(col.ColumnId, out var cv) && cv.Amount.HasValue)
                cell.Value = (double)cv.Amount.Value;

            cell.Style.NumberFormat.Format  = numFmt;
            cell.Style.Font.Italic          = true;
            cell.Style.Font.FontColor       = ColorUnpostedFg;
            cell.Style.Font.FontName        = FontName;
            cell.Style.Font.FontSize        = FontSize;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        ws.Row(row).Height = RowHeightDefault;
        return row + 1;
    }

    // ══════════════════════════════════════════════════════════════
    //  Footer
    // ══════════════════════════════════════════════════════════════

    private static void WriteFooter(IXLWorksheet ws, ReportMetadata meta, int row)
    {
        // Thin top border
        ws.Cell(row, ColAccountCode).Style.Border.TopBorder      = XLBorderStyleValues.Thin;
        ws.Cell(row, ColAccountCode).Style.Border.TopBorderColor = XLColor.LightGray;

        var footerCell = ws.Cell(row + 1, ColAccountCode);
        footerCell.Value = $"Generated by BCPFinAnalytics  |  DB: {meta.DbKey}  |  User: {meta.RunByUserId}  |  {meta.RunDate:MM/dd/yyyy HH:mm}";
        footerCell.Style.Font.FontSize  = 8;
        footerCell.Style.Font.FontName  = FontName;
        footerCell.Style.Font.FontColor = XLColor.Gray;
        footerCell.Style.Font.Italic    = true;
    }

    // ══════════════════════════════════════════════════════════════
    //  Column widths and freeze
    // ══════════════════════════════════════════════════════════════

    private static void ApplyColumnWidths(IXLWorksheet ws, List<ReportColumn> columns)
    {
        ws.Column(ColAccountCode).Width = 14;   // Account #
        ws.Column(ColDescription).Width = 40;   // Description

        for (int i = 0; i < columns.Count; i++)
        {
            var col   = columns[i];
            var width = col.Width.HasValue
                ? col.Width.Value / 7.5   // px to Excel width units (approx)
                : 18;
            ws.Column(FirstDataCol + i).Width = Math.Max(width, 16);
        }
    }

    private static void FreezeHeaderRows(IXLWorksheet ws, int currentRow)
    {
        // Freeze the first 4 rows (title, subtitle, blank, column headers)
        ws.SheetView.Freeze(4, 0);
    }

    // ══════════════════════════════════════════════════════════════
    //  Style helpers
    // ══════════════════════════════════════════════════════════════

    private static void StyleColumnHeader(IXLCell cell)
    {
        cell.Style.Font.Bold            = true;
        cell.Style.Font.FontName        = FontName;
        cell.Style.Font.FontSize        = FontSize;
        cell.Style.Fill.BackgroundColor = ColorHeaderBg;
        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
    }

    private static string SanitizeSheetName(string name)
    {
        // Excel sheet names max 31 chars, no special characters
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        foreach (var c in invalid)
            name = name.Replace(c, ' ');
        return name.Length > 31 ? name.Substring(0, 31).TrimEnd() : name;
    }
}
