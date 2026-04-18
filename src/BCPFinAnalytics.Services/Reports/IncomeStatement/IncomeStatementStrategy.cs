using System.Text.Json;
using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Interfaces;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Models.Format;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.Services.Format;
using BCPFinAnalytics.Services.Helpers;
using BCPFinAnalytics.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BCPFinAnalytics.Services.Reports.IncomeStatement;

/// <summary>
/// Simple Income Statement — Actual vs Budget with PTD and YTD columns.
/// Uses ResolvedAccountRange from BCPFinAnalytics.Common.Models.Format.
///
/// COLUMNS (8 total):
///   PTD Actual    — activity in selected period range
///   PTD Budget    — budget in selected period range
///   PTD Variance  — Actual - Budget
///   PTD Var %     — Variance / |Budget| × 100 (blank if budget = 0)
///   YTD Actual    — activity from BegYrPd to EndPeriod
///   YTD Budget    — budget from BegYrPd to EndPeriod
///   YTD Variance  — Actual - Budget
///   YTD Var %     — Variance / |Budget| × 100 (blank if budget = 0)
///
/// SIGN CONVENTION:
///   Same as other reports — ApplySign() from DEBCRED + O= flags for display.
///   Variance = signed Actual - signed Budget (so negative = unfavorable for income).
///   Variance % color coding from AppSettings.VarianceGreenAbove / VarianceRedBelow.
///
/// QUERIES (4 total, run in parallel):
///   PTD Actual: PERIOD BETWEEN @StartPeriod AND @EndPeriod, BASIS IN @ActualBasis, BALFOR='N'
///   PTD Budget: PERIOD BETWEEN @StartPeriod AND @EndPeriod, BASIS = @BudgetBasis, BALFOR='N'
///   YTD Actual: PERIOD BETWEEN @BegYrPd     AND @EndPeriod, BASIS IN @ActualBasis, BALFOR='N'
///   YTD Budget: PERIOD BETWEEN @BegYrPd     AND @EndPeriod, BASIS = @BudgetBasis,  BALFOR='N'
/// </summary>
public class IncomeStatementStrategy : IReportStrategy
{
    private readonly IFormatLoader _formatLoader;
    private readonly GlFilterBuilder _glFilterBuilder;
    private readonly IIncomeStatementRepository _repository;
    private readonly ILookupService _lookupService;
    private readonly AppSettings _appSettings;
    private readonly ILogger<IncomeStatementStrategy> _logger;

    // Column ID constants
    private const string ColPtdActual   = "PTD_ACTUAL";
    private const string ColPtdBudget   = "PTD_BUDGET";
    private const string ColPtdVar      = "PTD_VAR";
    private const string ColPtdVarPct   = "PTD_VAR_PCT";
    private const string ColYtdActual   = "YTD_ACTUAL";
    private const string ColYtdBudget   = "YTD_BUDGET";
    private const string ColYtdVar      = "YTD_VAR";
    private const string ColYtdVarPct   = "YTD_VAR_PCT";

    private sealed record AcctAggregate(
        string AcctName, string Type,
        decimal PtdActual, decimal PtdBudget,
        decimal YtdActual, decimal YtdBudget);

    public string ReportCode => "IS";
    public string ReportName => "Income Statement";

    public IncomeStatementStrategy(
        IFormatLoader formatLoader,
        GlFilterBuilder glFilterBuilder,
        IIncomeStatementRepository repository,
        ILookupService lookupService,
        IOptions<AppSettings> appSettings,
        ILogger<IncomeStatementStrategy> logger)
    {
        _formatLoader    = formatLoader;
        _glFilterBuilder = glFilterBuilder;
        _repository      = repository;
        _lookupService   = lookupService;
        _appSettings     = appSettings.Value;
        _logger          = logger;
    }

    /// <inheritdoc />
    public ReportOptionsConfig GetOptionsConfig() => new()
    {
        StartPeriodEnabled     = true,
        EndPeriodEnabled       = true,
        EndPeriodRequired      = true,
        BudgetEnabled          = true,
        SFTypeEnabled          = false,
        FormatEnabled          = true,
        BasisEnabled           = true,
        EntitySelectionEnabled = true,
        WholeDollarsEnabled    = true,
        IsCrosstab             = false
    };

