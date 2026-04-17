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

namespace BCPFinAnalytics.Services.Reports.TrialBalanceDC;

/// <summary>
/// Trial Balance with Debit and Credit Columns report strategy.
///
/// COLUMNS:
///   Account # | Description | Starting Balance | Debits | Credits | Ending Balance
///
/// COLUMN LOGIC:
///   Starting Balance = balance accumulated from period-open through period before StartPeriod
///     B/C accounts: from BALFORPD to PeriodBeforeStart
///     I   accounts: from BEGYRPD  to PeriodBeforeStart (zero if StartPeriod == BEGYRPD)
///
///   Debits / Credits = net activity between StartPeriod and EndPeriod
///     Net debit  (positive after sign) → shown in Debits  column, Credits = null
///     Net credit (negative after sign) → shown in Credits column, Debits  = null
///     Drill-down is on whichever column has the activity
///
///   Ending Balance = Starting Balance + net activity
///
/// SIGN CONVENTION: follows format O= flags and DEBCRED — same as Simple TB.
/// </summary>
public class TrialBalanceDCStrategy : IReportStrategy
{
    private readonly IFormatLoader _formatLoader;
    private readonly GlFilterBuilder _glFilterBuilder;
    private readonly ITrialBalanceDCRepository _repository;
    private readonly IUnpostedREService _unpostedReService;
    private readonly ILookupService _lookupService;
    private readonly ILogger<TrialBalanceDCStrategy> _logger;

    // Column ID constants
    private const string ColStarting = "STARTING";
    private const string ColDebits   = "DEBITS";
    private const string ColCredits  = "CREDITS";
    private const string ColEnding   = "ENDING";

    public string ReportCode => "TBDC";
    public string ReportName => "Trial Balance - Debit & Credit";

    public TrialBalanceDCStrategy(
        IFormatLoader formatLoader,
        GlFilterBuilder glFilterBuilder,
        ITrialBalanceDCRepository repository,
        IUnpostedREService unpostedReService,
        ILookupService lookupService,
        ILogger<TrialBalanceDCStrategy> logger)
    {
        _formatLoader      = formatLoader;
        _glFilterBuilder   = glFilterBuilder;
        _repository        = repository;
        _unpostedReService = unpostedReService;
        _lookupService     = lookupService;
        _logger            = logger;
    }

    /// <inheritdoc />
    public ReportOptionsConfig GetOptionsConfig() => new()
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

    // Typed aggregate combining starting balance and activity per account
    private sealed record AcctAggregate(
        string  AcctName,
        string  Type,
        decimal StartingBalance,
        decimal Activity);

