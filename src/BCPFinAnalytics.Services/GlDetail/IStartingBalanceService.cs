using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;

namespace BCPFinAnalytics.Services.GlDetail;

/// <summary>
/// Retrieves the starting balance for a GL drill-down — the balance as of the
/// period immediately before <see cref="DrillDownRef.PeriodFrom"/>.
///
/// Wraps <c>IStartingBalanceRepository</c> and surfaces errors via
/// <c>ServiceResult</c> so the dialog can render a friendly error state
/// rather than crashing on a DB blip.
/// </summary>
public interface IStartingBalanceService
{
    Task<ServiceResult<decimal>> GetStartingBalanceAsync(
        string dbKey, DrillDownRef drillDown);
}
