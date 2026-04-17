namespace BCPFinAnalytics.Services.Reports.TrialBalance;

/// <summary>
/// One raw row returned by the Trial Balance repository query.
/// One row per (ACCTNUM, ENTITYID) combination from GACC + GLSUM.
/// Aggregation across entities is performed in the strategy layer.
/// </summary>
public class TrialBalanceRawRow
{
    /// <summary>Raw account number — char(11), may have trailing spaces.</summary>
    public string AcctNum { get; set; } = string.Empty;

    /// <summary>Account display name from GACC.ACCTNAME.</summary>
    public string AcctName { get; set; } = string.Empty;

    /// <summary>Account type from GACC.TYPE — 'B', 'C', or 'I'.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Entity ID this balance belongs to.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Sum of GLSUM.ACTIVITY for this account/entity across the period range.
    /// Null when no activity exists (outer join scenario).
    /// </summary>
    public decimal? Balance { get; set; }
}
