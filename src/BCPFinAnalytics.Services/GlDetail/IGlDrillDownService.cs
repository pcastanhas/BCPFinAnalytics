using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;

namespace BCPFinAnalytics.Services.GlDetail;

/// <summary>
/// Retrieves GL transaction detail for the drill-down modal.
/// Wraps IGlDrillDownRepository and surfaces errors via ServiceResult.
/// </summary>
public interface IGlDrillDownService
{
    /// <summary>
    /// Returns all GL detail rows for the given drill-down context.
    /// Basis expansion (A/C → also include B) is applied in the repository.
    /// </summary>
    Task<ServiceResult<IEnumerable<GlDetailRow>>> GetTransactionsAsync(
        string dbKey,
        DrillDownRef drillDown);
}
