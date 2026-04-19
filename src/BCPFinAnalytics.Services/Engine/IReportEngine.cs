using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;

namespace BCPFinAnalytics.Services.Engine;

/// <summary>
/// The shared engine that every report strategy delegates to.
///
/// Responsibilities:
///   - Validate options (generic + report-specific via <see cref="ReportSpec.Validate"/>)
///   - Load format definition via <c>IFormatLoader</c>
///   - Build GL filter context via <c>GlFilterBuilder</c>
///   - Resolve column list via <see cref="ReportSpec.BuildColumns"/>
///   - Dedupe distinct DataSources across columns; fetch via primitives in parallel
///   - Walk format rows emitting Detail/Summary/Subtotal/GrandTotal rows
///   - Apply sign (DEBCRED + ReverseAmount + ReverseVariance) per accumulated cell
///   - Evaluate derived columns at emit time using current accumulator snapshot
///   - Append Unposted RE row when <see cref="ReportSpec.AppendUnpostedRE"/> is set
///   - Apply suppression via <c>ReportPostProcessor</c>
///   - Assemble <c>ReportResult</c>
///
/// Strategies shrink to ~40 lines: hold a <see cref="ReportSpec"/>, delegate
/// <c>ExecuteAsync</c> to this engine.
/// </summary>
public interface IReportEngine
{
    Task<ServiceResult<ReportResult>> ExecuteAsync(
        ReportSpec spec,
        ReportOptions options);
}
