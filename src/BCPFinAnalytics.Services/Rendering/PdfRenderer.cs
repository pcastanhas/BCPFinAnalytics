using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Services.Interfaces;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BCPFinAnalytics.Services.Rendering;

/// <summary>
/// Renders a ReportResult to a PDF document using QuestPDF 2024.10.
///
/// LAYOUT:
///   Landscape orientation, A4 or Letter width
///   Header: report title, entity, period (repeated on every page)
///   Body: table with Account # | Description | data columns
///   Footer: page N of M | DB key | run info (repeated on every page)
///
/// ROW STYLING (mirrors ExcelRenderer):
///   SectionHeader        — bold, light-grey background, no account code
///   Detail               — normal, account code + indented description
///   Total                — bold, top border, light-grey background
///   GrandTotal           — bold, double top border, darker background
///   UnpostedRetainedEarnings — italic, blue text
///
/// NUMBER FORMAT:
///   Whole dollars : #,##0 with (parentheses) for negatives, dash for zero
///   With cents    : #,##0.00 with (parentheses) for negatives, dash for zero
///
/// NEVER calls the database or services layer.
/// </summary>
public class PdfRenderer : IPdfRenderer
{
    private readonly ILogger<PdfRenderer> _logger;

    // Colors (RGB)
    private static readonly string ColorHeaderBg    = "#D9E1F2";
    private static readonly string ColorTotalBg     = "#F2F2F2";
    private static readonly string ColorGrandBg     = "#D6DCE4";
    private static readonly string ColorUnpostedFg  = "#0070C0";
    private static readonly string ColorTitleFg     = "#1F3864";
    private static readonly string ColorSubtitleFg  = "#666666";
    private static readonly string ColorBorderFg    = "#9DC3E6";

    // Font
    private const string FontName = "Arial";

