namespace BCPFinAnalytics.Services.Reports.Forecast12;

/// <summary>
/// Raw row for the 12-month forecast report.
/// Shared shape for both actual (GLSUM) and budget (BUDGETS) queries.
/// Period field is used to pivot columns in the strategy.
/// </summary>
public class Forecast12RawRow
{
    public string  AcctNum  { get; set; } = string.Empty;
    public string  AcctName { get; set; } = string.Empty;
    public string  Type     { get; set; } = string.Empty;
    public string  EntityId { get; set; } = string.Empty;
    public string  Period   { get; set; } = string.Empty;
    public decimal Amount   { get; set; }
}
