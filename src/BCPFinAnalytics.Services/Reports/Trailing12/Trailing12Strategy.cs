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
/// Trailing 12 Month Income Statement.
///
/// COLUMNS (14 total):
///   Account # | Description | Month-0 | Month-1 | ... | Month-11
///   where Month-0 = EndPeriod, Month-1 = EndPeriod-1, ..., Month-11 = EndPeriod-11
///
/// ONE query fetches all 12 periods (PERIOD IN @Periods, BALFOR='N').
/// Strategy pivots results by period into a dictionary keyed by ACCTNUM.
///
/// SIGN CONVENTION: same as IS report.
///   O=R (ReverseAmount) negates displayed amount.
///   O=^ (ReverseVariance) has no effect here (no variance column).
///   O=R in ApplySign only.
///
/// DRILL-DOWN: each cell opens GL detail for that account + single period.
/// </summary>
public class Trailing12Strategy : IReportStrategy
{
    private readonly IFormatLoader _formatLoader;
    private readonly GlFilterBuilder _glFilterBuilder;
    private readonly ITrailing12Repository _repository;
    private readonly ILookupService _lookupService;
    private readonly ILogger<Trailing12Strategy> _logger;

    // Column ID prefix — actual column IDs are "M_YYYYMM"
    private const string ColPrefix = "M_";
    private const string ColTotal  = "M_TOTAL";

    private sealed record AcctAggregate(
        string AcctName,
        string Type,
        // Monthly amounts keyed by YYYYMM period
        Dictionary<string, decimal> Monthly);

    public string ReportCode => "T12";
    public string ReportName => "Trailing 12 Month";

