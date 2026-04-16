using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Rendering;

// ══════════════════════════════════════════════════════════════
//  ExcelRenderer — stub, fully implemented in Phase 4
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Renders a ReportResult to an Excel workbook using ClosedXML.
/// Never calls the database or services layer.
/// Fully implemented in Phase 4.
/// </summary>
public class ExcelRenderer : IExcelRenderer
{
    private readonly ILogger<ExcelRenderer> _logger;

    public ExcelRenderer(ILogger<ExcelRenderer> logger)
    {
        _logger = logger;
    }

    public byte[] Render(ReportResult reportResult)
    {
        _logger.LogDebug("ExcelRenderer.Render — report={ReportCode}", reportResult.Metadata.ReportCode);
        // TODO: Phase 4 — implement ClosedXML rendering
        throw new NotImplementedException("ExcelRenderer will be fully implemented in Phase 4.");
    }

    public byte[] RenderPackage(IEnumerable<(string SheetName, ReportResult Result)> sheets)
    {
        _logger.LogDebug("ExcelRenderer.RenderPackage — {Count} sheets", sheets.Count());
        // TODO: Phase 4 — implement multi-sheet ClosedXML workbook for V2 Report Packages
        throw new NotImplementedException("ExcelRenderer.RenderPackage will be fully implemented in Phase 4.");
    }
}

// ══════════════════════════════════════════════════════════════
//  PdfRenderer — stub, fully implemented in Phase 4
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Renders a ReportResult to a PDF document using QuestPDF.
/// Never calls the database or services layer.
/// Fully implemented in Phase 4.
/// </summary>
public class PdfRenderer : IPdfRenderer
{
    private readonly ILogger<PdfRenderer> _logger;

    public PdfRenderer(ILogger<PdfRenderer> logger)
    {
        _logger = logger;
    }

    public byte[] Render(ReportResult reportResult)
    {
        _logger.LogDebug("PdfRenderer.Render — report={ReportCode}", reportResult.Metadata.ReportCode);
        // TODO: Phase 4 — implement QuestPDF rendering
        throw new NotImplementedException("PdfRenderer will be fully implemented in Phase 4.");
    }
}

// ══════════════════════════════════════════════════════════════
//  ScreenReportMapper — stub, fully implemented in Phase 4
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Prepares a ReportResult for the Blazor screen renderer.
/// Applies display-only transformations without calling services or DB.
/// Fully implemented in Phase 4.
/// </summary>
public class ScreenReportMapper : IScreenReportMapper
{
    private readonly ILogger<ScreenReportMapper> _logger;

    public ScreenReportMapper(ILogger<ScreenReportMapper> logger)
    {
        _logger = logger;
    }

    public ReportResult Prepare(ReportResult reportResult)
    {
        _logger.LogDebug("ScreenReportMapper.Prepare — report={ReportCode}", reportResult.Metadata.ReportCode);
        // TODO: Phase 4 — apply display transformations
        return reportResult;
    }
}
