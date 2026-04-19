using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Interfaces;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.Services.Engine;

namespace BCPFinAnalytics.Services.Reports.TrialBalance;

/// <summary>
/// Simple Trial Balance report strategy.
///
/// REPORT LAYOUT:
///   Account #  |  Description  |  Balance at MM/YYYY
///
/// Single column — ending balance only. No budget, no variance, no
/// MTD/YTD split.
///
/// Implementation: delegates entirely to <see cref="IReportEngine"/>.
/// All per-report behavior lives in <see cref="TrialBalanceSpec"/>:
/// the column definition, the drill-down factory, the report-specific
/// validation, and the Unposted-RE flag.
///
/// Compose: balance at end of EndPeriod
///   = GlStartingBalance(EndPeriod)       — balance at end of (EndPeriod-1)
///   + GlActivity(EndPeriod, EndPeriod)   — activity in EndPeriod itself
///
/// Drill-down per Detail row, PeriodFrom:
///   B/C accounts → BalForPd
///   I/E accounts → BegYrPd
///
/// The engine handles: format load, GL info resolution, filter build,
/// parallel primitive fetch, format-row walk (BL/TI/RA/SM/SU/TO),
/// sign application, subtotal accumulation, grand total rollup,
/// Unposted-RE append, suppression, metadata assembly.
/// </summary>
public class TrialBalanceStrategy : IReportStrategy
{
    private readonly IReportEngine _engine;

    public TrialBalanceStrategy(IReportEngine engine) => _engine = engine;

    public string ReportCode => "SIMPLETB";
    public string ReportName => "Simple Trial Balance";

    /// <inheritdoc />
    public ReportOptionsConfig GetOptionsConfig() => TrialBalanceSpec.OptionsConfig;

    /// <inheritdoc />
    public Task<ServiceResult<ReportResult>> ExecuteAsync(ReportOptions options)
        => _engine.ExecuteAsync(TrialBalanceSpec.Build(), options);
}
