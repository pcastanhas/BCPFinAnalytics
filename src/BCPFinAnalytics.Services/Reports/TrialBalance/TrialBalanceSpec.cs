using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.Services.Engine;
using BCPFinAnalytics.Services.Helpers;

namespace BCPFinAnalytics.Services.Reports.TrialBalance;

/// <summary>
/// Spec definition for Simple Trial Balance.
///
/// Single column: ending balance at EndPeriod.
/// Balance = GlStartingBalance(EndPeriod) + GlActivity(EndPeriod, EndPeriod).
///
/// Drill-down window per Detail row:
///   B/C accounts  → BalForPd..EndPeriod
///   I/E accounts  → BegYrPd..EndPeriod
///
/// SM (summary) rows drill with BegYrPd..EndPeriod regardless of account type
/// — preserves the behavior from the pre-engine strategy.
/// </summary>
internal static class TrialBalanceSpec
{
    public const string ColBalance = "BALANCE";

    public static readonly ReportOptionsConfig OptionsConfig = new()
    {
        StartPeriodEnabled       = false,
        EndPeriodEnabled         = true,
        EndPeriodRequired        = true,
        BudgetEnabled            = false,
        SFTypeEnabled            = false,
        FormatEnabled            = true,
        BasisEnabled             = true,
        EntitySelectionEnabled   = true,
        WholeDollarsEnabled      = true,
        IsCrosstab               = false
    };

    public static ReportSpec Build() => new()
    {
        ReportCode         = "SIMPLETB",
        ReportName         = "Simple Trial Balance",
        OptionsConfig      = OptionsConfig,
        Validate           = ValidateOptions,
        Consolidation      = ConsolidationMode.Consolidated,
        AppendUnpostedRE   = true,
        UnpostedREColumnId = ColBalance,
        BuildColumns       = BuildColumns
    };

    private static IReadOnlyList<ColumnSpec> BuildColumns(
        GlQueryParameters glParams, ReportOptions options)
    {
        var endDisplay = FiscalCalendar.ToDisplayPeriod(glParams.EndPeriod);
        var endPeriod  = glParams.EndPeriod;
        var balForPd   = glParams.BalForPd;
        var begYrPd    = glParams.BegYrPd;

        return new[]
        {
            new ColumnSpec
            {
                Id        = ColBalance,
                Header    = $"Balance at {endDisplay}",
                DataType  = ColumnDataType.Currency,
                Width     = 150,
                Source    = new DataSource.Sum(
                    new DataSource.GlStartingBalance(endPeriod),
                    new DataSource.GlActivity(endPeriod, endPeriod)),
                Drill     = (ctx, _) =>
                {
                    // Detail rows (single account): branch on acct type for
                    // the drill's PeriodFrom. B/C accounts use the BAL-FOR
                    // anchor; I/E accounts start from the beginning of the
                    // fiscal year.
                    //
                    // SM rows (multiple accounts): BegYrPd — preserves the
                    // "SM rows are typically income" behavior from the
                    // pre-engine strategy.
                    //
                    // Aggregate rows: engine supplies empty AcctNums; these
                    // cells don't need drill so we return null.
                    if (ctx.AcctNums.Count == 0) return null;

                    string periodFrom;
                    if (ctx.AcctNums.Count == 1
                        && ctx.AcctTypes.TryGetValue(ctx.AcctNums[0], out var t)
                        && (t == "B" || t == "C"))
                    {
                        periodFrom = balForPd;
                    }
                    else
                    {
                        periodFrom = begYrPd;
                    }

                    return new DrillDownRef
                    {
                        AcctNums     = ctx.AcctNums,
                        EntityIds    = ctx.EntityIds,
                        BasisList    = ctx.BasisList,
                        PeriodFrom   = periodFrom,
                        PeriodTo     = endPeriod,
                        DisplayLabel = ctx.DisplayLabel
                    };
                }
            }
        };
    }

    private static ServiceResult<bool> ValidateOptions(ReportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EndPeriod))
            return ServiceResult<bool>.Failure(
                "End Period is required for Trial Balance.", ErrorCode.ValidationError);
        if (options.Basis is null || options.Basis.Count == 0)
            return ServiceResult<bool>.Failure(
                "At least one Basis must be selected.", ErrorCode.ValidationError);
        return ServiceResult<bool>.Success(true);
    }
}
