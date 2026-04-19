namespace BCPFinAnalytics.Services.Reports.Trailing12;

/// <summary>
/// Raw GL row returned by the Trailing 12 query.
/// One row per ACCTNUM + ENTITYID + PERIOD combination.
/// Aggregated across entities and pivoted by period in the strategy.
/// </summary>
public class Trailing12RawRow
{
    public string  AcctNum  { get; set; } = string.Empty;
    public string  AcctName { get; set; } = string.Empty;
    public string  Type     { get; set; } = string.Empty;
    public string  EntityId { get; set; } = string.Empty;
    public string  Period   { get; set; } = string.Empty;
    public decimal Amount   { get; set; }
}
