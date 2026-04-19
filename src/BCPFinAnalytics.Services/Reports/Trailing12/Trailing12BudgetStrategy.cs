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

namespace BCPFinAnalytics.Services.Reports.Trailing12;

/// <summary>
/// Trailing 12 Month Budget Report.
///
/// Identical layout to Trailing12Strategy but columns show budget amounts
/// from the BUDGETS table instead of actuals from GLSUM.
///
/// COLUMNS (14 total):
///   Account # | Description | Month-0 Budget | Month-1 Budget | ... | Month-11 Budget
///   where Month-0 = EndPeriod, Month-1 = EndPeriod-1, ..., Month-11 = EndPeriod-11
///
/// Budget drill-down on each cell opens BudgetDetailDialog.
/// </summary>
public class Trailing12BudgetStrategy : IReportStrategy
{
    private readonly IFormatLoader _formatLoader;
    private readonly GlFilterBuilder _glFilterBuilder;
    private readonly ITrailing12Repository _repository;
    private readonly ILookupService _lookupService;
    private readonly ILogger<Trailing12BudgetStrategy> _logger;

    private const string ColPrefix = "M_";
    private const string ColTotal  = "M_TOTAL";

    private sealed record AcctAggregate(
        string AcctName,
        string Type,
        Dictionary<string, decimal> Monthly);

    public string ReportCode => "T12BUD";
    public string ReportName => "Trailing 12 Month Budget";

    public Trailing12BudgetStrategy(
        IFormatLoader formatLoader,
        GlFilterBuilder glFilterBuilder,
        ITrailing12Repository repository,
        ILookupService lookupService,
        ILogger<Trailing12BudgetStrategy> logger)
    {
        _formatLoader    = formatLoader;
        _glFilterBuilder = glFilterBuilder;
        _repository      = repository;
        _lookupService   = lookupService;
        _logger          = logger;
    }

    /// <inheritdoc />
    public ReportOptionsConfig GetOptionsConfig() => new()
    {
        StartPeriodEnabled     = false,
        EndPeriodEnabled       = true,
        EndPeriodRequired      = true,
        BudgetEnabled          = true,    // ← required for T12BUD
        SFTypeEnabled          = false,
        FormatEnabled          = true,
        BasisEnabled           = false,   // ← no basis for budget
        EntitySelectionEnabled = true,
        WholeDollarsEnabled    = true,
        IsCrosstab             = false
    };

