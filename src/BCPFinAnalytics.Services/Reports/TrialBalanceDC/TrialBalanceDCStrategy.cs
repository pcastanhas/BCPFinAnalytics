using System.Text.Json;
using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.DAL.Interfaces;
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
    private readonly IGlDataRepository _glData;
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
        IGlDataRepository glData,
        IUnpostedREService unpostedReService,
        ILookupService lookupService,
        ILogger<TrialBalanceDCStrategy> logger)
    {
        _formatLoader      = formatLoader;
        _glFilterBuilder   = glFilterBuilder;
        _glData            = glData;
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
            var formatResult = await _formatLoader.LoadAsync(options.DbKey, options.Format);
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

            // ── Step 6: Fetch data from primitives ────────────────────────
            // TBDC's four columns compose from two primitives:
            //   Starting column = GetGlStartingBalance(StartPeriod)
            //   Debits / Credits = GetGlActivity(StartPeriod, EndPeriod) split by sign
            //   Ending column    = Starting + (debits - credits)   (computed in strategy)
            //
            // Two parallel queries, per-account merge below. The primitives
            // handle BALFOR filtering and account-type (B/C vs I) anchoring
            // internally.
            _logger.LogInformation(
                "TrialBalanceDCStrategy — Fetching data from GL primitives.");

            var startingTask = _glData.GetGlStartingBalanceAsync(
                options.DbKey,
                startPeriod,
                glParams.LedgLo, glParams.LedgHi,
                glParams.EntityIds, glParams.BasisList);

            var activityTask = _glData.GetGlActivityAsync(
                options.DbKey,
                startPeriod, glParams.EndPeriod,
                glParams.LedgLo, glParams.LedgHi,
                glParams.EntityIds, glParams.BasisList);

            await Task.WhenAll(startingTask, activityTask);

            var startingByAcct = startingTask.Result;
            var activityByAcct = activityTask.Result;

            _logger.LogInformation(
                "TrialBalanceDCStrategy — Primitives returned: " +
                "{StartingCount} accounts with starting balance, {ActivityCount} with activity.",
                startingByAcct.Count, activityByAcct.Count);

            // ── Step 7: Compose per-account aggregates ────────────────────
            // Union of account numbers from both primitives. Metadata
            // (AcctName, Type) comes from whichever primitive returned the
            // account — both source it from GACC so they agree.
            var allAcctNums = startingByAcct.Keys.Union(activityByAcct.Keys).ToHashSet();

            var balanceByAcct = allAcctNums.ToDictionary(
                acct => acct,
                acct =>
                {
                    var starting = startingByAcct.TryGetValue(acct, out var s) ? s : null;
                    var activity = activityByAcct.TryGetValue(acct, out var a) ? a : null;
                    var meta     = starting ?? activity!;
                    return new AcctAggregate(
                        meta.AcctName,
                        meta.Type,
                        starting?.Amount ?? 0m,
                        activity?.Amount ?? 0m);
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
                            fmtRow, balanceByAcct, glParams, startPeriod, glInfo,
                            options.WholeDollars,
                            ref grpStarting, ref grpActivity));
                        break;

                    case FormatRowType.Summary:
                        grpStarting = 0m; grpActivity = 0m;
                        var smRow = BuildSummaryRow(
                            fmtRow, balanceByAcct, glParams, startPeriod,
                            options.WholeDollars,
                            ref grpStarting, ref grpActivity);
                        if (smRow != null) reportRows.Add(smRow);
                        break;

                    case FormatRowType.Subtotal:
                    {
                        // Store raw in accumulators; apply sign for display only
                        var signedStarting = ApplySign(grpStarting, fmtRow);
                        var signedActivity = ApplySign(grpActivity, fmtRow);
                        var (deb, cred)    = SplitDebitCredit(signedActivity);
                        var ending         = signedStarting + signedActivity;

                        subtotalAccumulators[fmtRow.SubtotId] =
                            (grpStarting, grpActivity, 0m, 0m); // store raw

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
                        // Sum raw subtotals then apply sign for display
                        decimal gtRawStart = 0m, gtRawActivity = 0m;
                        foreach (var (lo, hi) in fmtRow.SubtotRefs)
                            for (var id = lo; id <= hi; id++)
                                if (subtotalAccumulators.TryGetValue(id, out var st))
                                {
                                    gtRawStart    += st.Starting;
                                    gtRawActivity += st.Debits; // Debits holds raw activity
                                }

                        var gtSignedStart    = ApplySign(gtRawStart, fmtRow);
                        var gtSignedActivity = ApplySign(gtRawActivity, fmtRow);
                        var (gtDeb, gtCred)  = SplitDebitCredit(gtSignedActivity);
                        var gtEnd            = gtSignedStart + gtSignedActivity;

                        var suppress = fmtRow.Options.SuppressIfZero
                                       && gtSignedStart == 0m && gtRawActivity == 0m;
                        if (!suppress)
                            reportRows.Add(BuildGrandTotalRow(
                                fmtRow.Label, gtSignedStart, gtDeb, gtCred,
                                gtEnd, fmtRow.Options, options.WholeDollars));
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
                WholeDollars         = options.WholeDollars,
                ShadeAlternateRows   = options.ShadeAlternateRows,
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
        string startPeriod,
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

            groupStarting += data.StartingBalance;  // accumulate RAW
            groupActivity += data.Activity;          // accumulate RAW

            var formattedAcct = AccountNumberFormatter.Format(
                acctNum, glInfo.AcctLgt, glInfo.AcctDsp);

            // Drill-down on whichever of Debit/Credit column has activity.
            // Drill window = [StartPeriod, EndPeriod] — exactly the window whose
            // activity produced the Debits/Credits value the user clicked. The
            // dialog's Starting Balance will be the balance as of StartPeriod-1
            // and the Ending Balance = Starting + Net Activity, which reconciles
            // to the Starting Balance / Ending Balance columns shown in the report.
            var drillColId = deb.HasValue ? ColDebits : ColCredits;
            var drillDown  = new DrillDownRef
            {
                AcctNums         = new[] { acctNum },
                EntityIds        = glParams.EntityIds,
                PeriodFrom       = startPeriod,
                PeriodTo         = glParams.EndPeriod,
                BasisList        = glParams.BasisList,
                DisplayLabel     = $"{formattedAcct} · {data.AcctName}"
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
        string startPeriod,
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

        groupStarting += rawStarting;  // accumulate RAW
        groupActivity += rawActivity;  // accumulate RAW

        var acctNums  = matching.Select(a => a.Key).ToList();
        var drillDown = new DrillDownRef
        {
            AcctNums     = acctNums,
            EntityIds    = glParams.EntityIds,
            PeriodFrom   = startPeriod,
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

    private static decimal ApplySign(decimal amount, FormatRow fmtRow)
    {
        var result = amount;
        if (fmtRow.DebCred == "C")   result = -result;
        if (fmtRow.Options.ReverseVariance)  result = -result;
        if (fmtRow.Options.ReverseAmount) result = -result;
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
