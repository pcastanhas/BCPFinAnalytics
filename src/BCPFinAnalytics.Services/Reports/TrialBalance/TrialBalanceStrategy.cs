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

namespace BCPFinAnalytics.Services.Reports.TrialBalance;

/// <summary>
/// Simple Trial Balance report strategy.
///
/// REPORT LAYOUT:
///   Account #  |  Description  |  Balance at MM/YYYY
///
/// ONE COLUMN: ending balance only — no budget, no variance, no MTD/YTD split.
///
/// PIPELINE:
///   1. Validate options (end period required, basis required, entities required)
///   2. Load format definition (GUSR + MRIGLRW + GARR via FormatLoader)
///   3. Build GL query parameters (ledger range, BALFORPD, BEGYRPD, basis expand)
///   4. Execute GL query → raw rows (ACCTNUM, ACCTNAME, TYPE, ENTITYID, Balance)
///   5. Aggregate across entities (sum by ACCTNUM)
///   6. Walk format rows in SORTORD order, build ReportResult rows:
///        BL  → blank ReportRow
///        TI  → SectionHeader row, label from ~T=
///        RA  → one Detail row per ACCTNUM matching format ranges,
///               label from GACC.ACCTNAME, account # formatted via AccountNumberFormatter
///        SM  → one Detail row summing all ACCTNUMs in ranges, label from ~T=
///        SU  → Total row summing accumulated RA/SM rows since last SU
///        TO  → GrandTotal row summing specified SUBTOTIDs
///   7. Apply sign convention (O= flags + DEBCRED) to each amount
///   8. Append Unposted Retained Earnings row
///   9. Apply suppression (SuppressZeroAccounts, SuppressInactiveSubtotals)
///  10. Return ReportResult
///
/// SIGN CONVENTION:
///   Each RA/SM row carries DEBCRED and O= flags from the format definition.
///   ApplySign() combines them: base sign from DEBCRED, then O= ^ (flip) and R (reverse).
/// </summary>
public class TrialBalanceStrategy : IReportStrategy
{
    private readonly IFormatLoader _formatLoader;
    private readonly GlFilterBuilder _glFilterBuilder;
    private readonly ITrialBalanceRepository _repository;
    private readonly IUnpostedREService _unpostedReService;
    private readonly ILookupService _lookupService;
    private readonly ILogger<TrialBalanceStrategy> _logger;

    // Column ID constant — single column report
    private const string ColBalance = "BALANCE";

    // Typed aggregate — replaces dynamic for compile safety
    private sealed record AcctAggregate(string AcctName, string Type, decimal Balance, List<string> Entities);

    public string ReportCode => "SIMPLETB";
    public string ReportName => "Simple Trial Balance";

    public TrialBalanceStrategy(
        IFormatLoader formatLoader,
        GlFilterBuilder glFilterBuilder,
        ITrialBalanceRepository repository,
        IUnpostedREService unpostedReService,
        ILookupService lookupService,
        ILogger<TrialBalanceStrategy> logger)
    {
        _formatLoader     = formatLoader;
        _glFilterBuilder  = glFilterBuilder;
        _repository       = repository;
        _unpostedReService = unpostedReService;
        _lookupService    = lookupService;
        _logger           = logger;
    }

    /// <inheritdoc />
    public ReportOptionsConfig GetOptionsConfig() => new()
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