    /// <inheritdoc />
    public async Task<ServiceResult<ReportResult>> ExecuteAsync(ReportOptions options)
    {
        var optionsJson = JsonSerializer.Serialize(options);
        _logger.LogInformation(
            "TrialBalanceDCStrategy.ExecuteAsync — Starting. " +
            "DbKey={DbKey} UserId={UserId} Options={Options}",
            options.DbKey, options.UserId, optionsJson);

        // ── Step 1: Validate ───────────────────────────────────────────────
        var validation = ValidateOptions(options);
        if (!validation.IsSuccess)
            return ServiceResult<ReportResult>.Failure(
                validation.ErrorMessage, validation.ErrorCode);

        try
        {
            // ── Step 2: Load format ────────────────────────────────────────
            var formatResult = await _formatLoader.LoadAsync(options.DbKey, options.ReportType);
            if (!formatResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    formatResult.ErrorMessage, formatResult.ErrorCode);
            var format = formatResult.Data!;

            // ── Step 3: Load GL info ───────────────────────────────────────
            var glResult = await _lookupService.GetGLsAsync(options.DbKey);
            if (!glResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    glResult.ErrorMessage, glResult.ErrorCode);

            var glInfo = glResult.Data!.FirstOrDefault(g =>
                g.LedgCode.Trim().Equals(
                    format.LedgCode.Trim(), StringComparison.OrdinalIgnoreCase));

            if (glInfo == null)
                return ServiceResult<ReportResult>.Failure(
                    $"GL ledger '{format.LedgCode}' not found.", ErrorCode.NotFound);

            // ── Step 4: Build GL query parameters ─────────────────────────
            // GlFilterBuilder uses EndPeriod from options — we need it for both queries
            var glParamsResult = await _glFilterBuilder.BuildAsync(
                options.DbKey, options, format.LedgCode);
            if (!glParamsResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    glParamsResult.ErrorMessage, glParamsResult.ErrorCode);
            var glParams = glParamsResult.Data!;

            // ── Step 5: Derive period values ───────────────────────────────
            var startPeriod       = FiscalCalendar.ToMriPeriod(options.StartPeriod);
            var periodBeforeStart = FiscalCalendar.PreviousPeriod(startPeriod);

            _logger.LogInformation(
                "TrialBalanceDCStrategy — Periods: StartPeriod={Start} " +
                "PeriodBeforeStart={Before} EndPeriod={End} " +
                "BegYrPd={BegYr} BalForPd={BalFor}",
                startPeriod, periodBeforeStart, glParams.EndPeriod,
                glParams.BegYrPd, glParams.BalForPd);

            // ── Step 6: Execute both queries ───────────────────────────────
            // Starting balance query — skip if StartPeriod == BegYrPd
            // (all income accounts would be zero, B/C would also be zero
            //  since PeriodBeforeStart would be before BALFORPD)
            List<TrialBalanceDCRawRow> startingRows;

            if (FiscalCalendar.ComparePeriods(periodBeforeStart, glParams.BalForPd) < 0)
            {
                // PeriodBeforeStart is before any GL data — no starting balances
                _logger.LogDebug(
                    "TrialBalanceDCStrategy — Skipping starting balance query: " +
                    "PeriodBeforeStart={Before} is before BalForPd={BalFor}",
                    periodBeforeStart, glParams.BalForPd);
                startingRows = new List<TrialBalanceDCRawRow>();
            }
            else
            {
                startingRows = (await _repository.GetStartingBalancesAsync(
                    options.DbKey, glParams, periodBeforeStart)).ToList();
            }

            var activityRows = (await _repository.GetActivityAsync(
                options.DbKey, glParams, startPeriod)).ToList();

            _logger.LogInformation(
                "TrialBalanceDCStrategy — Queries complete: " +
                "StartingRows={SR} ActivityRows={AR}",
                startingRows.Count, activityRows.Count);

            // ── Step 7: Aggregate across entities ─────────────────────────
            var startingByAcct = AggregateByAcct(startingRows);
            var activityByAcct = AggregateByAcct(activityRows);

            // Build combined dictionary — union of all account numbers seen
            var allAcctNums = startingByAcct.Keys
                .Union(activityByAcct.Keys)
                .ToHashSet();

            // Seed ACCTNAME and TYPE from whichever query returned the account
            var acctMeta = new Dictionary<string, (string AcctName, string Type)>();
            foreach (var rows in new[] { startingRows, activityRows })
                foreach (var r in rows)
                {
                    var key = r.AcctNum.TrimEnd();
                    if (!acctMeta.ContainsKey(key))
                        acctMeta[key] = (r.AcctName, r.Type);
                }

            var balanceByAcct = allAcctNums.ToDictionary(
                acct => acct,
                acct =>
                {
                    var meta     = acctMeta.TryGetValue(acct, out var m) ? m : ("", "");
                    var starting = startingByAcct.TryGetValue(acct, out var sv) ? sv : 0m;
                    var activity = activityByAcct.TryGetValue(acct, out var av) ? av : 0m;
                    return new AcctAggregate(meta.AcctName, meta.Type, starting, activity);
                });

            // ── Step 8: Build columns ──────────────────────────────────────
            var startDisplay = FiscalCalendar.ToDisplayPeriod(startPeriod);
            var endDisplay   = FiscalCalendar.ToDisplayPeriod(glParams.EndPeriod);

            var columns = new List<ReportColumn>
            {
                new() { ColumnId = ColStarting, Header = $"Balance at {FiscalCalendar.ToDisplayPeriod(periodBeforeStart)}", DataType = ColumnDataType.Currency, RightAlign = true, Width = 140 },
                new() { ColumnId = ColDebits,   Header = $"Debits {startDisplay}–{endDisplay}",   DataType = ColumnDataType.Currency, RightAlign = true, Width = 140 },
                new() { ColumnId = ColCredits,  Header = $"Credits {startDisplay}–{endDisplay}",  DataType = ColumnDataType.Currency, RightAlign = true, Width = 140 },
                new() { ColumnId = ColEnding,   Header = $"Balance at {endDisplay}",              DataType = ColumnDataType.Currency, RightAlign = true, Width = 140 }
            };

            // ── Step 9: Walk format rows ───────────────────────────────────
            var reportRows     = new List<ReportRow>();
            var subtotalAccumulators = new Dictionary<int, (decimal Starting, decimal Debits, decimal Credits, decimal Ending)>();
            decimal grpStarting = 0m, grpActivity = 0m;

            foreach (var fmtRow in format.Rows)
            {
                switch (fmtRow.RowType)
                {
                    case FormatRowType.Blank:
                        reportRows.Add(BuildBlankRow());
                        break;

                    case FormatRowType.Title:
                        if (!fmtRow.Options.SuppressIfZero || reportRows.Any())
                            reportRows.Add(BuildTitleRow(fmtRow.Label));
                        break;

                    case FormatRowType.Range:
                        grpStarting = 0m; grpActivity = 0m;
                        reportRows.AddRange(BuildRangeRows(
                            fmtRow, balanceByAcct, glParams, glInfo,
                            options.WholeDollars,
                            ref grpStarting, ref grpActivity));
                        break;

                    case FormatRowType.Summary:
                        grpStarting = 0m; grpActivity = 0m;
                        var smRow = BuildSummaryRow(
                            fmtRow, balanceByAcct, glParams,
                            options.WholeDollars,
                            ref grpStarting, ref grpActivity);
                        if (smRow != null) reportRows.Add(smRow);
                        break;

                    case FormatRowType.Subtotal:
                    {
                        var signedStarting = ApplySign(grpStarting, fmtRow);
                        var signedActivity = ApplySign(grpActivity, fmtRow);
                        var (deb, cred)    = SplitDebitCredit(signedActivity);
                        var ending         = signedStarting + signedActivity;

                        subtotalAccumulators[fmtRow.SubtotId] =
                            (signedStarting, deb ?? 0m, cred ?? 0m, ending);

                        var suppress = fmtRow.Options.SuppressZeroSubtotal
                                       && signedStarting == 0m && signedActivity == 0m;
                        if (!suppress)
                            reportRows.Add(BuildTotalRow(
                                fmtRow.Label, signedStarting, deb, cred, ending,
                                options.WholeDollars));

                        grpStarting = 0m; grpActivity = 0m;
                        break;
                    }

                    case FormatRowType.GrandTotal:
                    {
                        decimal gtStart = 0m, gtDeb = 0m, gtCred = 0m, gtEnd = 0m;
                        foreach (var (lo, hi) in fmtRow.SubtotRefs)
                            for (var id = lo; id <= hi; id++)
                                if (subtotalAccumulators.TryGetValue(id, out var st))
                                {
                                    gtStart += st.Starting;
                                    gtDeb   += st.Debits;
                                    gtCred  += st.Credits;
                                    gtEnd   += st.Ending;
                                }

                        var signedStart = ApplySign(gtStart, fmtRow);
                        var signedEnd   = ApplySign(gtEnd, fmtRow);
                        var suppress    = fmtRow.Options.SuppressIfZero
                                          && signedStart == 0m && gtDeb == 0m
                                          && gtCred == 0m && signedEnd == 0m;
                        if (!suppress)
                            reportRows.Add(BuildGrandTotalRow(
                                fmtRow.Label, signedStart,
                                gtDeb == 0m ? null : (decimal?)gtDeb,
                                gtCred == 0m ? null : (decimal?)gtCred,
                                signedEnd, fmtRow.Options, options.WholeDollars));
                        break;
                    }
                }
            }

            // ── Step 10: Unposted Retained Earnings ───────────────────────
            var reResult = await _unpostedReService.BuildRowAsync(
                options.DbKey, glParams, glInfo.ReArnAcct,
                ColEnding, options.WholeDollars);

            if (reResult.IsSuccess && reResult.Data != null)
            {
                // RE row — add zero cells for Starting, Debits, Credits
                var reRow = reResult.Data;
                reRow.Cells[ColStarting] = CellValue.Empty;
                reRow.Cells[ColDebits]   = CellValue.Empty;
                reRow.Cells[ColCredits]  = CellValue.Empty;
                reportRows.Add(reRow);
            }

            // ── Step 11: Suppression ───────────────────────────────────────
            ReportPostProcessor.ApplySuppression(reportRows, options);

            // ── Step 12: Metadata ──────────────────────────────────────────
            var metadata = new ReportMetadata
            {
                ReportTitle     = $"{format.FormatName} — Trial Balance D/C",
                ReportCode      = options.ReportType,
                EntityName      = string.Join(", ", glParams.EntityIds.Take(3))
                                  + (glParams.EntityIds.Count > 3 ? "..." : ""),
                StartPeriod     = startDisplay,
                EndPeriod       = endDisplay,
                RunDate         = DateTime.Now,
                RunByUserId     = options.UserId,
                DbKey           = options.DbKey,
                WholeDollars    = options.WholeDollars,
                OptionsSnapshot = optionsJson
            };

            _logger.LogInformation(
                "TrialBalanceDCStrategy — Complete. OutputRows={Rows}",
                reportRows.Count);

            return ServiceResult<ReportResult>.Success(new ReportResult
            {
                Columns  = columns,
                Rows     = reportRows,
                Metadata = metadata
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TrialBalanceDCStrategy.ExecuteAsync failed — DbKey={DbKey}",
                options.DbKey);
            return ServiceResult<ReportResult>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Row builders
    // ══════════════════════════════════════════════════════════════

    private static ReportRow BuildBlankRow() => new()
    {
        RowType = RowType.SectionHeader,
        AccountCode = string.Empty,
        AccountName = string.Empty
    };

    private static ReportRow BuildTitleRow(string label) => new()
    {
        RowType = RowType.SectionHeader,
        AccountCode = string.Empty,
        AccountName = label
    };

    private static ReportRow BuildTotalRow(
        string label, decimal starting, decimal? debits, decimal? credits,
        decimal ending, bool wholeDollars) => new()
    {
        RowType     = RowType.Total,
        AccountCode = string.Empty,
        AccountName = label,
        Cells       = BuildCells(starting, debits, credits, ending, wholeDollars)
    };

    private static ReportRow BuildGrandTotalRow(
        string label, decimal starting, decimal? debits, decimal? credits,
        decimal ending, FormatOptions opts, bool wholeDollars) => new()
    {
        RowType     = RowType.GrandTotal,
        AccountCode = string.Empty,
        AccountName = label,
        Cells       = BuildCells(starting, debits, credits, ending, wholeDollars)
    };

    private List<ReportRow> BuildRangeRows(
        FormatRow fmtRow,
        Dictionary<string, AcctAggregate> balanceByAcct,
        GlQueryParameters glParams,
        GLDto glInfo,
        bool wholeDollars,
        ref decimal groupStarting,
        ref decimal groupActivity)
    {
        var rows = new List<ReportRow>();
        var matching = GetMatchingAccounts(fmtRow.Ranges, balanceByAcct);

        foreach (var (acctNum, data) in matching)
        {
            var signedStarting = ApplySign(data.StartingBalance, fmtRow);
            var signedActivity = ApplySign(data.Activity, fmtRow);
            var (deb, cred)    = SplitDebitCredit(signedActivity);
            var ending         = signedStarting + signedActivity;

            groupStarting += signedStarting;
            groupActivity += signedActivity;

            var formattedAcct = AccountNumberFormatter.Format(
                acctNum, glInfo.AcctLgt, glInfo.AcctDsp);

            // Drill-down on whichever of Debit/Credit column has activity
            var drillColId = deb.HasValue ? ColDebits : ColCredits;
            var drillDown  = new DrillDownRef
            {
                AcctNums     = new[] { acctNum },
                EntityIds    = glParams.EntityIds,
                PeriodFrom   = FiscalCalendar.ToMriPeriod(
                                   glParams.EndPeriod == glParams.BegYrPd
                                       ? glParams.BegYrPd
                                       : glParams.BegYrPd),
                PeriodTo     = glParams.EndPeriod,
                BasisList    = glParams.BasisList,
                DisplayLabel = $"{formattedAcct} · {data.AcctName}"
            };

            var cells = BuildCells(
                signedStarting, deb, cred, ending, wholeDollars);

            // Add drill-down to the active column only
            if (deb.HasValue)
                cells[ColDebits] = new CellValue(cells[ColDebits].Amount, drillDown);
            else if (cred.HasValue)
                cells[ColCredits] = new CellValue(cells[ColCredits].Amount, drillDown);

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
        Dictionary<string, AcctAggregate> balanceByAcct,
        GlQueryParameters glParams,
        bool wholeDollars,
        ref decimal groupStarting,
        ref decimal groupActivity)
    {
        var matching = GetMatchingAccounts(fmtRow.Ranges, balanceByAcct).ToList();
        if (!matching.Any()) return null;

        var rawStarting = matching.Sum(a => a.Value.StartingBalance);
        var rawActivity = matching.Sum(a => a.Value.Activity);
        var signedStart = ApplySign(rawStarting, fmtRow);
        var signedAct   = ApplySign(rawActivity, fmtRow);
        var (deb, cred) = SplitDebitCredit(signedAct);
        var ending      = signedStart + signedAct;

        groupStarting += signedStart;
        groupActivity += signedAct;

        var acctNums  = matching.Select(a => a.Key).ToList();
        var drillDown = new DrillDownRef
        {
            AcctNums     = acctNums,
            EntityIds    = glParams.EntityIds,
            PeriodFrom   = glParams.BegYrPd,
            PeriodTo     = glParams.EndPeriod,
            BasisList    = glParams.BasisList,
            DisplayLabel = acctNums.Count == 1
                ? fmtRow.Label
                : $"{fmtRow.Label} ({acctNums.Count} accounts)"
        };

        var cells = BuildCells(signedStart, deb, cred, ending, wholeDollars);

        if (deb.HasValue)
            cells[ColDebits] = new CellValue(cells[ColDebits].Amount, drillDown);
        else if (cred.HasValue)
            cells[ColCredits] = new CellValue(cells[ColCredits].Amount, drillDown);

        return new ReportRow
        {
            RowType     = RowType.Detail,
            AccountCode = string.Empty,
            AccountName = fmtRow.Label,
            Indent      = 1,
            Cells       = cells
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private static Dictionary<string, CellValue> BuildCells(
        decimal starting, decimal? debits, decimal? credits,
        decimal ending, bool wholeDollars)
    {
        decimal Round(decimal v) =>
            wholeDollars ? Math.Round(v, 0, MidpointRounding.AwayFromZero) : v;

        return new Dictionary<string, CellValue>
        {
            [ColStarting] = new CellValue(starting == 0m ? null : Round(starting)),
            [ColDebits]   = debits.HasValue
                ? new CellValue(Round(debits.Value))
                : CellValue.Empty,
            [ColCredits]  = credits.HasValue
                ? new CellValue(Round(Math.Abs(credits.Value)))
                : CellValue.Empty,
            [ColEnding]   = new CellValue(ending == 0m ? null : Round(ending))
        };
    }

    /// <summary>
    /// Splits a signed net activity amount into Debit / Credit columns.
    /// Positive (net debit)  → Debits column, Credits = null
    /// Negative (net credit) → Credits column (absolute value), Debits = null
    /// Zero → both null
    /// </summary>
    private static (decimal? Debit, decimal? Credit) SplitDebitCredit(decimal signedActivity)
    {
        if (signedActivity > 0m) return (signedActivity, null);
        if (signedActivity < 0m) return (null, signedActivity); // strategy keeps negative; BuildCells takes abs
        return (null, null);
    }

    private static IEnumerable<KeyValuePair<string, AcctAggregate>> GetMatchingAccounts(
        IReadOnlyList<ResolvedAccountRange> ranges,
        Dictionary<string, AcctAggregate> balanceByAcct)
    {
        return balanceByAcct.Where(kvp =>
        {
            var acct = kvp.Key;
            bool included = false;
            foreach (var range in ranges)
            {
                bool inRange =
                    string.Compare(acct, range.BegAcct, StringComparison.OrdinalIgnoreCase) >= 0
                 && string.Compare(acct, range.EndAcct, StringComparison.OrdinalIgnoreCase) <= 0;

                if (range.IsExclusion && inRange) return false;
                if (!range.IsExclusion && inRange) included = true;
            }
            return included;
        });
    }

    private static Dictionary<string, decimal> AggregateByAcct(
        IEnumerable<TrialBalanceDCRawRow> rows) =>
        rows.GroupBy(r => r.AcctNum.TrimEnd())
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount ?? 0m));

    private static decimal ApplySign(decimal amount, FormatRow fmtRow)
    {
        var result = amount;
        if (fmtRow.DebCred == "C")   result = -result;
        if (fmtRow.Options.FlipSign)  result = -result;
        if (fmtRow.Options.ReverseSign) result = -result;
        return result;
    }

    private static ServiceResult<bool> ValidateOptions(ReportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ReportType))
            return ServiceResult<bool>.Failure("Report Type is required.", ErrorCode.ValidationError);

        if (string.IsNullOrWhiteSpace(options.StartPeriod))
            return ServiceResult<bool>.Failure("Start Period is required.", ErrorCode.ValidationError);

        if (string.IsNullOrWhiteSpace(options.EndPeriod))
            return ServiceResult<bool>.Failure("End Period is required.", ErrorCode.ValidationError);

        if (FiscalCalendar.ComparePeriods(
                FiscalCalendar.ToMriPeriod(options.StartPeriod),
                FiscalCalendar.ToMriPeriod(options.EndPeriod)) > 0)
            return ServiceResult<bool>.Failure(
                "Start Period must be on or before End Period.", ErrorCode.ValidationError);

        if (!options.Basis.Any())
            return ServiceResult<bool>.Failure("At least one Basis must be selected.", ErrorCode.ValidationError);

        if (!options.SelectedIds.Any())
            return ServiceResult<bool>.Failure("At least one Entity must be selected.", ErrorCode.ValidationError);

        return ServiceResult<bool>.Success(true);
    }
}
