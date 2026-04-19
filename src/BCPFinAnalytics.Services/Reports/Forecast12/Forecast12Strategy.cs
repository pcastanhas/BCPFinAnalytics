using System.Text.Json;
using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Interfaces;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Models.Format;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using BCPFinAnalytics.Services.Format;
using BCPFinAnalytics.Services.Helpers;
using BCPFinAnalytics.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Reports.Forecast12;

/// <summary>
/// 12-Month Forecast Report.
///
/// Covers the full fiscal year containing the user's EndPeriod.
/// Columns BegYrPd → EndPeriod   = actuals  from GLSUM
/// Columns EndPeriod+1 → FYEnd   = budgets  from BUDGETS
/// Column 15                      = total of all 12 months
///
/// Column headers: "MM/YYYY Actual" or "MM/YYYY Budget"
/// Budget column headers rendered with a different CSS class (light green).
///
/// COLUMNS: Account # | Description | [12 monthly cols] | Total
/// </summary>
public class Forecast12Strategy : IReportStrategy
{
    private readonly IFormatLoader _formatLoader;
    private readonly GlFilterBuilder _glFilterBuilder;
    private readonly IGlDataRepository _glData;
    private readonly IBudgetDataRepository _budgetData;
    private readonly ILookupService _lookupService;
    private readonly ILogger<Forecast12Strategy> _logger;

    private const string ColPrefix = "FC_";
    private const string ColTotal  = "FC_TOTAL";

    private sealed record AcctAggregate(
        string AcctName,
        string Type,
        Dictionary<string, decimal> Monthly);  // keyed by YYYYMM

    public string ReportCode => "FC12";
    public string ReportName => "12 Month Forecast";

    public Forecast12Strategy(
        IFormatLoader formatLoader,
        GlFilterBuilder glFilterBuilder,
        IGlDataRepository glData,
        IBudgetDataRepository budgetData,
        ILookupService lookupService,
        ILogger<Forecast12Strategy> logger)
    {
        _formatLoader    = formatLoader;
        _glFilterBuilder = glFilterBuilder;
        _glData          = glData;
        _budgetData      = budgetData;
        _lookupService   = lookupService;
        _logger          = logger;
    }

    /// <inheritdoc />
    public ReportOptionsConfig GetOptionsConfig() => new()
    {
        StartPeriodEnabled     = false,
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
            "Forecast12Strategy.ExecuteAsync — DbKey={DbKey} End={End} Budget={Budget}",
            options.DbKey, options.EndPeriod, options.Budget);