    /// <inheritdoc />
    public async Task<ServiceResult<ReportResult>> ExecuteAsync(ReportOptions options)
    {
        // ── Log all user selections ────────────────────────────────────────
        var optionsJson = JsonSerializer.Serialize(options);
        _logger.LogInformation(
            "TrialBalanceStrategy.ExecuteAsync — Starting. " +
            "ReportType={ReportType} DbKey={DbKey} UserId={UserId} Options={Options}",
            options.ReportType, options.DbKey, options.UserId, optionsJson);

        // ── Step 1: Validate ───────────────────────────────────────────────
        var validation = ValidateOptions(options);
        if (!validation.IsSuccess)
            return ServiceResult<ReportResult>.Failure(
                validation.ErrorMessage, validation.ErrorCode);

        try
        {
            // ── Step 2: Load format definition ────────────────────────────
            var formatResult = await _formatLoader.LoadAsync(options.DbKey, options.Format);
            if (!formatResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    formatResult.ErrorMessage, formatResult.ErrorCode);

            var format = formatResult.Data!;
            _logger.LogInformation(
                "TrialBalanceStrategy — Format loaded: {FormatId} '{FormatName}' " +
                "LedgCode={LedgCode} Rows={Rows}",
                format.FormatId, format.FormatName, format.LedgCode, format.Rows.Count);

            // ── Step 3: Load GL info (ACCTLGT, ACCTDSP for formatter) ─────
            var glResult = await _lookupService.GetGLsAsync(options.DbKey);
            if (!glResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    glResult.ErrorMessage, glResult.ErrorCode);

            var glInfo = glResult.Data!
                .FirstOrDefault(g => g.LedgCode.Trim()
                    .Equals(format.LedgCode.Trim(), StringComparison.OrdinalIgnoreCase));

            if (glInfo == null)
            {
                _logger.LogWarning(
                    "TrialBalanceStrategy — GL info not found for LedgCode={LedgCode}",
                    format.LedgCode);
                return ServiceResult<ReportResult>.Failure(
                    $"GL ledger '{format.LedgCode}' not found in GLCD.",
                    ErrorCode.NotFound);
            }

            // ── Step 4: Build GL query parameters ─────────────────────────
            var glParamsResult = await _glFilterBuilder.BuildAsync(
                options.DbKey, options, format.LedgCode);
            if (!glParamsResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    glParamsResult.ErrorMessage, glParamsResult.ErrorCode);

            var glParams = glParamsResult.Data!;

            // ── Step 5: Execute GL query ───────────────────────────────────
            _logger.LogInformation(
                "TrialBalanceStrategy — Executing GL query. SQL will be logged by repository.");

            var rawRows = (await _repository.GetBalancesAsync(
                options.DbKey, glParams)).ToList();

            _logger.LogInformation(
                "TrialBalanceStrategy — GL query returned {Count} raw rows.",
                rawRows.Count);

            // ── Step 6: Aggregate across entities ─────────────────────────
            // Sum balance across all entities per account number
            var balanceByAcct = rawRows
                .GroupBy(r => r.AcctNum.TrimEnd())
                .ToDictionary(
                    g => g.Key,
                    g => new AcctAggregate(
                        AcctName: g.First().AcctName,
                        Type:     g.First().Type,
                        Balance:  g.Sum(r => r.Balance ?? 0m),
                        Entities: g.Select(r => r.EntityId.TrimEnd()).ToList()));

            // ── Step 7: Build report column ───────────────────────────────
            var endDisplayPeriod = FiscalCalendar.ToDisplayPeriod(glParams.EndPeriod);
            var columns = new List<ReportColumn>
            {
                new()
                {
                    ColumnId  = ColBalance,
                    Header    = $"Balance at {endDisplayPeriod}",
                    DataType  = ColumnDataType.Currency,
                    RightAlign = true,
                    Width     = 150
                }
            };

            // ── Step 8: Walk format rows, build ReportResult rows ─────────
            var reportRows = new List<ReportRow>();

            // Subtotal accumulators keyed by SUBTOTID
            var subtotalAccumulators = new Dictionary<int, decimal>();
            // Running accumulator for the current SU group
            decimal currentGroupTotal = 0m;

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
                        // RA — one Detail row per matching account
                        currentGroupTotal = 0m;
                        var raRows = BuildRangeRows(
                            fmtRow, balanceByAcct, glParams, glInfo, options.WholeDollars,
                            ref currentGroupTotal);
                        reportRows.AddRange(raRows);
                        break;

                    case FormatRowType.Summary:
                        // SM — one Detail row summing all matching accounts
                        currentGroupTotal = 0m;
                        var smRow = BuildSummaryRow(
                            fmtRow, balanceByAcct, glParams, options.WholeDollars,
                            ref currentGroupTotal);
                        if (smRow != null)
                            reportRows.Add(smRow);
                        break;

                    case FormatRowType.Subtotal:
                    {
                        // SU — currentGroupTotal holds raw GL sum.
                        // Store raw for TO accumulation, apply sign for display only.
                        var suDisplay = ApplySign(currentGroupTotal, fmtRow);
                        subtotalAccumulators[fmtRow.SubtotId] = currentGroupTotal; // raw

                        var suppress = fmtRow.Options.SuppressZeroSubtotal && suDisplay == 0m;
                        if (!suppress)
                            reportRows.Add(BuildTotalRow(fmtRow.Label, suDisplay, options.WholeDollars));

                        currentGroupTotal = 0m;
                        break;
                    }

                    case FormatRowType.GrandTotal:
                    {
                        // TO — sums raw subtotal amounts then applies sign for display.
                        var grandRaw = 0m;
                        foreach (var (lo, hi) in fmtRow.SubtotRefs)
                            for (var id = lo; id <= hi; id++)
                                if (subtotalAccumulators.TryGetValue(id, out var v))
                                    grandRaw += v;

                        var grandDisplay = ApplySign(grandRaw, fmtRow);
                        var suppress = fmtRow.Options.SuppressIfZero && grandDisplay == 0m;
                        if (!suppress)
                            reportRows.Add(BuildGrandTotalRow(
                                fmtRow.Label, grandDisplay, fmtRow.Options, options.WholeDollars));
                        break;
                    }
                }
            }

