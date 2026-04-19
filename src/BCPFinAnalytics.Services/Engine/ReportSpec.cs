using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.Services.Helpers;

namespace BCPFinAnalytics.Services.Engine;

/// <summary>
/// How the engine fetches data. Consolidating reports (the majority) sum
/// across entities before accumulation; per-entity reports (Property
/// Comparison) keep per-entity breakdowns for per-column scoping.
/// </summary>
public enum ConsolidationMode
{
    Consolidated,
    PerEntity
}

/// <summary>
/// Full description of a report. Each strategy holds a <see cref="ReportSpec"/>
/// (typically built once per invocation via <see cref="BuildColumns"/>) and
/// delegates <c>ExecuteAsync</c> to the engine.
/// </summary>
public sealed record ReportSpec
{
    public required string ReportCode { get; init; }
    public required string ReportName { get; init; }

    /// <summary>
    /// UI-facing option configuration — which options are enabled, required,
    /// or hidden. Same <see cref="ReportOptionsConfig"/> shape the old
    /// <c>IReportStrategy.GetOptionsConfig</c> returned; the engine doesn't
    /// read this directly but strategies surface it through their existing
    /// interface.
    /// </summary>
    public required ReportOptionsConfig OptionsConfig { get; init; }

    /// <summary>
    /// Factory that produces the column list at execution time, using the
    /// resolved GL filter params (BegYrPd, BalForPd, EndPeriod, Entities, ...)
    /// and the raw user options. Called once per report run, after validation
    /// and filter-building but before data fetch.
    ///
    /// Chosen over static column lists because column definitions frequently
    /// reference runtime period values (e.g. <c>GlActivity(StartPeriod,
    /// EndPeriod)</c>, <c>Header = $"Balance at {endDisplay}"</c>).
    /// </summary>
    public required Func<GlQueryParameters, ReportOptions, IReadOnlyList<ColumnSpec>> BuildColumns { get; init; }

    /// <summary>
    /// Pre-execution validator. Runs before format load / filter build /
    /// data fetch. Returns <c>ServiceResult.Failure</c> to abort early.
    /// Null means no report-specific validation (base validation still runs).
    /// </summary>
    public Func<ReportOptions, ServiceResult<bool>>? Validate { get; init; }

    /// <summary>
    /// Consolidation mode — default <see cref="ConsolidationMode.Consolidated"/>.
    /// Setting this to <see cref="ConsolidationMode.PerEntity"/> switches the
    /// engine to use the by-entity primitive variants. Only used by
    /// Property Comparison currently.
    /// </summary>
    public ConsolidationMode Consolidation { get; init; } = ConsolidationMode.Consolidated;

    /// <summary>
    /// If true, the engine appends the Unposted Retained Earnings row to the
    /// report output using the shared <see cref="IUnpostedREService"/>.
    /// Used by Trial Balance and Balance Sheet variants — any report where
    /// the display includes a closing equity position that needs to reflect
    /// current-period P&amp;L that hasn't yet closed to retained earnings.
    /// </summary>
    public bool AppendUnpostedRE { get; init; } = false;

    /// <summary>
    /// When <see cref="AppendUnpostedRE"/> is true, the column ID that
    /// receives the RE amount in the appended row. Must match one of the
    /// column IDs produced by <see cref="BuildColumns"/>. Ignored when
    /// <see cref="AppendUnpostedRE"/> is false.
    /// </summary>
    public string? UnpostedREColumnId { get; init; }
}
