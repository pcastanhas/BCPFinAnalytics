using BCPFinAnalytics.Common.Enums;

namespace BCPFinAnalytics.Services.Engine;

/// <summary>
/// Computes a derived column's value from the current accumulator snapshot.
///
/// Called at three points in the report lifecycle:
///   - Detail row   — accumulator = a single account's per-column decimals
///   - Subtotal row — accumulator = the group accumulator just closed
///   - Grand total  — accumulator = the grand-total accumulator
///
/// Same lambda runs at all three points — only the input changes.
/// By convention formulas re-derive from primitive accumulators rather
/// than depending on other derived columns, so no dependency ordering.
/// </summary>
public delegate decimal DerivedFn(IReadOnlyDictionary<string, decimal> accumulator);

/// <summary>
/// Produces a drill-down reference for a cell given the context of the row
/// being emitted. Returns <c>null</c> if the cell isn't clickable.
///
/// Return type is <c>object?</c> so a single factory type can return either
/// a GL <c>DrillDownRef</c> or a <c>BudgetDrillDownRef</c> depending on the
/// column's data source. The engine dispatches on the actual type when
/// attaching the ref to the produced cell.
/// </summary>
public delegate object? DrillFactory(DrillContext ctx);

/// <summary>
/// Context supplied to a <see cref="DrillFactory"/> at cell-emit time. The
/// engine populates all fields from the report-level filter context plus
/// the current row's account(s).
/// </summary>
public sealed record DrillContext
{
    /// <summary>
    /// One entry for detail rows (single account) or many for summary /
    /// subtotal / grand-total rows (multiple accounts rolled up).
    /// </summary>
    public required IReadOnlyList<string> AcctNums { get; init; }

    public required IReadOnlyList<string> EntityIds { get; init; }
    public required IReadOnlyList<string> BasisList { get; init; }
    public required string DisplayLabel           { get; init; }

    /// <summary>
    /// Account type lookup (<c>AcctNum → Type</c>) for accounts that appear
    /// in <see cref="AcctNums"/>. Used by drill factories that need to
    /// branch on account type — e.g. Simple TB uses a different
    /// <c>PeriodFrom</c> for Balance-Sheet accounts (B/C) vs Income accounts.
    /// Populated for Detail and Summary rows; empty for aggregate rows.
    /// </summary>
    public IReadOnlyDictionary<string, string> AcctTypes { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>
/// Describes one column on a report.
///
/// Every column is EITHER accumulated (has a <see cref="Source"/>) OR derived
/// (has a <see cref="Derived"/> function) — never both, never neither.
/// <see cref="Hidden"/> columns participate in accumulation but don't render,
/// letting formulas depend on intermediate values without cluttering the UI.
/// </summary>
public sealed record ColumnSpec
{
    /// <summary>
    /// Accumulator key and output ColumnId. Must be unique within a report.
    /// Conventions: <c>"BALANCE"</c>, <c>"PTD_ACTUAL"</c>, <c>"M_202503"</c>,
    /// prefix with <c>_</c> for hidden columns (e.g. <c>"_NET"</c>).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display text for the column header. Ignored when <see cref="Hidden"/>.
    /// </summary>
    public required string Header { get; init; }

    public ColumnDataType DataType   { get; init; } = ColumnDataType.Currency;
    public int            Width      { get; init; } = 120;
    public bool           RightAlign { get; init; } = true;

    /// <summary>
    /// Intermediate accumulator that doesn't render in the output. Used when
    /// one or more derived columns depend on a primitive-sourced sum that
    /// isn't itself shown (e.g. TBDC's <c>_NET</c>, consumed by
    /// <c>DEBITS</c>, <c>CREDITS</c>, and <c>ENDING</c>).
    /// </summary>
    public bool Hidden { get; init; } = false;

    /// <summary>
    /// How this column's accumulated decimal is produced — non-null for
    /// accumulated columns, null for derived.
    /// </summary>
    public DataSource? Source { get; init; }

    /// <summary>
    /// Formula that produces the cell value from the current accumulator —
    /// non-null for derived columns, null for accumulated.
    /// </summary>
    public DerivedFn? Derived { get; init; }

    /// <summary>
    /// Produces the cell's drill-down ref. Null means the cell isn't
    /// clickable. Same factory used at detail, summary, subtotal, and
    /// grand-total emit points — the engine populates <see cref="DrillContext"/>
    /// differently at each point.
    /// </summary>
    public DrillFactory? Drill { get; init; }
}