            // ── Step 9: Unposted Retained Earnings ────────────────────────
            var reResult = await _unpostedReService.BuildRowAsync(
                options.DbKey, glParams,
                glInfo.ReArnAcct,
                ColBalance, options.WholeDollars);

            // Note: GLCD.REARNACC needs to be added to GLDto in a follow-up.
            // For now the UnpostedRE row is appended if the service returns data.
            if (reResult.IsSuccess && reResult.Data != null)
                reportRows.Add(reResult.Data);

            // ── Step 10: Apply suppression ────────────────────────────────
            ReportPostProcessor.ApplySuppression(reportRows, options);

            // ── Step 11: Build metadata ───────────────────────────────────
            var metadata = new ReportMetadata
            {
                ReportTitle     = $"{format.FormatName} — Trial Balance",
                ReportCode      = options.ReportType,
                EntityName      = string.Join(", ", glParams.EntityIds.Take(3))
                                  + (glParams.EntityIds.Count > 3 ? "..." : ""),
                StartPeriod     = string.Empty,
                EndPeriod       = endDisplayPeriod,
                RunDate         = DateTime.Now,
                RunByUserId     = options.UserId,
                DbKey           = options.DbKey,
                WholeDollars         = options.WholeDollars,
                ShadeAlternateRows   = options.ShadeAlternateRows,
                OptionsSnapshot = optionsJson
            };

            var result = new ReportResult
            {
                Columns  = columns,
                Rows     = reportRows,
                Metadata = metadata
            };

            _logger.LogInformation(
                "TrialBalanceStrategy — Complete. " +
                "OutputRows={Rows} DbKey={DbKey} EndPeriod={End}",
                reportRows.Count, options.DbKey, endDisplayPeriod);

