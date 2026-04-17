using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;

namespace BCPFinAnalytics.DAL.Interfaces;

/// <summary>
/// Retrieves GL transaction detail for the drill-down modal.
/// Queries JOURNAL (open periods) UNION ALL GHIS (closed periods).
/// </summary>
public interface IGlDetailRepository
{
    /// <summary>
    /// Returns all posted journal entry lines matching the drill-down context.
    ///
    /// Basis expansion rule is applied internally:
    ///   if A or C appears in drillDown.BasisList, B is automatically added.
    ///
    /// Results are ordered by Period, Ref, Item — matching the MRI standard.
    /// </summary>
    Task<IEnumerable<GlDetailRow>> GetDetailAsync(string dbKey, DrillDownRef drillDown);
}
