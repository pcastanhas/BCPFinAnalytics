using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Rendering;

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
