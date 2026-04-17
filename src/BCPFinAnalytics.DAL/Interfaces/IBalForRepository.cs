namespace BCPFinAnalytics.DAL.Interfaces;

/// <summary>
/// Queries the PERIOD table to resolve the BALFORPD anchor —
/// the beginning-of-year period for balance sheet account accumulation.
/// </summary>
public interface IBalForRepository
{
    /// <summary>
    /// Returns the BALFOR anchor period for a given entity and end period.
    ///
    /// Query:
    ///   SELECT ISNULL(MAX(PERIOD), '200001')
    ///   FROM PERIOD
    ///   WHERE BALFOR = 'B'
    ///     AND PERIOD &lt;= @EndPeriod
    ///     AND ENTITYID = @EntityId
    ///
    /// Returns '200001' (sentinel) when the entity has no B-row in PERIOD
    /// (brand-new entity with no closed fiscal years yet).
    ///
    /// This query is always scoped to a single representative entity —
    /// preflight has guaranteed all selected entities share the same YEAREND,
    /// so any entity will yield the same BALFORPD.
    /// </summary>
    Task<string> GetBalForAnchorAsync(string dbKey, string entityId, string endPeriod);
}
