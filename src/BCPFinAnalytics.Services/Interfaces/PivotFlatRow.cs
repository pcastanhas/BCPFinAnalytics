namespace BCPFinAnalytics.Services.Interfaces;

/// <summary>
/// Represents a single normalized/flat row returned from the DAL
/// for crosstab reports before C# pivoting.
///
/// The DAL returns one row per (EntityId, AccountCode, Value) combination.
/// IPivotService then transforms these into ReportResult with dynamic columns.
/// </summary>
public class PivotFlatRow
{
    public string ColumnId { get; set; } = string.Empty;   // e.g. EntityId
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountGroup { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public decimal? Value { get; set; }
}
