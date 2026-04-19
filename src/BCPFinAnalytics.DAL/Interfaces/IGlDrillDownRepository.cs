using BCPFinAnalytics.Common.Models;

namespace BCPFinAnalytics.DAL.Interfaces;

/// <summary>
/// Provides GL transaction detail for the drill-down modal.
/// Queries JOURNAL (open periods) and GHIS (closed periods) via UNION ALL.
///
/// JOURNAL: STATUS = 'P' (posted only — 'U' = unposted, never shown in drill-down)
/// GHIS:    BALFOR = 'N' (activity rows only — beginning balance rows excluded)
///
/// BASIS EXPANSION RULE:
///   If BasisList contains 'A' or 'C', 'B' rows are automatically included.
///   Applied here in the repository, not by the caller.
/// </summary>
public interface IGlDrillDownRepository
{
    /// <summary>
    /// Returns all posted GL transactions matching the drill-down context,
    /// from both open (JOURNAL) and closed (GHIS) periods.
    /// Ordered by period, ref, item — the natural JE entry order.
    /// </summary>
    Task<IEnumerable<GlDetailRow>> GetTransactionsAsync(
        string dbKey,
        DrillDownRef drillDown);
}
