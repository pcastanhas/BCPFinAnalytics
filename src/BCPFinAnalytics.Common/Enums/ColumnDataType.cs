namespace BCPFinAnalytics.Common.Enums;

/// <summary>
/// Defines the data type of a report column.
/// Used by renderers to apply correct number formatting.
/// </summary>
public enum ColumnDataType
{
    /// <summary>Formatted as currency — e.g. 1,234,567.89 or 1,234,568 (whole dollars).</summary>
    Currency = 0,

    /// <summary>Formatted as a percentage — e.g. 12.34% or N/A when not calculable.</summary>
    Percent = 1,

    /// <summary>Plain text — no numeric formatting applied.</summary>
    Text = 2,

    /// <summary>Whole number — no decimal places regardless of WholeDollars setting.</summary>
    Integer = 3
}
