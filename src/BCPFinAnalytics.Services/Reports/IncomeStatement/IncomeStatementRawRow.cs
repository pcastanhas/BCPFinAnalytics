namespace BCPFinAnalytics.Services.Reports.IncomeStatement;

/// <summary>
/// Raw GL row returned by the Income Statement queries.
/// Shared shape for both actual and budget queries.
/// </summary>
public class IncomeStatementRawRow
{
    public string  AcctNum  { get; set; } = string.Empty;
    public string  AcctName { get; set; } = string.Empty;
    public string  Type     { get; set; } = string.Empty;
    public string  EntityId { get; set; } = string.Empty;
    public decimal Amount   { get; set; }
}
