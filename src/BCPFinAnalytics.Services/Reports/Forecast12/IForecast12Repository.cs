using BCPFinAnalytics.Services.Helpers;

namespace BCPFinAnalytics.Services.Reports.Forecast12;

/// <summary>
/// Data access for the 12-Month Forecast report.
///
/// Two queries:
///   Actual  — GLSUM, PERIOD IN @ActualPeriods, BALFOR='N', BASIS IN @BasisList
///   Budget  — BUDGETS, PERIOD IN @BudgetPeriods, BUDTYPE = @BudgetType
///
/// Each returns one row per ACCTNUM + ENTITYID + PERIOD.
/// Strategy merges both into a single unified pivot.
/// </summary>
public interface IForecast12Repository
{
    Task<IEnumerable<Forecast12RawRow>> GetActualAsync(
        string dbKey,
        GlQueryParameters glParams,
        IReadOnlyList<string> periods);

    Task<IEnumerable<Forecast12RawRow>> GetBudgetAsync(
        string dbKey,
        GlQueryParameters glParams,
        IReadOnlyList<string> periods,
        string budgetType);
}