    /// <inheritdoc />
    public async Task<ServiceResult<ReportResult>> ExecuteAsync(ReportOptions options)
    {
        _logger.LogInformation(
            "Trailing12BudgetStrategy.ExecuteAsync — DbKey={DbKey} End={End} Budget={Budget}",
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
            if (!options.SelectedIds.Any())
                return ServiceResult<ReportResult>.Failure(
                    "At least one Entity must be selected.", ErrorCode.ValidationError);

            // ── Load format ────────────────────────────────────────────────
            var formatResult = await _formatLoader.LoadAsync(options.DbKey, options.Format);
            if (!formatResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    formatResult.ErrorMessage, formatResult.ErrorCode);
            var format = formatResult.Data!;

            // ── Build GL params (for ledger range + entity list) ───────────
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

            // ── Build 12 period list ───────────────────────────────────────
            var endPeriod = glParams.EndPeriod;
            var periods   = BuildPeriodList(endPeriod);

            _logger.LogInformation(
                "Trailing12BudgetStrategy — EndPeriod={End} Budget={Budget} Periods=[{Periods}]",
                endPeriod, options.Budget, string.Join(",", periods));

            // ── Fetch budget data from BUDGETS table ───────────────────────
            var rawRows = await _repository.GetBudgetAsync(
                options.DbKey, glParams, periods, options.Budget);

            // ── Aggregate and pivot ────────────────────────────────────────
            var combined = AggregateAndPivot(rawRows, periods);

            _logger.LogInformation(
                "Trailing12BudgetStrategy — {Count} accounts after pivot", combined.Count);

            // ── Walk format rows ───────────────────────────────────────────
            var reportRows   = new List<ReportRow>();
            var subtotalAccs = new Dictionary<int, Dictionary<string, decimal>>();
            var grpMonthly   = NewMonthlyDict(periods);

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
                        grpMonthly = NewMonthlyDict(periods);
                        var raRows = BuildRangeRows(
                            fmtRow, combined, glParams, glInfo,
                            options.WholeDollars, periods, grpMonthly,
                            options.Budget);
                        reportRows.AddRange(raRows);
                        break;

                    case FormatRowType.Summary:
                        grpMonthly = NewMonthlyDict(periods);
                        var smRow  = BuildSummaryRow(
                            fmtRow, combined, options.WholeDollars, periods, grpMonthly);
                        if (smRow != null) reportRows.Add(smRow);
                        break;

                    case FormatRowType.Subtotal:
                    {
                        var suMonthly = periods.ToDictionary(
                            p => p,
                            p => fmtRow.Options.ReverseAmount ? -grpMonthly[p] : grpMonthly[p]);

                        subtotalAccs[fmtRow.SubtotId] = suMonthly;

                        var allZero  = suMonthly.Values.All(v => v == 0m);
                        var suppress = fmtRow.Options.SuppressZeroSubtotal && allZero;
                        if (!suppress)
                            reportRows.Add(BuildTotalRow(
                                fmtRow.Label, suMonthly, options.WholeDollars,
                                periods, RowType.Total));

                        grpMonthly = NewMonthlyDict(periods);
                        break;
                    }

                    case FormatRowType.GrandTotal:
                    {
                        var gtMonthly = NewMonthlyDict(periods);
                        foreach (var (lo, hi) in fmtRow.SubtotRefs)
                            for (var id = lo; id <= hi; id++)
                                if (subtotalAccs.TryGetValue(id, out var st))
                                    foreach (var p in periods)
                                        gtMonthly[p] += st[p];

                        var toMonthly = periods.ToDictionary(
                            p => p,
                            p => fmtRow.Options.ReverseAmount ? -gtMonthly[p] : gtMonthly[p]);

                        var allZero  = toMonthly.Values.All(v => v == 0m);
                        var suppress = fmtRow.Options.SuppressIfZero && allZero;
                        if (!suppress)
                            reportRows.Add(BuildTotalRow(
                                fmtRow.Label, toMonthly, options.WholeDollars,
                                periods, RowType.GrandTotal));
                        break;
                    }
                }
            }

            // ── Suppression ────────────────────────────────────────────────
            ReportPostProcessor.ApplySuppression(reportRows, options);

