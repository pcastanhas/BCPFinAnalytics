namespace BCPFinAnalytics.Services.Reports.TrialBalanceDC;

/// <summary>
/// Raw row returned by the Trial Balance DC repository.
/// Two queries are executed and their results combined:
///   1. Starting Balance query  — activity from period-open through PeriodBeforeStart
///   2. Activity query          — activity from StartPeriod through EndPeriod
///
/// One row per (ACCTNUM, ENTITYID) per query.
/// The strategy aggregates across entities and combines the two query results.
/// </summary>
public class TrialBalanceDCRawRow
{
    /// <summary>Raw account number — char(11), may have trailing spaces.</summary>
    public string AcctNum { get; set; } = string.Empty;

    /// <summary>Account display name from GACC.ACCTNAME.</summary>
    public string AcctName { get; set; } = string.Empty;

    /// <summary>Account type from GACC.TYPE — 'B', 'C', or 'I'.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Entity ID this balance belongs to.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Sum of GLSUM.ACTIVITY for this account/entity across the queried period range.</summary>
    public decimal? Amount { get; set; }
}
