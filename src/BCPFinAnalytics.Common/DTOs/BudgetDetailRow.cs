namespace BCPFinAnalytics.Common.DTOs;

/// <summary>
/// One row from the BUDGETS table for the budget detail drill-down modal.
/// </summary>
public class BudgetDetailRow
{
    public string   Period     { get; set; } = string.Empty;
    public string   EntityId   { get; set; } = string.Empty;
    public string   AcctNum    { get; set; } = string.Empty;
    public string   Department { get; set; } = string.Empty;
    public string   Basis      { get; set; } = string.Empty;
    public string   BudType    { get; set; } = string.Empty;
    public decimal  Activity   { get; set; }
}