    public Trailing12Strategy(
        IFormatLoader formatLoader,
        GlFilterBuilder glFilterBuilder,
        ITrailing12Repository repository,
        ILookupService lookupService,
        ILogger<Trailing12Strategy> logger)
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
        StartPeriodEnabled     = false,   // T12 only needs end period
        EndPeriodEnabled       = true,
        EndPeriodRequired      = true,
        BudgetEnabled          = false,   // No budget on T12
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
            "Trailing12Strategy.ExecuteAsync — DbKey={DbKey} End={End} Options={Options}",
            options.DbKey, options.EndPeriod, JsonSerializer.Serialize(options));

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

            // ── Step 5: Build the 12 period list ──────────────────────────
            var endPeriod = glParams.EndPeriod;
            var periods   = BuildPeriodList(endPeriod); // [endPeriod, endPeriod-1, ..., endPeriod-11]

            _logger.LogInformation(
                "Trailing12Strategy — EndPeriod={End} Periods=[{Periods}]",
                endPeriod, string.Join(",", periods));

            // ── Step 6: Fetch all 12 months in one query ───────────────────
            var rawRows = await _repository.GetActivityAsync(
                options.DbKey, glParams, periods);

            // ── Step 7: Aggregate across entities, pivot by period ─────────
            var combined = AggregateAndPivot(rawRows, periods);

            _logger.LogInformation(
                "Trailing12Strategy — {Count} accounts after pivot", combined.Count);

            // ── Step 8: Walk format rows ───────────────────────────────────
            var reportRows   = new List<ReportRow>();
            var subtotalAccs = new Dictionary<int, Dictionary<string, decimal>>();
            var grpMonthly   = NewMonthlyDict(periods);

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
                        grpMonthly = NewMonthlyDict(periods);
                        var raRows = BuildRangeRows(
                            fmtRow, combined, glParams, glInfo,
                            options.WholeDollars, periods, grpMonthly);
                        reportRows.AddRange(raRows);
                        break;

                    case FormatRowType.Summary:
                        grpMonthly = NewMonthlyDict(periods);
                        var smRow  = BuildSummaryRow(
                            fmtRow, combined, options.WholeDollars,
                            periods, grpMonthly);
                        if (smRow != null) reportRows.Add(smRow);
                        break;

                    case FormatRowType.Subtotal:
                    {
                        // Apply SU's ReverseAmount to the accumulated signed totals
                        var suMonthly = periods.ToDictionary(
                            p => p,
                            p => fmtRow.Options.ReverseAmount ? -grpMonthly[p] : grpMonthly[p]);

                        subtotalAccs[fmtRow.SubtotId] = suMonthly;

                        var allZero = suMonthly.Values.All(v => v == 0m);
                        var suppress = fmtRow.Options.SuppressZeroSubtotal && allZero;
                        if (!suppress)
                            reportRows.Add(BuildTotalRow(
                                fmtRow.Label, suMonthly, options.WholeDollars, periods,
                                RowType.Total));

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

                        var allZero = toMonthly.Values.All(v => v == 0m);
                        var suppress = fmtRow.Options.SuppressIfZero && allZero;
                        if (!suppress)
                            reportRows.Add(BuildTotalRow(
                                fmtRow.Label, toMonthly, options.WholeDollars, periods,
                                RowType.GrandTotal));
                        break;
                    }
                }
            }

            // ── Step 9: Suppression ────────────────────────────────────────
            ReportPostProcessor.ApplySuppression(reportRows, options);

            // ── Step 10: Build columns ─────────────────────────────────────
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

            // ── Step 11: Metadata ──────────────────────────────────────────
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

            var result = new ReportResult
            {
                Metadata = metadata,
                Columns  = columns,
                Rows     = reportRows
            };

            _logger.LogInformation(
                "Trailing12Strategy complete — {Rows} rows", reportRows.Count);

            return ServiceResult<ReportResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Trailing12Strategy.ExecuteAsync failed — DbKey={DbKey}", options.DbKey);
            return ServiceResult<ReportResult>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Period list builder
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds list of 12 periods starting at endPeriod going backwards.
    /// e.g. endPeriod=202503 → [202503, 202502, 202501, 202412, ..., 202404]
    /// </summary>
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

    // ══════════════════════════════════════════════════════════════
    //  Aggregation + pivot
    // ══════════════════════════════════════════════════════════════

    private static Dictionary<string, AcctAggregate> AggregateAndPivot(
        IEnumerable<Trailing12RawRow> rows,
        IReadOnlyList<string> periods)
    {
        var dict = new Dictionary<string, AcctAggregate>();

        foreach (var row in rows)
        {
            if (!dict.TryGetValue(row.AcctNum, out var agg))
            {
                agg = new AcctAggregate(
                    row.AcctName, row.Type, NewMonthlyDict(periods));
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

    // ══════════════════════════════════════════════════════════════
    //  Row builders
    // ══════════════════════════════════════════════════════════════

    private static ReportRow BuildBlankRow() => new()
    {
        RowType     = RowType.SectionHeader,
        AccountCode = string.Empty,
        AccountName = string.Empty
    };

    private static ReportRow BuildTitleRow(string label) => new()
    {
        RowType     = RowType.SectionHeader,
        AccountCode = string.Empty,
        AccountName = label
    };

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
        Cells       = BuildCells(monthly, wholeDollars, periods, null, null)
    };

    private List<ReportRow> BuildRangeRows(
        FormatRow fmtRow,
        Dictionary<string, AcctAggregate> combined,
        GlQueryParameters glParams,
        GLDto glInfo,
        bool wholeDollars,
        IReadOnlyList<string> periods,
        Dictionary<string, decimal> grpMonthly)
    {
        var rows     = new List<ReportRow>();
        var matching = GetMatchingAccounts(fmtRow.Ranges, combined);

        foreach (var (acctNum, data) in matching)
        {
            // Apply sign per period
            var signedMonthly = periods.ToDictionary(
                p => p,
                p => ApplySign(data.Monthly[p], fmtRow));

            // Accumulate signed values into group total
            foreach (var p in periods)
                grpMonthly[p] += signedMonthly[p];

            var formattedAcct = AccountNumberFormatter.Format(
                acctNum, glInfo.AcctLgt, glInfo.AcctDsp);

            var cells = BuildCells(signedMonthly, wholeDollars, periods,
                acctNum, glParams);

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
            Cells       = BuildCells(signedMonthly, wholeDollars, periods, null, null)
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Cell builder
    // ══════════════════════════════════════════════════════════════

    private Dictionary<string, CellValue> BuildCells(
        Dictionary<string, decimal> monthly,
        bool wholeDollars,
        IReadOnlyList<string> periods,
        string? acctNum,
        GlQueryParameters? glParams)
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

            if (acctNum != null && glParams != null)
            {
                // Drillable detail cell
                var drill = new DrillDownRef
                {
                    AcctNums     = new[] { acctNum },
                    EntityIds    = glParams.EntityIds,
                    PeriodFrom   = period,
                    PeriodTo     = period,
                    BasisList    = glParams.BasisList,
                    DisplayLabel = $"{period} · {FiscalCalendar.ToDisplayPeriod(period)}"
                };
                cells[colId] = new CellValue(amount, drill);
            }
            else
            {
                // Non-drillable total/subtotal cell
                cells[colId] = new CellValue(amount == 0m ? null : amount);
            }
        }

        // Total column — non-drillable
        var roundedTotal = Round(total);
        cells[ColTotal] = new CellValue(roundedTotal == 0m ? null : roundedTotal);

        return cells;
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private static decimal ApplySign(decimal amount, FormatRow fmtRow)
    {
        var result = amount;
        if (fmtRow.DebCred == "C")          result = -result;
        if (fmtRow.Options.ReverseAmount)   result = -result;
        // ReverseVariance (^) has no effect — no variance column on T12
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

    private static ServiceResult<bool> ValidateOptions(ReportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.EndPeriod))
            return ServiceResult<bool>.Failure(
                "End Period is required.", ErrorCode.ValidationError);
        if (!options.Basis.Any())
            return ServiceResult<bool>.Failure(
                "At least one Basis must be selected.", ErrorCode.ValidationError);
        if (!options.SelectedIds.Any())
            return ServiceResult<bool>.Failure(
                "At least one Entity must be selected.", ErrorCode.ValidationError);
        return ServiceResult<bool>.Success(true);
    }
}