    /// <inheritdoc />
    public async Task<ServiceResult<ReportResult>> ExecuteAsync(ReportOptions options)
    {
        _logger.LogInformation(
            "IncomeStatementStrategy.ExecuteAsync — DbKey={DbKey} " +
            "Start={Start} End={End} Budget={Budget} Options={Options}",
            options.DbKey, options.StartPeriod, options.EndPeriod,
            options.Budget, JsonSerializer.Serialize(options));

        try
        {
            // ── Step 1: Validate ───────────────────────────────────────────
            var validation = ValidateOptions(options);
            if (!validation.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    validation.ErrorMessage, validation.ErrorCode);

            // ── Step 2: Load format ────────────────────────────────────────
            var formatResult = await _formatLoader.LoadAsync(options.DbKey, options.Format);
            if (!formatResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    formatResult.ErrorMessage, formatResult.ErrorCode);
            var format = formatResult.Data!;

            // ── Step 3: Build GL query parameters ─────────────────────────
            var glParamsResult = await _glFilterBuilder.BuildAsync(
                options.DbKey, options, format.LedgCode);
            if (!glParamsResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    glParamsResult.ErrorMessage, glParamsResult.ErrorCode);
            var glParams = glParamsResult.Data!;

            // ── Step 4: Load GL info for account number formatting ─────────
            var glInfoResult = await _lookupService.GetGLsAsync(options.DbKey);
            if (!glInfoResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    glInfoResult.ErrorMessage, glInfoResult.ErrorCode);
            var glInfo = glInfoResult.Data!
                .FirstOrDefault(g => g.LedgCode == format.LedgCode)
                ?? new GLDto();

            // ── Step 5: Derive period values ───────────────────────────────
            var startPeriod = FiscalCalendar.ToMriPeriod(options.StartPeriod);
            var endPeriod   = glParams.EndPeriod;
            var begYrPd     = glParams.BegYrPd;

            _logger.LogInformation(
                "IncomeStatementStrategy — PTD={Start}–{End} YTD={BegYr}–{End2} Budget={Budget}",
                startPeriod, endPeriod, begYrPd, endPeriod, options.Budget);

            // ── Step 6: Run 4 queries in parallel ─────────────────────────
            var tPtdActual = _repository.GetActualAsync(
                options.DbKey, glParams, startPeriod, endPeriod);
            var tPtdBudget = _repository.GetBudgetAsync(
                options.DbKey, glParams, startPeriod, endPeriod, options.Budget);
            var tYtdActual = _repository.GetActualAsync(
                options.DbKey, glParams, begYrPd, endPeriod);
            var tYtdBudget = _repository.GetBudgetAsync(
                options.DbKey, glParams, begYrPd, endPeriod, options.Budget);

            await Task.WhenAll(tPtdActual, tPtdBudget, tYtdActual, tYtdBudget);

            // ── Step 7: Aggregate across entities ─────────────────────────
            var ptdActual = AggregateRaw(tPtdActual.Result);
            var ptdBudget = AggregateRaw(tPtdBudget.Result);
            var ytdActual = AggregateRaw(tYtdActual.Result);
            var ytdBudget = AggregateRaw(tYtdBudget.Result);

            // ── Step 8: Build combined account dictionary ──────────────────
            var allAccts = ptdActual.Keys
                .Union(ptdBudget.Keys)
                .Union(ytdActual.Keys)
                .Union(ytdBudget.Keys)
                .ToHashSet();

            // Build unified aggregate — get AcctName/Type from whichever source has it
            var combined = new Dictionary<string, AcctAggregate>();
            foreach (var acct in allAccts)
            {
                var meta = ptdActual.GetValueOrDefault(acct)
                        ?? ptdBudget.GetValueOrDefault(acct)
                        ?? ytdActual.GetValueOrDefault(acct)
                        ?? ytdBudget.GetValueOrDefault(acct);

                combined[acct] = new AcctAggregate(
                    meta!.AcctName, meta.Type,
                    ptdActual.GetValueOrDefault(acct)?.Balance ?? 0m,
                    ptdBudget.GetValueOrDefault(acct)?.Balance ?? 0m,
                    ytdActual.GetValueOrDefault(acct)?.Balance ?? 0m,
                    ytdBudget.GetValueOrDefault(acct)?.Balance ?? 0m
                );
            }

            _logger.LogInformation(
                "IncomeStatementStrategy — Accounts: {Count} combined", combined.Count);

            // ── Step 9: Walk format rows ───────────────────────────────────
            var reportRows   = new List<ReportRow>();
            var subtotalAccs = new Dictionary<int, (decimal PtdA, decimal PtdB, decimal YtdA, decimal YtdB)>();
            decimal grpPtdA = 0, grpPtdB = 0, grpYtdA = 0, grpYtdB = 0;

            foreach (var fmtRow in format.Rows)
            {
                switch (fmtRow.RowType)
                {
                    case FormatRowType.Blank:
                        reportRows.Add(BuildBlankRow());
                        break;

                    case FormatRowType.Title:
                        reportRows.Add(BuildTitleRow(fmtRow.Label));
                        break;

                    case FormatRowType.Range:
                        grpPtdA = grpPtdB = grpYtdA = grpYtdB = 0;
                        var raRows = BuildRangeRows(
                            fmtRow, combined, glParams, glInfo, options.WholeDollars,
                            startPeriod,
                            ref grpPtdA, ref grpPtdB, ref grpYtdA, ref grpYtdB);
                        reportRows.AddRange(raRows);
                        break;

                    case FormatRowType.Summary:
                        grpPtdA = grpPtdB = grpYtdA = grpYtdB = 0;
                        var smRow = BuildSummaryRow(
                            fmtRow, combined, options.WholeDollars,
                            ref grpPtdA, ref grpPtdB, ref grpYtdA, ref grpYtdB);
                        if (smRow != null) reportRows.Add(smRow);
                        break;

                    case FormatRowType.Subtotal:
                    {
                        // Display with sign applied, store raw for TO
                        var sPtdA = ApplySign(grpPtdA, fmtRow);
                        var sPtdB = ApplySign(grpPtdB, fmtRow);
                        var sPtdV = sPtdA - sPtdB;
                        var sPtdP = CalcVarPct(sPtdV, sPtdB);
                        var sYtdA = ApplySign(grpYtdA, fmtRow);
                        var sYtdB = ApplySign(grpYtdB, fmtRow);
                        var sYtdV = sYtdA - sYtdB;
                        var sYtdP = CalcVarPct(sYtdV, sYtdB);

                        subtotalAccs[fmtRow.SubtotId] = (grpPtdA, grpPtdB, grpYtdA, grpYtdB);

                        var suppress = fmtRow.Options.SuppressZeroSubtotal
                            && sPtdA == 0m && sPtdB == 0m && sYtdA == 0m && sYtdB == 0m;
                        if (!suppress)
                            reportRows.Add(BuildTotalRow(
                                fmtRow.Label, sPtdA, sPtdB, sPtdV, sPtdP,
                                sYtdA, sYtdB, sYtdV, sYtdP, options.WholeDollars));

                        grpPtdA = grpPtdB = grpYtdA = grpYtdB = 0;
                        break;
                    }

                    case FormatRowType.GrandTotal:
                    {
                        decimal gtPtdA = 0, gtPtdB = 0, gtYtdA = 0, gtYtdB = 0;
                        foreach (var (lo, hi) in fmtRow.SubtotRefs)
                            for (var id = lo; id <= hi; id++)
                                if (subtotalAccs.TryGetValue(id, out var st))
                                {
                                    gtPtdA += st.PtdA; gtPtdB += st.PtdB;
                                    gtYtdA += st.YtdA; gtYtdB += st.YtdB;
                                }

                        var gPtdA = ApplySign(gtPtdA, fmtRow);
                        var gPtdB = ApplySign(gtPtdB, fmtRow);
                        var gPtdV = gPtdA - gPtdB;
                        var gPtdP = CalcVarPct(gPtdV, gPtdB);
                        var gYtdA = ApplySign(gtYtdA, fmtRow);
                        var gYtdB = ApplySign(gtYtdB, fmtRow);
                        var gYtdV = gYtdA - gYtdB;
                        var gYtdP = CalcVarPct(gYtdV, gYtdB);

                        var suppress = fmtRow.Options.SuppressIfZero
                            && gPtdA == 0m && gPtdB == 0m && gYtdA == 0m && gYtdB == 0m;
                        if (!suppress)
                            reportRows.Add(BuildGrandTotalRow(
                                fmtRow.Label, gPtdA, gPtdB, gPtdV, gPtdP,
                                gYtdA, gYtdB, gYtdV, gYtdP,
                                options.WholeDollars));
                        break;
                    }
                }
            }

            // ── Step 10: Suppression ───────────────────────────────────────
            ReportPostProcessor.ApplySuppression(reportRows, options);

            // ── Step 11: Build column headers with period labels ───────────
            var ptdLabel = startPeriod == endPeriod
                ? FiscalCalendar.ToDisplayPeriod(endPeriod)
                : $"{FiscalCalendar.ToDisplayPeriod(startPeriod)}–{FiscalCalendar.ToDisplayPeriod(endPeriod)}";
            var ytdLabel = $"{FiscalCalendar.ToDisplayPeriod(begYrPd)}–{FiscalCalendar.ToDisplayPeriod(endPeriod)}";

            var columns = new List<ReportColumn>
            {
                new() { ColumnId = ColPtdActual, Header = $"PTD Actual\n{ptdLabel}",  Width = 120, DataType = ColumnDataType.Currency },
                new() { ColumnId = ColPtdBudget, Header = $"PTD Budget\n{ptdLabel}",  Width = 120, DataType = ColumnDataType.Currency },
                new() { ColumnId = ColPtdVar,    Header = "PTD Variance",              Width = 120, DataType = ColumnDataType.Currency },
                new() { ColumnId = ColPtdVarPct, Header = "PTD Var %",                Width = 80,  DataType = ColumnDataType.Percent   },
                new() { ColumnId = ColYtdActual, Header = $"YTD Actual\n{ytdLabel}",  Width = 120, DataType = ColumnDataType.Currency },
                new() { ColumnId = ColYtdBudget, Header = $"YTD Budget\n{ytdLabel}",  Width = 120, DataType = ColumnDataType.Currency },
                new() { ColumnId = ColYtdVar,    Header = "YTD Variance",              Width = 120, DataType = ColumnDataType.Currency },
                new() { ColumnId = ColYtdVarPct, Header = "YTD Var %",                Width = 80,  DataType = ColumnDataType.Percent   },
            };

            // ── Step 12: Build metadata ────────────────────────────────────
            var entityDisplay = options.SelectedIds.Count == 1
                ? options.SelectedIds[0]
                : $"{options.SelectedIds.Count} entities";

            var metadata = new ReportMetadata
            {
                ReportCode         = ReportCode,
                ReportTitle        = ReportName,
                EntityName         = entityDisplay,
                StartPeriod        = FiscalCalendar.ToDisplayPeriod(startPeriod),
                EndPeriod          = FiscalCalendar.ToDisplayPeriod(endPeriod),
                RunDate            = DateTime.Now,
                RunByUserId        = options.UserId,
                DbKey              = options.DbKey,
                WholeDollars       = options.WholeDollars,
                ShadeAlternateRows = options.ShadeAlternateRows,
            };

            var result = new ReportResult
            {
                Metadata = metadata,
                Columns  = columns,
                Rows     = reportRows
            };

            _logger.LogInformation(
                "IncomeStatementStrategy complete — {Rows} rows", reportRows.Count);

            return ServiceResult<ReportResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "IncomeStatementStrategy.ExecuteAsync failed — DbKey={DbKey}", options.DbKey);
            return ServiceResult<ReportResult>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Row builders
    // ══════════════════════════════════════════════════════════════

    private static ReportRow BuildBlankRow() => new()
    {
        RowType = RowType.SectionHeader, AccountCode = string.Empty, AccountName = string.Empty
    };

    private static ReportRow BuildTitleRow(string label) => new()
    {
        RowType = RowType.SectionHeader, AccountCode = string.Empty, AccountName = label
    };

    private ReportRow BuildTotalRow(
        string label,
        decimal ptdA, decimal ptdB, decimal ptdV, decimal? ptdP,
        decimal ytdA, decimal ytdB, decimal ytdV, decimal? ytdP,
        bool wholeDollars) => new()
    {
        RowType = RowType.Total, AccountCode = string.Empty, AccountName = label,
        Cells   = BuildCells(ptdA, ptdB, ptdV, ptdP, ytdA, ytdB, ytdV, ytdP, wholeDollars)
    };

    private ReportRow BuildGrandTotalRow(
        string label,
        decimal ptdA, decimal ptdB, decimal ptdV, decimal? ptdP,
        decimal ytdA, decimal ytdB, decimal ytdV, decimal? ytdP,
        bool wholeDollars) => new()
    {
        RowType = RowType.GrandTotal, AccountCode = string.Empty, AccountName = label,
        Cells   = BuildCells(ptdA, ptdB, ptdV, ptdP, ytdA, ytdB, ytdV, ytdP, wholeDollars)
    };

    private Dictionary<string, CellValue> BuildCells(
        decimal ptdA, decimal ptdB, decimal ptdV, decimal? ptdP,
        decimal ytdA, decimal ytdB, decimal ytdV, decimal? ytdP,
        bool wholeDollars)
    {
        decimal Round(decimal v) => wholeDollars
            ? Math.Round(v, 0, MidpointRounding.AwayFromZero) : v;

        var cells = new Dictionary<string, CellValue>
        {
            [ColPtdActual] = new(ptdA == 0m ? null : Round(ptdA)),
            [ColPtdBudget] = new(ptdB == 0m ? null : Round(ptdB)),
            [ColPtdVar]    = new(ptdV == 0m ? null : Round(ptdV)),
            [ColPtdVarPct] = BuildVarPctCell(ptdP),
            [ColYtdActual] = new(ytdA == 0m ? null : Round(ytdA)),
            [ColYtdBudget] = new(ytdB == 0m ? null : Round(ytdB)),
            [ColYtdVar]    = new(ytdV == 0m ? null : Round(ytdV)),
            [ColYtdVarPct] = BuildVarPctCell(ytdP),
        };
        return cells;
    }

    private CellValue BuildVarPctCell(decimal? pct)
    {
        if (!pct.HasValue) return CellValue.Empty;
        var cell = new CellValue(Math.Round(pct.Value, 1));

        // Color coding from AppSettings thresholds
        if ((double)pct.Value > _appSettings.VarianceGreenAbove)
            cell = cell with { CssClass = "variance-green" };
        else if ((double)pct.Value < _appSettings.VarianceRedBelow)
            cell = cell with { CssClass = "variance-red" };
        else
            cell = cell with { CssClass = "variance-yellow" };

        return cell;
    }

    private List<ReportRow> BuildRangeRows(
        FormatRow fmtRow,
        Dictionary<string, AcctAggregate> combined,
        GlQueryParameters glParams,
        GLDto glInfo,
        bool wholeDollars,
        string startPeriod,
        ref decimal grpPtdA, ref decimal grpPtdB,
        ref decimal grpYtdA, ref decimal grpYtdB)
    {
        var rows     = new List<ReportRow>();
        var matching = GetMatchingAccounts(fmtRow.Ranges, combined);

        foreach (var (acctNum, data) in matching)
        {
            var sPtdA = ApplySign(data.PtdActual, fmtRow);
            var sPtdB = ApplySign(data.PtdBudget, fmtRow);
            var sPtdV = sPtdA - sPtdB;
            var sPtdP = CalcVarPct(sPtdV, sPtdB);
            var sYtdA = ApplySign(data.YtdActual, fmtRow);
            var sYtdB = ApplySign(data.YtdBudget, fmtRow);
            var sYtdV = sYtdA - sYtdB;
            var sYtdP = CalcVarPct(sYtdV, sYtdB);

            // Accumulate raw for SU/TO math
            grpPtdA += data.PtdActual;
            grpPtdB += data.PtdBudget;
            grpYtdA += data.YtdActual;
            grpYtdB += data.YtdBudget;

            var formattedAcct = AccountNumberFormatter.Format(
                acctNum, glInfo.AcctLgt, glInfo.AcctDsp);

            // PTD drill-down: activity in the user-selected period range only
            var ptdDrill = new DrillDownRef
            {
                AcctNums      = new[] { acctNum },
                EntityIds     = glParams.EntityIds,
                PeriodFrom    = startPeriod,
                PeriodTo      = glParams.EndPeriod,
                BasisList     = glParams.BasisList,
                DisplayLabel  = $"{formattedAcct} · {data.AcctName} (PTD)",
                EndingBalance = sPtdA
            };

            // YTD drill-down: activity from beginning of year to end period
            var ytdDrill = new DrillDownRef
            {
                AcctNums      = new[] { acctNum },
                EntityIds     = glParams.EntityIds,
                PeriodFrom    = glParams.BegYrPd,
                PeriodTo      = glParams.EndPeriod,
                BasisList     = glParams.BasisList,
                DisplayLabel  = $"{formattedAcct} · {data.AcctName} (YTD)",
                EndingBalance = sYtdA
            };

            var cells = BuildCells(
                sPtdA, sPtdB, sPtdV, sPtdP,
                sYtdA, sYtdB, sYtdV, sYtdP,
                wholeDollars);

            // Wire separate drill-downs to PTD and YTD actual columns
            if (cells[ColPtdActual].Amount.HasValue)
                cells[ColPtdActual] = cells[ColPtdActual] with { DrillDown = ptdDrill };
            if (cells[ColYtdActual].Amount.HasValue)
                cells[ColYtdActual] = cells[ColYtdActual] with { DrillDown = ytdDrill };

            rows.Add(new ReportRow
            {
                RowType     = RowType.Detail,
                AccountCode = formattedAcct,
                AccountName = data.AcctName,
                Indent      = 1,
                Cells       = cells
            });
        }

        return rows;
    }

    private ReportRow? BuildSummaryRow(
        FormatRow fmtRow,
        Dictionary<string, AcctAggregate> combined,
        bool wholeDollars,
        ref decimal grpPtdA, ref decimal grpPtdB,
        ref decimal grpYtdA, ref decimal grpYtdB)
    {
        var matching = GetMatchingAccounts(fmtRow.Ranges, combined);
        if (!matching.Any()) return null;

        var rawPtdA = matching.Sum(a => a.Value.PtdActual);
        var rawPtdB = matching.Sum(a => a.Value.PtdBudget);
        var rawYtdA = matching.Sum(a => a.Value.YtdActual);
        var rawYtdB = matching.Sum(a => a.Value.YtdBudget);

        var sPtdA = ApplySign(rawPtdA, fmtRow);
        var sPtdB = ApplySign(rawPtdB, fmtRow);
        var sYtdA = ApplySign(rawYtdA, fmtRow);
        var sYtdB = ApplySign(rawYtdB, fmtRow);

        grpPtdA += rawPtdA;
        grpPtdB += rawPtdB;
        grpYtdA += rawYtdA;
        grpYtdB += rawYtdB;

        return new ReportRow
        {
            RowType     = RowType.Detail,
            AccountCode = string.Empty,
            AccountName = fmtRow.Label,
            Indent      = 1,
            Cells       = BuildCells(
                sPtdA, sPtdB, sPtdA - sPtdB, CalcVarPct(sPtdA - sPtdB, sPtdB),
                sYtdA, sYtdB, sYtdA - sYtdB, CalcVarPct(sYtdA - sYtdB, sYtdB),
                wholeDollars)
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private static decimal ApplySign(decimal amount, FormatRow fmtRow)
    {
        var result = amount;
        if (fmtRow.DebCred == "C")          result = -result;
        if (fmtRow.Options.FlipSign)        result = -result;
        if (fmtRow.Options.ReverseSign)     result = -result;
        return result;
    }

    /// <summary>
    /// Variance % = Variance / |Budget| × 100.
    /// Returns null if budget is 0 (avoid divide-by-zero).
    /// </summary>
    private static decimal? CalcVarPct(decimal variance, decimal budget)
    {
        if (budget == 0m) return null;
        return variance / Math.Abs(budget) * 100m;
    }

    private sealed record SimpleAcct(string AcctName, string Type, decimal Balance);

    private static Dictionary<string, SimpleAcct> AggregateRaw(
        IEnumerable<IncomeStatementRawRow> rows) =>
        rows.GroupBy(r => r.AcctNum)
            .ToDictionary(g => g.Key,
                g => new SimpleAcct(g.First().AcctName, g.First().Type, g.Sum(r => r.Amount)));



    private static IEnumerable<KeyValuePair<string, AcctAggregate>> GetMatchingAccounts(
        IReadOnlyList<ResolvedAccountRange> ranges,
        Dictionary<string, AcctAggregate> combined)
    {
        return combined.Where(kvp =>
        {
            var acct = kvp.Key;
            bool included = false;
            foreach (var range in ranges)
            {
                bool inRange =
                    string.Compare(acct, range.BegAcct, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    string.Compare(acct, range.EndAcct, StringComparison.OrdinalIgnoreCase) <= 0;

                if (range.IsExclusion && inRange) return false;
                if (!range.IsExclusion && inRange) included = true;
            }
            return included;
        }).OrderBy(kvp => kvp.Key);
    }

    private static ServiceResult<bool> ValidateOptions(ReportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.StartPeriod))
            return ServiceResult<bool>.Failure("Start Period is required.", ErrorCode.ValidationError);
        if (string.IsNullOrWhiteSpace(options.EndPeriod))
            return ServiceResult<bool>.Failure("End Period is required.", ErrorCode.ValidationError);
        if (string.IsNullOrWhiteSpace(options.Budget))
            return ServiceResult<bool>.Failure("Budget type is required.", ErrorCode.ValidationError);
        if (!options.Basis.Any())
            return ServiceResult<bool>.Failure("At least one Basis must be selected.", ErrorCode.ValidationError);
        if (!options.SelectedIds.Any())
            return ServiceResult<bool>.Failure("At least one Entity must be selected.", ErrorCode.ValidationError);
        return ServiceResult<bool>.Success(true);
    }
}
