using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;

namespace BCPFinAnalytics.Services.GlDetail;

/// <summary>
/// Retrieves GL transaction detail for the drill-down modal.
/// Wraps IGlDetailRepository and surfaces errors via ServiceResult.
/// </summary>
public interface IGlDetailService
{
    /// <summary>
    /// Returns all GL detail rows for the given drill-down context.
    /// Basis expansion (A/C → also include B) is applied in the repository.
    /// </summary>
    Task<ServiceResult<IEnumerable<GlDetailRow>>> GetDetailAsync(
        string dbKey,
        DrillDownRef drillDown);
}
