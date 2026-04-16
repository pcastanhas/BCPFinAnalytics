namespace BCPFinAnalytics.Common.Enums;

/// <summary>
/// Controls how the entity/project selection grid behaves
/// on the report options panel.
/// </summary>
public enum SelectionMode
{
    /// <summary>Run report for all entities/projects. Grid is disabled.</summary>
    All = 0,

    /// <summary>Run report only for the listed entity/project IDs.</summary>
    Include = 1,

    /// <summary>Run report for all entities/projects except the listed ones.</summary>
    Exclude = 2,

    /// <summary>Run report for a range — exactly two IDs, second must be &gt;= first.</summary>
    Range = 3
}