        try
        {
            // ── Validate ───────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(options.EndPeriod))
                return ServiceResult<ReportResult>.Failure(
                    "End Period is required.", ErrorCode.ValidationError);
            if (string.IsNullOrWhiteSpace(options.Budget))
                return ServiceResult<ReportResult>.Failure(
                    "Budget type is required.", ErrorCode.ValidationError);
            if (!options.Basis.Any())
                return ServiceResult<ReportResult>.Failure(
                    "At least one Basis must be selected.", ErrorCode.ValidationError);
            if (!options.SelectedIds.Any())
                return ServiceResult<ReportResult>.Failure(
                    "At least one Entity must be selected.", ErrorCode.ValidationError);

            // ── Load format ────────────────────────────────────────────────
            var formatResult = await _formatLoader.LoadAsync(options.DbKey, options.Format);
            if (!formatResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    formatResult.ErrorMessage, formatResult.ErrorCode);
            var format = formatResult.Data!;

            // ── Build GL parameters ────────────────────────────────────────
            var glParamsResult = await _glFilterBuilder.BuildAsync(
                options.DbKey, options, format.LedgCode);
            if (!glParamsResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    glParamsResult.ErrorMessage, glParamsResult.ErrorCode);
            var glParams = glParamsResult.Data!;

            // ── GL info for account number formatting ──────────────────────
            var glInfoResult = await _lookupService.GetGLsAsync(options.DbKey);
            if (!glInfoResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    glInfoResult.ErrorMessage, glInfoResult.ErrorCode);
            var glInfo = glInfoResult.Data!
                .FirstOrDefault(g => g.LedgCode == format.LedgCode)
                ?? new GLDto();

            // ── Build fiscal year period list ──────────────────────────────
            // 12 months starting from BegYrPd (fiscal year start)
            var endPeriod  = glParams.EndPeriod;
            var begYrPd    = glParams.BegYrPd;
            var allPeriods = BuildFiscalYearPeriods(begYrPd);   // 12 periods in order

            // Split into actual and budget ranges
            var actualPeriods = allPeriods
                .Where(p => string.Compare(p, endPeriod, StringComparison.Ordinal) <= 0)
                .ToList();
            var budgetPeriods = allPeriods
                .Where(p => string.Compare(p, endPeriod, StringComparison.Ordinal) > 0)
                .ToList();

            _logger.LogInformation(
                "Forecast12Strategy — FY={Beg} ActualPeriods=[{AP}] BudgetPeriods=[{BP}]",
                begYrPd,
                string.Join(",", actualPeriods),
                string.Join(",", budgetPeriods));

            // ── Fetch actuals and budgets in parallel via primitives ───────
            // actualPeriods: GetGlActivity per period (BALFOR='N' inside)
            // budgetPeriods: GetBudgetAmount per period (basis-filtered inside)
            // Either list may be empty (first or last month of fiscal year).
            // All tasks flattened into one Task.WhenAll so they run concurrently.
            var actualTasks = actualPeriods
                .Select(p => _glData.GetGlActivityAsync(
                    options.DbKey, p, p,
                    glParams.LedgLo, glParams.LedgHi,
                    glParams.EntityIds, glParams.BasisList))
                .ToList();

            var budgetTasks = budgetPeriods
                .Select(p => _budgetData.GetBudgetAmountAsync(
                    options.DbKey, p, p, options.Budget,
                    glParams.LedgLo, glParams.LedgHi,
                    glParams.EntityIds, glParams.BasisList))
                .ToList();

            await Task.WhenAll(actualTasks.Concat(budgetTasks));

            var actualResults = actualTasks.Select(t => t.Result).ToList();
            var budgetResults = budgetTasks.Select(t => t.Result).ToList();

            // ── Merge into unified pivot ───────────────────────────────────
            // Both result lists parallel their period lists: results[i]
            // corresponds to periods[i]. Merge pushes each period's data
            // into combined[acct].Monthly[period].
            var combined = new Dictionary<string, AcctAggregate>();

            void MergeInto(
                IReadOnlyList<string> periods,
                IReadOnlyList<IReadOnlyDictionary<string, AccountAmount>> results)
            {
                for (var i = 0; i < periods.Count; i++)
                {
                    var period = periods[i];
                    foreach (var (acct, data) in results[i])
                    {
                        if (!combined.TryGetValue(acct, out var agg))
                        {
                            agg = new AcctAggregate(
                                data.AcctName, data.Type, NewMonthlyDict(allPeriods));
                            combined[acct] = agg;
                        }
                        agg.Monthly[period] = data.Amount;
                    }
                }
            }

            MergeInto(actualPeriods, actualResults);
            MergeInto(budgetPeriods, budgetResults);

            _logger.LogInformation(
                "Forecast12Strategy — {Count} accounts after merge", combined.Count);

            // ── Walk format rows ───────────────────────────────────────────
            var reportRows   = new List<ReportRow>();
            var subtotalAccs = new Dictionary<int, Dictionary<string, decimal>>();
            var grpMonthly   = NewMonthlyDict(allPeriods);

            foreach (var fmtRow in format.Rows)
            {
                switch (fmtRow.RowType)
                {
                    case FormatRowType.Blank:
                        reportRows.Add(new ReportRow
                        {
                            RowType     = RowType.SectionHeader,
                            AccountCode = string.Empty,
                            AccountName = string.Empty
                        });
                        break;

                    case FormatRowType.Title:
                        reportRows.Add(new ReportRow
                        {
                            RowType     = RowType.SectionHeader,
                            AccountCode = string.Empty,
                            AccountName = fmtRow.Label
                        });
                        break;

                    case FormatRowType.Range:
                        grpMonthly = NewMonthlyDict(allPeriods);
                        var raRows = BuildRangeRows(
                            fmtRow, combined, glParams, glInfo,
                            options.WholeDollars, allPeriods,
                            actualPeriods, budgetPeriods,
                            grpMonthly, options.Budget);
                        reportRows.AddRange(raRows);
                        break;

                    case FormatRowType.Summary:
                        grpMonthly = NewMonthlyDict(allPeriods);
                        var smRow  = BuildSummaryRow(
                            fmtRow, combined, options.WholeDollars,
                            allPeriods, grpMonthly);
                        if (smRow != null) reportRows.Add(smRow);
                        break;

                    case FormatRowType.Subtotal:
                    {
                        var suMonthly = allPeriods.ToDictionary(
                            p => p,
                            p => fmtRow.Options.ReverseAmount ? -grpMonthly[p] : grpMonthly[p]);

                        subtotalAccs[fmtRow.SubtotId] = suMonthly;

                        var allZero  = suMonthly.Values.All(v => v == 0m);
                        var suppress = fmtRow.Options.SuppressZeroSubtotal && allZero;
                        if (!suppress)
                            reportRows.Add(BuildTotalRow(
                                fmtRow.Label, suMonthly, options.WholeDollars,
                                allPeriods, RowType.Total));

                        grpMonthly = NewMonthlyDict(allPeriods);
                        break;
                    }

                    case FormatRowType.GrandTotal:
                    {
                        var gtMonthly = NewMonthlyDict(allPeriods);
                        foreach (var (lo, hi) in fmtRow.SubtotRefs)
                            for (var id = lo; id <= hi; id++)
                                if (subtotalAccs.TryGetValue(id, out var st))
                                    foreach (var p in allPeriods)
                                        gtMonthly[p] += st[p];

                        var toMonthly = allPeriods.ToDictionary(
                            p => p,
                            p => fmtRow.Options.ReverseAmount ? -gtMonthly[p] : gtMonthly[p]);

                        var allZero  = toMonthly.Values.All(v => v == 0m);
                        var suppress = fmtRow.Options.SuppressIfZero && allZero;
                        if (!suppress)
                            reportRows.Add(BuildTotalRow(
                                fmtRow.Label, toMonthly, options.WholeDollars,
                                allPeriods, RowType.GrandTotal));
                        break;
                    }
                }
            }

            // ── Suppression ────────────────────────────────────────────────
            ReportPostProcessor.ApplySuppression(reportRows, options);

            // ── Build columns ──────────────────────────────────────────────
            // 12 monthly columns + 1 total column
            var budgetSet = new HashSet<string>(budgetPeriods);
            var columns   = new List<ReportColumn>();

            foreach (var p in allPeriods)
            {
                var isBudget = budgetSet.Contains(p);
                columns.Add(new ReportColumn
                {
                    ColumnId = ColPrefix + p,
                    Header   = $"{FiscalCalendar.ToDisplayPeriod(p)}\n{(isBudget ? "Budget" : "Actual")}",
                    Width    = 110,
                    DataType = ColumnDataType.Currency,
                    CssClass = isBudget ? "col-header-budget" : string.Empty
                });
            }

            columns.Add(new ReportColumn
            {
                ColumnId = ColTotal,
                Header   = "Total",
                Width    = 120,
                DataType = ColumnDataType.Currency,
                CssClass = "col-header-total"
            });

            // ── Metadata ───────────────────────────────────────────────────
            var entityDisplay = options.SelectedIds.Count == 1
                ? options.SelectedIds[0]
                : $"{options.SelectedIds.Count} entities";

            var metadata = new ReportMetadata
            {
                ReportCode         = ReportCode,
                ReportTitle        = ReportName,
                EntityName         = entityDisplay,
                StartPeriod        = FiscalCalendar.ToDisplayPeriod(begYrPd),
                EndPeriod          = FiscalCalendar.ToDisplayPeriod(allPeriods.Last()),
                RunDate            = DateTime.Now,
                RunByUserId        = options.UserId,
                DbKey              = options.DbKey,
                WholeDollars       = options.WholeDollars,
                ShadeAlternateRows = options.ShadeAlternateRows,
            };

            return ServiceResult<ReportResult>.Success(new ReportResult
            {
                Metadata = metadata,
                Columns  = columns,
                Rows     = reportRows
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Forecast12Strategy.ExecuteAsync failed — DbKey={DbKey}", options.DbKey);
            return ServiceResult<ReportResult>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Period helpers
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds 12 consecutive periods starting from begYrPd (fiscal year order).
    /// e.g. begYrPd=202501 → [202501, 202502, ..., 202512]
    ///      begYrPd=202407 → [202407, 202408, ..., 202506]
    /// </summary>
    private static IReadOnlyList<string> BuildFiscalYearPeriods(string begYrPd)
    {
        var periods = new List<string>(12);
        var current = begYrPd;
        for (int i = 0; i < 12; i++)
        {
            periods.Add(current);
            current = FiscalCalendar.NextPeriod(current);
        }
        return periods;
    }

    // ══════════════════════════════════════════════════════════════
    //  Aggregation
    // ══════════════════════════════════════════════════════════════

    private static Dictionary<string, decimal> NewMonthlyDict(
        IReadOnlyList<string> periods) =>
        periods.ToDictionary(p => p, _ => 0m);

    // ══════════════════════════════════════════════════════════════
    //  Row builders
    // ══════════════════════════════════════════════════════════════

    private ReportRow BuildTotalRow(
        string label,
        Dictionary<string, decimal> monthly,
        bool wholeDollars,
        IReadOnlyList<string> allPeriods,
        RowType rowType) => new()
    {
        RowType     = rowType,
        AccountCode = string.Empty,
        AccountName = label,
        Cells       = BuildCells(monthly, wholeDollars, allPeriods,
                          null, null, null, null)
    };

    private List<ReportRow> BuildRangeRows(
        FormatRow fmtRow,
        Dictionary<string, AcctAggregate> combined,
        GlQueryParameters glParams,
        GLDto glInfo,
        bool wholeDollars,
        IReadOnlyList<string> allPeriods,
        IReadOnlyList<string> actualPeriods,
        IReadOnlyList<string> budgetPeriods,
        Dictionary<string, decimal> grpMonthly,
        string budgetType)
    {
        var rows     = new List<ReportRow>();
        var matching = GetMatchingAccounts(fmtRow.Ranges, combined);
        var budSet   = new HashSet<string>(budgetPeriods);

        foreach (var (acctNum, data) in matching)
        {
            var signedMonthly = allPeriods.ToDictionary(
                p => p,
                p => ApplySign(data.Monthly[p], fmtRow));

            foreach (var p in allPeriods)
                grpMonthly[p] += signedMonthly[p];

            var formattedAcct = AccountNumberFormatter.Format(
                acctNum, glInfo.AcctLgt, glInfo.AcctDsp);

            var cells = BuildCells(signedMonthly, wholeDollars, allPeriods,
                acctNum, glParams, budSet, budgetType);

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
        IReadOnlyList<string> allPeriods,
        Dictionary<string, decimal> grpMonthly)
    {
        var matching = GetMatchingAccounts(fmtRow.Ranges, combined);
        if (!matching.Any()) return null;

        var signedMonthly = allPeriods.ToDictionary(
            p => p,
            p => ApplySign(matching.Sum(a => a.Value.Monthly[p]), fmtRow));

        foreach (var p in allPeriods)
            grpMonthly[p] += signedMonthly[p];

        return new ReportRow
        {
            RowType     = RowType.Detail,
            AccountCode = string.Empty,
            AccountName = fmtRow.Label,
            Indent      = 1,
            Cells       = BuildCells(signedMonthly, wholeDollars, allPeriods,
                              null, null, null, null)
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Cell builder
    // ══════════════════════════════════════════════════════════════

    private Dictionary<string, CellValue> BuildCells(
        Dictionary<string, decimal> monthly,
        bool wholeDollars,
        IReadOnlyList<string> allPeriods,
        string? acctNum,
        GlQueryParameters? glParams,
        HashSet<string>? budgetPeriods,
        string? budgetType)
    {
        decimal Round(decimal v) => wholeDollars
            ? Math.Round(v, 0, MidpointRounding.AwayFromZero) : v;

        var cells = new Dictionary<string, CellValue>();
        decimal total = 0m;

        foreach (var period in allPeriods)
        {
            var amount = Round(monthly[period]);
            total += amount;
            var colId    = ColPrefix + period;
            var isBudget = budgetPeriods?.Contains(period) ?? false;

            if (acctNum != null && glParams != null)
            {
                if (isBudget && budgetType != null)
                {
                    // Budget drill-down
                    var budDrill = new BudgetDrillDownRef
                    {
                        AcctNums     = new[] { acctNum },
                        EntityIds    = glParams.EntityIds,
                        PeriodFrom   = period,
                        PeriodTo     = period,
                        BudgetType   = budgetType,
                        DisplayLabel = $"{FiscalCalendar.ToDisplayPeriod(period)} · Budget"
                    };
                    cells[colId] = new CellValue(amount) { BudgetDrillDown = budDrill };
                }
                else
                {
                    // GL actual drill-down
                    var actDrill = new DrillDownRef
                    {
                        AcctNums      = new[] { acctNum },
                        EntityIds     = glParams.EntityIds,
                        PeriodFrom    = period,
                        PeriodTo      = period,
                        BasisList     = glParams.BasisList,
                        DisplayLabel  = $"{FiscalCalendar.ToDisplayPeriod(period)} · Actual"
                    };
                    cells[colId] = new CellValue(amount, actDrill);
                }
            }
            else
            {
                cells[colId] = new CellValue(amount == 0m ? null : amount);
            }
        }

        // Total column — non-drillable
        cells[ColTotal] = new CellValue(Round(total) == 0m ? null : Round(total));

        return cells;
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private static decimal ApplySign(decimal amount, FormatRow fmtRow)
    {
        var result = amount;
        if (fmtRow.DebCred == "C")        result = -result;
        if (fmtRow.Options.ReverseAmount) result = -result;
        return result;
    }

    private static IEnumerable<KeyValuePair<string, AcctAggregate>> GetMatchingAccounts(
        IReadOnlyList<ResolvedAccountRange> ranges,
        Dictionary<string, AcctAggregate> combined)
    {
        return combined.Where(kvp =>
        {
            var acct     = kvp.Key;
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
}
