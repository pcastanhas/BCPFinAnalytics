using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.Services.Engine;
using BCPFinAnalytics.Services.Helpers;

namespace BCPFinAnalytics.Services.Reports.TrialBalanceDC;

/// <summary>
/// Spec definition for Trial Balance with Debit/Credit split.
///
/// Four visible columns + one hidden intermediate:
///   STARTING — GlStartingBalance(StartPeriod). Balance as of (StartPeriod - 1).
///   _NET     — Hidden. GlActivity(StartPeriod, EndPeriod). Signed net.
///   DEBITS   — Derived. _NET when positive, else 0.
///   CREDITS  — Derived. -_NET when negative, else 0.
///   ENDING   — Derived. STARTING + _NET.
///
/// STARTING and ENDING always render their computed value (including 0) —
/// they represent balance positions that must always display.
///
/// DEBITS and CREDITS set BlankWhenZero=true — exactly one side is active
/// per detail row; the inactive side renders blank, not "0.00". This also
/// automatically suppresses the drill on the inactive side since the engine
/// skips the factory call for blank cells.
///
/// Drill-down:
///   STARTING, ENDING → no drill factory (not clickable)
///   DEBITS / CREDITS → drill over [StartPeriod, EndPeriod] on the active
///                       side only; engine skips the factory when blank
///
/// SU / TO rows: engine supplies empty AcctNums — drill factory returns null.
/// </summary>
internal static class TrialBalanceDCSpec
{
    public const string ColStarting = "STARTING";
    public const string ColNet      = "_NET";
    public const string ColDebits   = "DEBITS";
    public const string ColCredits  = "CREDITS";
    public const string ColEnding   = "ENDING";

    public static readonly ReportOptionsConfig OptionsConfig = new()
    {
        StartPeriodEnabled     = true,
        EndPeriodEnabled       = true,
        EndPeriodRequired      = true,
        BudgetEnabled          = false,
        SFTypeEnabled          = false,
        FormatEnabled          = true,
        BasisEnabled           = true,
        EntitySelectionEnabled = true,
        WholeDollarsEnabled    = true,
        IsCrosstab             = false
    };

    public static ReportSpec Build() => new()
    {
        ReportCode         = "TBDC",
        ReportName         = "Trial Balance - Debit & Credit",
        OptionsConfig      = OptionsConfig,
        Validate           = ValidateOptions,
        Consolidation      = ConsolidationMode.Consolidated,
        AppendUnpostedRE   = true,
        UnpostedREColumnId = ColEnding,
        BuildColumns       = BuildColumns
    };

    private static IReadOnlyList<ColumnSpec> BuildColumns(
        GlQueryParameters glParams, ReportOptions options)
    {
        var startPeriod       = FiscalCalendar.ToMriPeriod(options.StartPeriod);
        var endPeriod         = glParams.EndPeriod;
        var periodBeforeStart = FiscalCalendar.PreviousPeriod(startPeriod);
        var startDisplay      = FiscalCalendar.ToDisplayPeriod(startPeriod);
        var endDisplay        = FiscalCalendar.ToDisplayPeriod(endPeriod);
        var prevDisplay       = FiscalCalendar.ToDisplayPeriod(periodBeforeStart);

        // Drill factory for Debits/Credits — both share the same window
        // [StartPeriod, EndPeriod]. The engine suppresses the factory call
        // entirely when BlankWhenZero and the cell is 0, so we only get
        // here on the active side of the split. Aggregate rows (SU/TO) get
        // empty AcctNums — return null (no drill on totals).
        DrillFactory activityDrill = (ctx, cellValue) =>
        {
            if (ctx.AcctNums.Count == 0) return null;

            return new DrillDownRef
            {
                AcctNums     = ctx.AcctNums,
                EntityIds    = ctx.EntityIds,
                BasisList    = ctx.BasisList,
                PeriodFrom   = startPeriod,
                PeriodTo     = endPeriod,
                DisplayLabel = ctx.DisplayLabel
            };
        };

        return new[]
        {
            // Starting balance as of (StartPeriod - 1). Accumulated.
            // Always renders a value (including 0) — starting balance is
            // a meaningful position even when it's zero.
            new ColumnSpec
            {
                Id       = ColStarting,
                Header   = $"Balance at {prevDisplay}",
                DataType = ColumnDataType.Currency,
                Width    = 140,
                Source   = new DataSource.GlStartingBalance(startPeriod)
            },

            // Hidden intermediate — signed net activity for [StartPeriod,
            // EndPeriod]. Feeds the DEBITS / CREDITS / ENDING derived columns.
            new ColumnSpec
            {
                Id     = ColNet,
                Header = string.Empty,
                Hidden = true,
                Source = new DataSource.GlActivity(startPeriod, endPeriod)
            },

            // Debits column — positive side of the net activity.
            new ColumnSpec
            {
                Id            = ColDebits,
                Header        = $"Debits {startDisplay}–{endDisplay}",
                DataType      = ColumnDataType.Currency,
                Width         = 140,
                BlankWhenZero = true,
                Derived       = acc => acc[ColNet] > 0m ? acc[ColNet] : 0m,
                Drill         = activityDrill
            },

            // Credits column — magnitude of the credit side of the net
            // activity. Sign-flipped so it renders as a positive number.
            new ColumnSpec
            {
                Id            = ColCredits,
                Header        = $"Credits {startDisplay}–{endDisplay}",
                DataType      = ColumnDataType.Currency,
                Width         = 140,
                BlankWhenZero = true,
                Derived       = acc => acc[ColNet] < 0m ? -acc[ColNet] : 0m,
                Drill         = activityDrill
            },

            // Ending balance — Starting + Net. Not drillable (it's a computed
            // position, not a time-bounded activity window). Always renders
            // a value (including 0) — ending balance is a meaningful position
            // even when it's zero.
            new ColumnSpec
            {
                Id       = ColEnding,
                Header   = $"Balance at {endDisplay}",
                DataType = ColumnDataType.Currency,
                Width    = 140,
                Derived  = acc => acc[ColStarting] + acc[ColNet]
            }
        };
    }

    private static ServiceResult<bool> ValidateOptions(ReportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.StartPeriod))
            return ServiceResult<bool>.Failure(
                "Start Period is required.", ErrorCode.ValidationError);
        if (string.IsNullOrWhiteSpace(options.EndPeriod))
            return ServiceResult<bool>.Failure(
                "End Period is required.", ErrorCode.ValidationError);
        if (FiscalCalendar.ComparePeriods(
                FiscalCalendar.ToMriPeriod(options.StartPeriod),
                FiscalCalendar.ToMriPeriod(options.EndPeriod)) > 0)
            return ServiceResult<bool>.Failure(
                "Start Period must be on or before End Period.",
                ErrorCode.ValidationError);
        if (options.Basis is null || options.Basis.Count == 0)
            return ServiceResult<bool>.Failure(
                "At least one Basis must be selected.", ErrorCode.ValidationError);
        return ServiceResult<bool>.Success(true);
    }
}
