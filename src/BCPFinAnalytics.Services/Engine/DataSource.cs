namespace BCPFinAnalytics.Services.Engine;

/// <summary>
/// Describes how an accumulated column's per-account decimal is sourced.
/// Every accumulated column's <see cref="ColumnSpec.Source"/> is one of these
/// shapes. The engine evaluates distinct DataSources once per report run
/// (deduplicating identical sources shared across columns) then indexes the
/// results by account number for the per-column accumulation pass.
///
/// Composition with <see cref="Sum"/> lets a single column roll up multiple
/// primitive calls — used by Simple TB's balance column which is
/// <c>GlStartingBalance(EndPeriod) + GlActivity(EndPeriod, EndPeriod)</c>.
/// </summary>
public abstract record DataSource
{
    /// <summary>
    /// GL balance at end of (Period - 1). The engine calls
    /// <c>IGlDataRepository.GetGlStartingBalanceAsync</c> with this period.
    /// </summary>
    public sealed record GlStartingBalance(string Period) : DataSource;

    /// <summary>
    /// Net GL activity summed over [StartPeriod, EndPeriod] inclusive.
    /// BALFOR='N' filter applied inside the primitive.
    /// </summary>
    public sealed record GlActivity(string StartPeriod, string EndPeriod) : DataSource;

    /// <summary>
    /// Budget total over [StartPeriod, EndPeriod] for the given budget type.
    /// BASIS filter applied by the primitive.
    /// </summary>
    public sealed record BudgetAmount(
        string StartPeriod,
        string EndPeriod,
        string BudgetType) : DataSource;

    /// <summary>
    /// Sum of N sub-sources. Used when a single displayed column rolls up
    /// multiple primitives (e.g. Simple TB ending balance = snapshot + current
    /// period activity). The engine evaluates each sub-source and sums per
    /// account.
    /// </summary>
    public sealed record Sum(params DataSource[] Sources) : DataSource;
}
