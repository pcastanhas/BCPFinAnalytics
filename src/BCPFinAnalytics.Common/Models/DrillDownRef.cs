namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// Identifies the GL transaction data behind a drillable report cell.
/// Passed to <see cref="GLDrillDownDialog"/> (in the UI layer) when the
/// user clicks a Detail row cell. The dialog fetches its own transactions
/// and starting balance from the GL — this record only describes WHICH
/// transactions to pull (entity + account + period + basis filters).
///
/// Supports consolidated reports — AcctNums and EntityIds are both lists
/// because a single displayed cell may aggregate multiple accounts and/or
/// multiple entities into one number.
///
/// Only RowType.Detail cells carry a non-null DrillDownRef.
/// Total, GrandTotal, SectionHeader, SubHeader, and UnpostedRetainedEarnings
/// rows are never drillable.
///
/// BASIS EXPANSION RULE (applied by the repository, not the UI):
///   If BasisList contains 'A' or 'C', the query also includes 'B' rows.
///   This matches MRI convention — accrual/cash selections always include
///   the 'Both' basis transactions.
/// </summary>
public sealed record DrillDownRef
{
    /// <summary>
    /// Raw account number(s) — e.g. ["MR063100000"].
    /// One entry for a single-account detail row.
    /// Multiple entries when the row summarises several accounts
    /// (e.g. a detail row that rolls up a sub-group).
    /// Always raw ACCTNUM values — display formatting is applied by the modal.
    /// </summary>
    public IReadOnlyList<string> AcctNums { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Entity ID(s) contributing to this cell's value.
    /// One entry for single-entity reports.
    /// Multiple entries for consolidated reports where several entities
    /// are summed into a single displayed column.
    /// </summary>
    public IReadOnlyList<string> EntityIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Start period for this cell's data range in YYYYMM format.
    /// For income accounts (TYPE='I') this is BEGYRPD.
    /// For balance accounts (TYPE='B'/'C') this is BALFORPD.
    /// </summary>
    public string PeriodFrom { get; init; } = string.Empty;

    /// <summary>End period for this cell's data range in YYYYMM format.</summary>
    public string PeriodTo { get; init; } = string.Empty;

    /// <summary>
    /// User-selected basis values (e.g. ["A"] or ["C"] or ["A","C"]).
    /// The repository applies the expansion rule:
    ///   if list contains 'A' or 'C' → also query BASIS='B' rows.
    /// </summary>
    public IReadOnlyList<string> BasisList { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Display label shown in the modal header.
    /// Pre-formatted by the report strategy — e.g.
    ///   "401-0005 · RESIDENTIAL RENT - GROSS"          (single account)
    ///   "RESIDENTIAL RENT INCOME (4 accounts)"          (multi-account)
    /// The modal never needs to re-derive this.
    /// </summary>
    public string DisplayLabel { get; init; } = string.Empty;
}

/// <summary>
/// Identifies the budget data behind a drillable budget cell.
/// Passed to <see cref="BudgetDrillDownDialog"/> (in the UI layer) when the user
/// clicks a PTD/YTD Budget cell. The dialog fetches its own budget rows from
/// the BUDGETS table and computes the total locally — this record only
/// describes WHICH budget rows to pull (entity + account + period + budget-
/// type filters).
/// </summary>
public sealed record BudgetDrillDownRef
{
    public IReadOnlyList<string> AcctNums   { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> EntityIds  { get; init; } = Array.Empty<string>();
    public string PeriodFrom    { get; init; } = string.Empty;
    public string PeriodTo      { get; init; } = string.Empty;
    public string BudgetType    { get; init; } = string.Empty;
    public string DisplayLabel  { get; init; } = string.Empty;
}