    public PdfRenderer(ILogger<PdfRenderer> logger)
    {
        _logger = logger;

        // QuestPDF community licence — required for non-commercial / internal use
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <inheritdoc />
    public byte[] Render(ReportResult reportResult)
    {
        _logger.LogInformation(
            "PdfRenderer.Render — ReportCode={Code} Rows={Rows} Cols={Cols}",
            reportResult.Metadata.ReportCode,
            reportResult.Rows.Count,
            reportResult.Columns.Count);

        var numFmt = reportResult.Metadata.WholeDollars;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                // Landscape A4
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(x => x.FontFamily(FontName).FontSize(9));

                page.Header().Element(c => ComposeHeader(c, reportResult.Metadata));
                page.Content().Element(c => ComposeBody(c, reportResult, numFmt));
                page.Footer().Element(c => ComposeFooter(c, reportResult.Metadata));
            });
        });

        var bytes = document.GeneratePdf();

        _logger.LogInformation(
            "PdfRenderer.Render — Complete. Bytes={Bytes}", bytes.Length);

        return bytes;
    }

    // ══════════════════════════════════════════════════════════════
    //  Header (repeated on every page)
    // ══════════════════════════════════════════════════════════════

    private void ComposeHeader(IContainer container, ReportMetadata meta)
    {
        container.Column(col =>
        {
            // Report title
            col.Item().Text(meta.ReportTitle)
                .FontSize(14)
                .Bold()
                .FontColor(ColorTitleFg)
                .FontFamily(FontName);

            // Subtitle: entity + period + run info
            var periodText = string.IsNullOrEmpty(meta.StartPeriod)
                ? $"Period: {meta.EndPeriod}"
                : $"Period: {meta.StartPeriod} – {meta.EndPeriod}";

            col.Item().Text(
                $"{meta.EntityName}     {periodText}     " +
                $"Run: {meta.RunDate:MM/dd/yyyy HH:mm}     DB: {meta.DbKey}     User: {meta.RunByUserId}")
                .FontSize(8)
                .FontColor(ColorSubtitleFg)
                .FontFamily(FontName);

            // Divider
            col.Item().PaddingTop(4).LineHorizontal(1f).LineColor(ColorBorderFg);
            col.Item().Height(4);
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Body — table
    // ══════════════════════════════════════════════════════════════

    private void ComposeBody(IContainer container, ReportResult report, bool wholeDollars)
    {
        container.Table(table =>
        {
            // ── Column definitions ─────────────────────────────────
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(70);    // Account #
                cols.RelativeColumn(3);     // Description — gets remaining space

                foreach (var col in report.Columns)
                {
                    var pts = col.Width.HasValue
                        ? (float)(col.Width.Value * 0.75f)  // px → pts approx
                        : 90f;
                    cols.ConstantColumn(Math.Max(pts, 75f));
                }
            });

            // ── Column headers ─────────────────────────────────────
            table.Header(header =>
            {
                void HCell(string text) =>
                    header.Cell()
                        .Background(ColorHeaderBg)
                        .BorderBottom(1.5f).BorderColor(ColorBorderFg)
                        .Padding(3)
                        .AlignMiddle()
                        .Text(text)
                        .Bold()
                        .FontSize(9)
                        .FontFamily(FontName);

                HCell("Account #");
                HCell("Description");
                foreach (var col in report.Columns)
                    HCell(col.Header);
            });

            // ── Data rows ──────────────────────────────────────────
            foreach (var row in report.Rows)
            {
                switch (row.RowType)
                {
                    case RowType.SectionHeader:
                        WriteSectionHeaderRow(table, row, report.Columns);
                        break;
                    case RowType.Detail:
                        WriteDetailRow(table, row, report.Columns, wholeDollars);
                        break;
                    case RowType.Total:
                        WriteTotalRow(table, row, report.Columns, wholeDollars);
                        break;
                    case RowType.GrandTotal:
                        WriteGrandTotalRow(table, row, report.Columns, wholeDollars);
                        break;
                    case RowType.UnpostedRetainedEarnings:
                        WriteUnpostedRERow(table, row, report.Columns, wholeDollars);
                        break;
                }
            }
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Row writers
    // ══════════════════════════════════════════════════════════════

    private void WriteSectionHeaderRow(
        TableDescriptor table, ReportRow row, List<ReportColumn> columns)
    {
        // Blank spacer row
        if (string.IsNullOrWhiteSpace(row.AccountName))
        {
            table.Cell().ColumnSpan((uint)(2 + columns.Count))
                .Height(6).Background(Colors.White);
            return;
        }

        // Full-width label spanning all columns
        table.Cell().ColumnSpan((uint)(2 + columns.Count))
            .Background(ColorHeaderBg)
            .Padding(3)
            .Text(row.AccountName.ToUpper())
            .Bold()
            .FontSize(9)
            .FontFamily(FontName);
    }

    private void WriteDetailRow(
        TableDescriptor table, ReportRow row,
        List<ReportColumn> columns, bool wholeDollars)
    {
        // Account code
        table.Cell()
            .BorderBottom(0.25f).BorderColor(ColorTotalBg)
            .PaddingLeft(2).PaddingVertical(2)
            .Text(row.AccountCode)
            .FontSize(8.5f)
            .FontFamily(FontName);

        // Description — indented
        var indent = row.Indent * 10f;
        table.Cell()
            .BorderBottom(0.25f).BorderColor(ColorTotalBg)
            .PaddingLeft(indent + 2).PaddingVertical(2)
            .Text(row.AccountName)
            .FontSize(8.5f)
            .FontFamily(FontName);

        // Data cells
        foreach (var col in columns)
        {
            var amount = row.Cells.TryGetValue(col.ColumnId, out var cv)
                ? cv.Amount : null;
            table.Cell()
                .BorderBottom(0.25f).BorderColor(ColorTotalBg)
                .PaddingRight(3).PaddingVertical(2)
                .AlignRight()
                .Text(FormatAmount(amount, wholeDollars))
                .FontSize(8.5f)
                .FontFamily(FontName);
        }
    }

    private void WriteTotalRow(
        TableDescriptor table, ReportRow row,
        List<ReportColumn> columns, bool wholeDollars)
    {
        // Empty account code cell
        table.Cell()
            .Background(ColorTotalBg)
            .BorderTop(1f).BorderColor(Colors.Black)
            .PaddingVertical(2)
            .Text(string.Empty);

        // Label
        table.Cell()
            .Background(ColorTotalBg)
            .BorderTop(1f).BorderColor(Colors.Black)
            .PaddingLeft(2).PaddingVertical(2)
            .Text(row.AccountName)
            .Bold()
            .FontFamily(FontName);

        // Data cells
        foreach (var col in columns)
        {
            var amount = row.Cells.TryGetValue(col.ColumnId, out var cv)
                ? cv.Amount : null;
            table.Cell()
                .Background(ColorTotalBg)
                .BorderTop(1f).BorderColor(Colors.Black)
                .PaddingRight(3).PaddingVertical(2)
                .AlignRight()
                .Text(FormatAmount(amount, wholeDollars))
                .Bold()
                .FontFamily(FontName);
        }
    }

    private void WriteGrandTotalRow(
        TableDescriptor table, ReportRow row,
        List<ReportColumn> columns, bool wholeDollars)
    {
        // Empty account code cell
        table.Cell()
            .Background(ColorGrandBg)
            .BorderTop(2f).BorderColor(Colors.Black)
            .BorderBottom(2f).BorderColor(Colors.Black)
            .PaddingVertical(2)
            .Text(string.Empty);

        // Label
        table.Cell()
            .Background(ColorGrandBg)
            .BorderTop(2f).BorderColor(Colors.Black)
            .BorderBottom(2f).BorderColor(Colors.Black)
            .PaddingLeft(2).PaddingVertical(2)
            .Text(row.AccountName)
            .Bold()
            .FontFamily(FontName);

        // Data cells
        foreach (var col in columns)
        {
            var amount = row.Cells.TryGetValue(col.ColumnId, out var cv)
                ? cv.Amount : null;
            table.Cell()
                .Background(ColorGrandBg)
                .BorderTop(2f).BorderColor(Colors.Black)
                .BorderBottom(2f).BorderColor(Colors.Black)
                .PaddingRight(3).PaddingVertical(2)
                .AlignRight()
                .Text(FormatAmount(amount, wholeDollars))
                .Bold()
                .FontFamily(FontName);
        }
    }

    private void WriteUnpostedRERow(
        TableDescriptor table, ReportRow row,
        List<ReportColumn> columns, bool wholeDollars)
    {
        // Empty account code cell
        table.Cell().PaddingVertical(2).Text(string.Empty);

        // Label — italic blue
        table.Cell()
            .PaddingLeft(2).PaddingVertical(2)
            .Text(row.AccountName)
            .Italic()
            .FontColor(ColorUnpostedFg)
            .FontFamily(FontName);

        // Data cells
        foreach (var col in columns)
        {
            var amount = row.Cells.TryGetValue(col.ColumnId, out var cv)
                ? cv.Amount : null;
            table.Cell()
                .PaddingRight(3).PaddingVertical(2)
                .AlignRight()
                .Text(FormatAmount(amount, wholeDollars))
                .Italic()
                .FontColor(ColorUnpostedFg)
                .FontFamily(FontName);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Footer (repeated on every page)
    // ══════════════════════════════════════════════════════════════

    private void ComposeFooter(IContainer container, ReportMetadata meta)
    {
        container.Column(col =>
        {
            col.Item().LineHorizontal(0.5f).LineColor(ColorBorderFg);
            col.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(
                    $"BCPFinAnalytics  |  DB: {meta.DbKey}  |  User: {meta.RunByUserId}  |  {meta.RunDate:MM/dd/yyyy HH:mm}")
                    .FontSize(7.5f)
                    .FontColor(ColorSubtitleFg)
                    .Italic();

                row.AutoItem().Text(text =>
                {
                    text.Span("Page ").FontSize(7.5f).FontColor(ColorSubtitleFg);
                    text.CurrentPageNumber().FontSize(7.5f).FontColor(ColorSubtitleFg);
                    text.Span(" of ").FontSize(7.5f).FontColor(ColorSubtitleFg);
                    text.TotalPages().FontSize(7.5f).FontColor(ColorSubtitleFg);
                });
            });
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════



    /// <summary>
    /// Formats a nullable decimal amount for PDF display.
    /// Null → blank, zero → "–", negative → parentheses, positive → formatted number.
    /// </summary>
    private static string FormatAmount(decimal? amount, bool wholeDollars)
    {
        if (!amount.HasValue) return string.Empty;
        if (amount.Value == 0m) return "–";

        if (wholeDollars)
        {
            return amount.Value < 0
                ? $"({Math.Abs(amount.Value):N0})"
                : $"{amount.Value:N0}";
        }
        else
        {
            return amount.Value < 0
                ? $"({Math.Abs(amount.Value):N2})"
                : $"{amount.Value:N2}";
        }
    }
}