            // ── Columns ────────────────────────────────────────────────────
            var columns = periods
                .Select(p => new ReportColumn
                {
                    ColumnId = ColPrefix + p,
                    Header   = FiscalCalendar.ToDisplayPeriod(p),
                    Width    = 110,
                    DataType = ColumnDataType.Currency
                })
                .ToList();

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
                StartPeriod        = FiscalCalendar.ToDisplayPeriod(periods.Last()),
                EndPeriod          = FiscalCalendar.ToDisplayPeriod(endPeriod),
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
                "Trailing12BudgetStrategy.ExecuteAsync failed — DbKey={DbKey}", options.DbKey);
            return ServiceResult<ReportResult>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers — identical to Trailing12Strategy
    // ══════════════════════════════════════════════════════════════

    private static IReadOnlyList<string> BuildPeriodList(string endPeriod)
    {
        var periods = new List<string>(12);
        var current = endPeriod;
        for (int i = 0; i < 12; i++)
        {
            periods.Add(current);
            current = FiscalCalendar.PreviousPeriod(current);
        }
        return periods;
    }

    private static Dictionary<string, AcctAggregate> AggregateAndPivot(
        IEnumerable<Trailing12RawRow> rows,
        IReadOnlyList<string> periods)
    {
        var dict = new Dictionary<string, AcctAggregate>();
        foreach (var row in rows)
        {
            if (!dict.TryGetValue(row.AcctNum, out var agg))
            {
                agg = new AcctAggregate(row.AcctName, row.Type, NewMonthlyDict(periods));
                dict[row.AcctNum] = agg;
            }
            if (agg.Monthly.ContainsKey(row.Period))
                agg.Monthly[row.Period] += row.Amount;
        }
        return dict;
    }

    private static Dictionary<string, decimal> NewMonthlyDict(
        IReadOnlyList<string> periods) =>
        periods.ToDictionary(p => p, _ => 0m);

    private static decimal ApplySign(decimal amount, FormatRow fmtRow)
    {
        var result = amount;
        if (fmtRow.DebCred == "C")        result = -result;
        if (fmtRow.Options.ReverseAmount) result = -result;
        return result;
    }

    private ReportRow BuildTotalRow(
        string label,
        Dictionary<string, decimal> monthly,
        bool wholeDollars,
        IReadOnlyList<string> periods,
        RowType rowType) => new()
    {
        RowType     = rowType,
        AccountCode = string.Empty,
        AccountName = label,
        Cells       = BuildCells(monthly, wholeDollars, periods, null, null, null)
    };

    private List<ReportRow> BuildRangeRows(
        FormatRow fmtRow,
        Dictionary<string, AcctAggregate> combined,
        GlQueryParameters glParams,
        GLDto glInfo,
        bool wholeDollars,
        IReadOnlyList<string> periods,
        Dictionary<string, decimal> grpMonthly,
        string budgetType)
    {
        var rows     = new List<ReportRow>();
        var matching = GetMatchingAccounts(fmtRow.Ranges, combined);

        foreach (var (acctNum, data) in matching)
        {
            var signedMonthly = periods.ToDictionary(
                p => p,
                p => ApplySign(data.Monthly[p], fmtRow));

            foreach (var p in periods)
                grpMonthly[p] += signedMonthly[p];

            var formattedAcct = AccountNumberFormatter.Format(
                acctNum, glInfo.AcctLgt, glInfo.AcctDsp);

            var cells = BuildCells(signedMonthly, wholeDollars, periods,
                acctNum, glParams, budgetType);

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
        IReadOnlyList<string> periods,
        Dictionary<string, decimal> grpMonthly)
    {
        var matching = GetMatchingAccounts(fmtRow.Ranges, combined);
        if (!matching.Any()) return null;

        var signedMonthly = periods.ToDictionary(
            p => p,
            p => ApplySign(matching.Sum(a => a.Value.Monthly[p]), fmtRow));

        foreach (var p in periods)
            grpMonthly[p] += signedMonthly[p];

        return new ReportRow
        {
            RowType     = RowType.Detail,
            AccountCode = string.Empty,
            AccountName = fmtRow.Label,
            Indent      = 1,
            Cells       = BuildCells(signedMonthly, wholeDollars, periods, null, null, null)
        };
    }

    private Dictionary<string, CellValue> BuildCells(
        Dictionary<string, decimal> monthly,
        bool wholeDollars,
        IReadOnlyList<string> periods,
        string? acctNum,
        GlQueryParameters? glParams,
        string? budgetType)
    {
        decimal Round(decimal v) => wholeDollars
            ? Math.Round(v, 0, MidpointRounding.AwayFromZero) : v;

        var cells = new Dictionary<string, CellValue>();
        decimal total = 0m;

        foreach (var period in periods)
        {
            var amount = Round(monthly[period]);
            total += amount;
            var colId  = ColPrefix + period;

            if (acctNum != null && glParams != null && budgetType != null)
            {
                // Budget drill-down cell
                var drill = new BudgetDrillDownRef
                {
                    AcctNums     = new[] { acctNum },
                    EntityIds    = glParams.EntityIds,
                    PeriodFrom   = period,
                    PeriodTo     = period,
                    BudgetType   = budgetType,
                    DisplayLabel = $"{FiscalCalendar.ToDisplayPeriod(period)} · Budget"
                };
                cells[colId] = new CellValue(amount) { BudgetDrillDown = drill };
            }
            else
            {
                cells[colId] = new CellValue(amount == 0m ? null : amount);
            }
        }

        // Total column — non-drillable
        var roundedTotal = Round(total);
        cells[ColTotal] = new CellValue(roundedTotal == 0m ? null : roundedTotal);

        return cells;
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