            return ServiceResult<ReportResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TrialBalanceStrategy.ExecuteAsync failed — " +
                "DbKey={DbKey} ReportType={ReportType}",
                options.DbKey, options.ReportType);
            return ServiceResult<ReportResult>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

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

    private static ReportRow BuildTotalRow(string label, decimal amount, bool wholeDollars) => new()
    {
        RowType     = RowType.Total,
        AccountCode = string.Empty,
        AccountName = label,
        Cells       = new Dictionary<string, CellValue>
        {
            [ColBalance] = new CellValue(
                wholeDollars ? Math.Round(amount, 0, MidpointRounding.AwayFromZero) : amount)
        }
    };

    private static ReportRow BuildGrandTotalRow(
        string label, decimal amount, FormatOptions options, bool wholeDollars) => new()
    {
        RowType     = RowType.GrandTotal,
        AccountCode = string.Empty,
        AccountName = label,
        Cells       = new Dictionary<string, CellValue>
        {
            [ColBalance] = new CellValue(
                wholeDollars ? Math.Round(amount, 0, MidpointRounding.AwayFromZero) : amount)
        }
    };

    /// <summary>
    /// Builds one Detail row per matching account in the RA range.
    /// Accounts are matched against the format's resolved ranges.
    /// Amounts accumulate into the running group total for the next SU row.
    /// </summary>
    private List<ReportRow> BuildRangeRows(
        FormatRow fmtRow,
        Dictionary<string, AcctAggregate> balanceByAcct,
        GlQueryParameters glParams,
        GLDto glInfo,
        bool wholeDollars,
        ref decimal groupTotal)
    {
        var rows = new List<ReportRow>();

        // Find all accounts that fall within the format row's resolved ranges
        var matchingAccounts = GetMatchingAccounts(fmtRow.Ranges, balanceByAcct);

        foreach (var (acctNum, acctData) in matchingAccounts)
        {
            var rawBalance = acctData.Balance;
            var signed    = ApplySign(rawBalance, fmtRow);
            var display   = wholeDollars
                ? Math.Round(signed, 0, MidpointRounding.AwayFromZero)
                : signed;

            groupTotal += rawBalance;  // accumulate RAW for SU/TO math

            var formattedAcct = AccountNumberFormatter.Format(
                acctNum, glInfo.AcctLgt, glInfo.AcctDsp);

            // Build DrillDownRef — Detail rows are drillable
            var drillDown = new DrillDownRef
            {
                AcctNums       = new[] { acctNum },
                EntityIds      = glParams.EntityIds,
                PeriodFrom     = acctData.Type is "B" or "C"
                                     ? glParams.BalForPd
                                     : glParams.BegYrPd,
                PeriodTo       = glParams.EndPeriod,
                BasisList      = glParams.BasisList,
                DisplayLabel   = $"{formattedAcct} · {acctData.AcctName}"
            };

            rows.Add(new ReportRow
            {
                RowType     = RowType.Detail,
                AccountCode = formattedAcct,
                AccountName = acctData.AcctName,
                Indent      = 1,
                Cells       = new Dictionary<string, CellValue>
                {
                    [ColBalance] = new CellValue(display, drillDown)
                }
            });
        }

        return rows;
    }

    /// <summary>
    /// Builds one Detail row summarising all accounts in the SM range.
    /// Label comes from the format row ~T=, not from GACC.
    /// </summary>
    private ReportRow? BuildSummaryRow(
        FormatRow fmtRow,
        Dictionary<string, AcctAggregate> balanceByAcct,
        GlQueryParameters glParams,
        bool wholeDollars,
        ref decimal groupTotal)
    {
        var matchingAccounts = GetMatchingAccounts(fmtRow.Ranges, balanceByAcct);
        if (!matchingAccounts.Any()) return null;

        var rawBalance = matchingAccounts.Sum(a => a.Value.Balance);
        var signed     = ApplySign(rawBalance, fmtRow);
        var display    = wholeDollars
            ? Math.Round(signed, 0, MidpointRounding.AwayFromZero)
            : signed;

        groupTotal += rawBalance;  // accumulate RAW for SU/TO math

        // SM rows: all accounts summed — DrillDownRef carries all account numbers
        var acctNums = matchingAccounts.Select(a => a.Key).ToList();
        var drillDown = new DrillDownRef
        {
            AcctNums     = acctNums,
            EntityIds    = glParams.EntityIds,
            PeriodFrom   = glParams.BegYrPd,  // SM rows are typically income
            PeriodTo     = glParams.EndPeriod,
            BasisList    = glParams.BasisList,
            DisplayLabel = acctNums.Count == 1
                ? fmtRow.Label
                : $"{fmtRow.Label} ({acctNums.Count} accounts)"
        };

        return new ReportRow
        {
            RowType     = RowType.Detail,
            AccountCode = string.Empty,
            AccountName = fmtRow.Label,
            Indent      = 1,
            Cells       = new Dictionary<string, CellValue>
            {
                [ColBalance] = new CellValue(display, drillDown)
            }
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Filters the balance dictionary to only accounts that fall within
    /// the format row's resolved ranges (honoring exclusions).
    /// </summary>
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
                bool inRange = string.Compare(acct, range.BegAcct,
                                   StringComparison.OrdinalIgnoreCase) >= 0
                            && string.Compare(acct, range.EndAcct,
                                   StringComparison.OrdinalIgnoreCase) <= 0;

                if (range.IsExclusion && inRange)
                    return false;   // Explicitly excluded — skip immediately

                if (!range.IsExclusion && inRange)
                    included = true;
            }

            return included;
        });
    }

    /// <summary>
    /// Applies sign convention from format row DEBCRED and O= flags.
    ///
    /// MRI GL stores:
    ///   Debits  as positive  (assets, expenses)
    ///   Credits as negative  (liabilities, income)
    ///
    /// Sign pipeline:
    ///   1. Start with raw GL amount
    ///   2. If DEBCRED='C': negate (credit-normal account)
    ///   3. If O=^ (ReverseVariance): negate again
    ///   4. If O=R (ReverseAmount): negate
    /// </summary>
    private static decimal ApplySign(decimal amount, FormatRow fmtRow)
    {
        var result = amount;

        if (fmtRow.DebCred == "C")
            result = -result;

        if (fmtRow.Options.ReverseVariance)
            result = -result;

        if (fmtRow.Options.ReverseAmount)
            result = -result;

        return result;
    }

    /// <summary>
    /// Validates report options specific to Trial Balance.
    /// </summary>
    private static ServiceResult<bool> ValidateOptions(ReportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ReportType))
            return ServiceResult<bool>.Failure(
                "Report Type is required.", ErrorCode.ValidationError);

        if (string.IsNullOrWhiteSpace(options.Format))
            return ServiceResult<bool>.Failure(
                "Format is required. Please select a GL format.", ErrorCode.ValidationError);

        if (string.IsNullOrWhiteSpace(options.EndPeriod))
            return ServiceResult<bool>.Failure(
                "End Period is required for Trial Balance.", ErrorCode.ValidationError);

        if (!options.Basis.Any())
            return ServiceResult<bool>.Failure(
                "At least one Basis must be selected.", ErrorCode.ValidationError);

        if (!options.SelectedIds.Any())
            return ServiceResult<bool>.Failure(
                "At least one Entity must be selected.", ErrorCode.ValidationError);

        return ServiceResult<bool>.Success(true);
    }
}
