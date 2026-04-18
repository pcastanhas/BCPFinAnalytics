using BCPFinAnalytics.Common.Enums;

namespace BCPFinAnalytics.DAL.Interfaces;

/// <summary>
/// Provides entity-level metadata queries used by preflight validation.
/// Kept separate from ILookupRepository — these queries have fiscal-calendar
/// semantics and are not simple dropdown lookups.
/// </summary>
public interface IEntityMetaRepository
{
    /// <summary>
    /// Returns the count of distinct YEAREND values across the effective
    /// entity selection. Used by preflight to detect mixed fiscal year-ends.
    ///
    /// SelectionMode drives the WHERE clause:
    ///   All     → no filter (all entities)
    ///   Include → ENTITYID IN (@ids)
    ///   Exclude → ENTITYID NOT IN (@ids)
    ///   Range   → ENTITYID BETWEEN @lo AND @hi
    /// </summary>
    Task<int> GetDistinctYearEndCountAsync(
        string dbKey,
        BCPFinAnalytics.Common.Enums.SelectionMode selectionMode,
        IReadOnlyList<string> selectedIds);

    /// <summary>
    /// Returns the distinct YEAREND values across the effective entity selection.
    /// Only called on the preflight FAILURE path to populate the error message.
    /// </summary>
    Task<IEnumerable<string>> GetDistinctYearEndsAsync(
        string dbKey,
        BCPFinAnalytics.Common.Enums.SelectionMode selectionMode,
        IReadOnlyList<string> selectedIds);

    /// <summary>
    /// Returns the distinct LEDGCODE values across the effective entity selection.
    /// Used by preflight to validate all entities share the same ledger code,
    /// and to derive the LedgCode for the report without user input.
    /// </summary>
    Task<IEnumerable<string>> GetDistinctLedgCodesAsync(
        string dbKey,
        BCPFinAnalytics.Common.Enums.SelectionMode selectionMode,
        IReadOnlyList<string> selectedIds);
}
