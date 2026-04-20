using BCPFinAnalytics.Common.Interfaces;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.Services.Engine;

namespace BCPFinAnalytics.Services.Reports.TrialBalanceDC;

/// <summary>
/// Trial Balance with Debit &amp; Credit columns report strategy.
///
/// REPORT LAYOUT:
///   Account # | Description | Balance at {PeriodBeforeStart}
///                         | Debits {Start–End} | Credits {Start–End}
///                         | Balance at {End}
///
/// Implementation delegates to <see cref="IReportEngine"/>. All per-report
/// behavior lives in <see cref="TrialBalanceDCSpec"/>.
///
/// Column logic (see spec for details):
///   STARTING — balance as of (StartPeriod - 1), from GlStartingBalance
///   _NET     — hidden signed net activity for [StartPeriod, EndPeriod]
///   DEBITS   — _NET projected to positive side (0 when _NET ≤ 0)
///   CREDITS  — -_NET when _NET &lt; 0, else 0
///   ENDING   — STARTING + _NET
///
/// Drill-down attaches only to DEBITS or CREDITS (not Starting / Ending),
/// and only on the active side of the split (cellValue > 0). Drill window
/// is [StartPeriod, EndPeriod]. SU/TO rows are not drillable — the engine
/// supplies empty AcctNums and the drill factory returns null.
/// </summary>
public class TrialBalanceDCStrategy : IReportStrategy
{
    private readonly IReportEngine _engine;

    public TrialBalanceDCStrategy(IReportEngine engine) => _engine = engine;

    public string ReportCode => "TBDC";
    public string ReportName => "Trial Balance - Debit & Credit";

    /// <inheritdoc />
    public ReportOptionsConfig GetOptionsConfig() => TrialBalanceDCSpec.OptionsConfig;

    /// <inheritdoc />
    public Task<ServiceResult<ReportResult>> ExecuteAsync(ReportOptions options)
        => _engine.ExecuteAsync(TrialBalanceDCSpec.Build(), options);
}
