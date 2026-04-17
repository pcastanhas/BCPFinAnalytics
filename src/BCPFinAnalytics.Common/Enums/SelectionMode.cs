namespace BCPFinAnalytics.Common.Enums;

/// <summary>
/// Controls how the entity/project selection grid behaves
/// on the report options panel.
///
/// NOTE: 'All' was removed — running a report against all entities
/// without any filter caused performance issues for minimal benefit.
/// Users must explicitly select entities via Include, Exclude, or Range.
/// </summary>
public enum SelectionMode
{
    /// <summary>Run report only for the listed entity/project IDs.</summary>
    Include = 0,

    /// <summary>Run report for all entities/projects except the listed ones.</summary>
    Exclude = 1,

    /// <summary>Run report for a range — exactly two IDs, second must be &gt;= first.</summary>
    Range = 2
}
