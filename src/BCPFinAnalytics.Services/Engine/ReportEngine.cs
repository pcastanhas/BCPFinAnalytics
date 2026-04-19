using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Models.Format;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using BCPFinAnalytics.Services.Format;
using BCPFinAnalytics.Services.Helpers;
using BCPFinAnalytics.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BCPFinAnalytics.Services.Engine;

/// <summary>
/// Shared report engine — runs the 10-step report pipeline for any
/// <see cref="ReportSpec"/>. See <see cref="IReportEngine"/> for the full
/// responsibility list.
/// </summary>
public class ReportEngine : IReportEngine
{
    private readonly IFormatLoader _formatLoader;
    private readonly GlFilterBuilder _glFilterBuilder;
    private readonly ILookupService _lookupService;
    private readonly IGlDataRepository _glData;
    private readonly IBudgetDataRepository _budgetData;
    private readonly IUnpostedREService _unpostedReService;
    private readonly ILogger<ReportEngine> _logger;

    public ReportEngine(
        IFormatLoader formatLoader,
        GlFilterBuilder glFilterBuilder,
        ILookupService lookupService,
        IGlDataRepository glData,
        IBudgetDataRepository budgetData,
        IUnpostedREService unpostedReService,
        ILogger<ReportEngine> logger)
    {
        _formatLoader      = formatLoader;
        _glFilterBuilder   = glFilterBuilder;
        _lookupService     = lookupService;
        _glData            = glData;
        _budgetData        = budgetData;
        _unpostedReService = unpostedReService;
        _logger            = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ReportResult>> ExecuteAsync(
        ReportSpec spec,
        ReportOptions options)
    {
        _logger.LogInformation(
            "ReportEngine.ExecuteAsync — ReportCode={Code} DbKey={DbKey} UserId={UserId}",
            spec.ReportCode, options.DbKey, options.UserId);

        // ── Step 1: Base + report-specific validation ─────────────────
        var baseValidation = ValidateBaseOptions(options);
        if (!baseValidation.IsSuccess)
            return ServiceResult<ReportResult>.Failure(
                baseValidation.ErrorMessage, baseValidation.ErrorCode);

        if (spec.Validate is not null)
        {
            var reportValidation = spec.Validate(options);
            if (!reportValidation.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    reportValidation.ErrorMessage, reportValidation.ErrorCode);
        }

        try
        {
            // ── Step 2: Load format ───────────────────────────────────
            var formatResult = await _formatLoader.LoadAsync(options.DbKey, options.Format);
            if (!formatResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    formatResult.ErrorMessage, formatResult.ErrorCode);
            var format = formatResult.Data!;

            // ── Step 3: Load GL info ──────────────────────────────────
            var glResult = await _lookupService.GetGLsAsync(options.DbKey);
            if (!glResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    glResult.ErrorMessage, glResult.ErrorCode);

            var glInfo = glResult.Data!
                .FirstOrDefault(g => g.LedgCode.Trim()
                    .Equals(format.LedgCode.Trim(), StringComparison.OrdinalIgnoreCase));

            if (glInfo is null)
                return ServiceResult<ReportResult>.Failure(
                    $"GL ledger '{format.LedgCode}' not found in GLCD.",
                    ErrorCode.NotFound);

            // ── Step 4: Build GL filter context ───────────────────────
            var glParamsResult = await _glFilterBuilder.BuildAsync(
                options.DbKey, options, format.LedgCode);
            if (!glParamsResult.IsSuccess)
                return ServiceResult<ReportResult>.Failure(
                    glParamsResult.ErrorMessage, glParamsResult.ErrorCode);

            var glParams = glParamsResult.Data!;

            // ── Step 5: Resolve columns via spec ──────────────────────
            var columns = spec.BuildColumns(glParams, options);
            if (columns.Count == 0)
                return ServiceResult<ReportResult>.Failure(
                    "ReportSpec produced no columns.", ErrorCode.ValidationError);

            // ── Step 6: Fetch data — dedupe distinct DataSources ──────
            // Accumulated columns each have a Source. Multiple columns may
            // share the same source (e.g. TBDC's _NET is also used by DEBITS,
            // CREDITS, ENDING via derivation). We fetch each distinct source
            // once and index results by source.
            if (spec.Consolidation != ConsolidationMode.Consolidated)
                return ServiceResult<ReportResult>.Failure(
                    "PerEntity consolidation not yet implemented in ReportEngine.",
                    ErrorCode.InternalError);

            var distinctSources = columns
                .Where(c => c.Source is not null)
                .Select(c => c.Source!)
                .SelectMany(FlattenSources)
                .Distinct()
                .ToList();

            var fetchTasks = distinctSources.ToDictionary(
                src => src,
                src => FetchPrimitiveAsync(src, options.DbKey, glParams));

            await Task.WhenAll(fetchTasks.Values);

            var dataBySource = fetchTasks.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Result);

            // ── Step 7: Build per-account per-column accumulator base ─
            // For each account that appears in ANY fetched source, compute
            // its value for each accumulated column. Column Sources may be
            // primitive (GlActivity etc.) or compound (Sum) — EvaluateSource
            // handles both.
            var allAccts = dataBySource.Values
                .SelectMany(d => d.Keys)
                .Distinct()
                .ToHashSet();

            var acctMetadata = new Dictionary<string, (string AcctName, string Type)>();
            foreach (var acct in allAccts)
            {
                foreach (var dict in dataBySource.Values)
                {
                    if (dict.TryGetValue(acct, out var aa))
                    {
                        acctMetadata[acct] = (aa.AcctName, aa.Type);
                        break;
                    }
                }
            }

            // perAcct[acct][colId] = raw (unsigned) accumulated decimal
            var perAcct = new Dictionary<string, Dictionary<string, decimal>>();
            foreach (var acct in allAccts)
            {
                var values = new Dictionary<string, decimal>();
                foreach (var col in columns.Where(c => c.Source is not null))
                    values[col.Id] = EvaluateSource(col.Source!, acct, dataBySource);
                perAcct[acct] = values;
            }

            // ── Step 8: Walk format rows ──────────────────────────────
            var reportRows = new List<ReportRow>();
            var subtotalAccs = new Dictionary<int, Dictionary<string, decimal>>();
            var grpAcc = NewAccumulator(columns);

            foreach (var fmtRow in format.Rows)
            {
                switch (fmtRow.RowType)
                {
                    case FormatRowType.Blank:
                        reportRows.Add(ReportFormatHelpers.BuildBlankRow());
                        break;

                    case FormatRowType.Title:
                        if (!fmtRow.Options.SuppressIfZero || reportRows.Any())
                            reportRows.Add(ReportFormatHelpers.BuildTitleRow(fmtRow.Label));
                        break;

                    case FormatRowType.Range:
                        grpAcc = NewAccumulator(columns);
                        reportRows.AddRange(BuildRangeRows(
                            fmtRow, columns, perAcct, acctMetadata, glInfo, glParams,
                            options.WholeDollars, grpAcc));
                        break;

                    case FormatRowType.Summary:
                        grpAcc = NewAccumulator(columns);
                        var smRow = BuildSummaryRow(
                            fmtRow, columns, perAcct, glParams,
                            options.WholeDollars, grpAcc);
                        if (smRow is not null)
                            reportRows.Add(smRow);
                        break;

                    case FormatRowType.Subtotal:
                    {
                        // Store RAW (unsigned) group accumulator for TO to sum.
                        // Display uses signed values.
                        subtotalAccs[fmtRow.SubtotId] = new Dictionary<string, decimal>(grpAcc);

                        var suSigned = ApplySignToAccumulator(grpAcc, fmtRow);
                        var suppress = fmtRow.Options.SuppressZeroSubtotal
                            && AllZero(suSigned, columns);

                        if (!suppress)
                            reportRows.Add(BuildAggregateRow(
                                fmtRow.Label, RowType.Total, suSigned, columns,
                                options.WholeDollars));

                        grpAcc = NewAccumulator(columns);
                        break;
                    }

                    case FormatRowType.GrandTotal:
                    {
                        var gtAcc = NewAccumulator(columns);
                        foreach (var (lo, hi) in fmtRow.SubtotRefs)
                            for (var id = lo; id <= hi; id++)
                                if (subtotalAccs.TryGetValue(id, out var stored))
                                    foreach (var (colId, v) in stored)
                                        gtAcc[colId] += v;

                        var gtSigned = ApplySignToAccumulator(gtAcc, fmtRow);
                        var suppress = fmtRow.Options.SuppressIfZero
                            && AllZero(gtSigned, columns);

                        if (!suppress)
                            reportRows.Add(BuildAggregateRow(
                                fmtRow.Label, RowType.GrandTotal, gtSigned, columns,
                                options.WholeDollars));
                        break;
                    }
                }
            }

            // ── Step 9: Unposted Retained Earnings ────────────────────
            if (spec.AppendUnpostedRE)
            {
                if (string.IsNullOrEmpty(spec.UnpostedREColumnId))
                    return ServiceResult<ReportResult>.Failure(
                        "ReportSpec.AppendUnpostedRE is true but UnpostedREColumnId is not set.",
                        ErrorCode.ValidationError);

                var reResult = await _unpostedReService.BuildRowAsync(
                    options.DbKey, glParams, glInfo.ReArnAcct,
                    spec.UnpostedREColumnId, options.WholeDollars);

                if (reResult.IsSuccess && reResult.Data is not null)
                    reportRows.Add(reResult.Data);
            }

            // ── Step 10: Suppression ──────────────────────────────────
            ReportPostProcessor.ApplySuppression(reportRows, options);

            // ── Step 11: Metadata ─────────────────────────────────────
            var metadata = new ReportMetadata
            {
                ReportTitle     = $"{format.FormatName} — {spec.ReportName}",
                ReportCode      = spec.ReportCode,
                StartPeriod     = options.StartPeriod ?? string.Empty,
                EndPeriod       = options.EndPeriod   ?? string.Empty,
                RunDate         = DateTime.Now,
                RunByUserId     = options.UserId,
                DbKey           = options.DbKey,
                WholeDollars    = options.WholeDollars,
                ShadeAlternateRows = options.ShadeAlternateRows,
                OptionsSnapshot = JsonSerializer.Serialize(options)
            };

            var reportColumns = columns
                .Where(c => !c.Hidden)
                .Select(c => new ReportColumn
                {
                    ColumnId   = c.Id,
                    Header     = c.Header,
                    DataType   = c.DataType,
                    Width      = c.Width,
                    RightAlign = c.RightAlign
                })
                .ToList();

            _logger.LogInformation(
                "ReportEngine — {ReportCode} complete. Rows={Rows} Columns={Cols}",
                spec.ReportCode, reportRows.Count, reportColumns.Count);

            return ServiceResult<ReportResult>.Success(new ReportResult
            {
                Columns  = reportColumns,
                Rows     = reportRows,
                Metadata = metadata
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ReportEngine.ExecuteAsync failed — ReportCode={Code} DbKey={DbKey}",
                spec.ReportCode, options.DbKey);
            return ServiceResult<ReportResult>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Internals
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Basic validation the engine enforces regardless of report.
    /// Report-specific validation runs via <see cref="ReportSpec.Validate"/>.
    /// </summary>
    private static ServiceResult<bool> ValidateBaseOptions(ReportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ReportType))
            return ServiceResult<bool>.Failure(
                "Report Type is required.", ErrorCode.ValidationError);
        if (string.IsNullOrWhiteSpace(options.Format))
            return ServiceResult<bool>.Failure(
                "Format is required.", ErrorCode.ValidationError);
        if (options.SelectedIds is null || options.SelectedIds.Count == 0)
            return ServiceResult<bool>.Failure(
                "At least one Entity must be selected.", ErrorCode.ValidationError);
        return ServiceResult<bool>.Success(true);
    }

    /// <summary>
    /// Flattens a <see cref="DataSource"/> tree to the set of primitive
    /// sources that need fetching. Sum nodes expand to their sub-sources.
    /// </summary>
    private static IEnumerable<DataSource> FlattenSources(DataSource src) => src switch
    {
        DataSource.Sum sum => sum.Sources.SelectMany(FlattenSources),
        _                   => new[] { src }
    };

    /// <summary>
    /// Fires the right primitive call for a given <see cref="DataSource"/>.
    /// Sum sources should have been flattened before reaching here.
    /// </summary>
    private Task<IReadOnlyDictionary<string, AccountAmount>> FetchPrimitiveAsync(
        DataSource source, string dbKey, GlQueryParameters glParams) => source switch
    {
        DataSource.GlStartingBalance gs =>
            _glData.GetGlStartingBalanceAsync(
                dbKey, gs.Period,
                glParams.LedgLo, glParams.LedgHi,
                glParams.EntityIds, glParams.BasisList),

        DataSource.GlActivity ga =>
            _glData.GetGlActivityAsync(
                dbKey, ga.StartPeriod, ga.EndPeriod,
                glParams.LedgLo, glParams.LedgHi,
                glParams.EntityIds, glParams.BasisList),

        DataSource.BudgetAmount ba =>
            _budgetData.GetBudgetAmountAsync(
                dbKey, ba.StartPeriod, ba.EndPeriod, ba.BudgetType,
                glParams.LedgLo, glParams.LedgHi,
                glParams.EntityIds, glParams.BasisList),

        DataSource.Sum =>
            throw new InvalidOperationException(
                "Sum sources must be flattened before fetch — this is a bug."),

        _ => throw new NotSupportedException($"Unknown DataSource: {source.GetType().Name}")
    };

    /// <summary>
    /// Evaluates a <see cref="DataSource"/> tree for a single account,
    /// returning the summed decimal. Missing accounts contribute 0.
    /// </summary>
    private static decimal EvaluateSource(
        DataSource source,
        string acct,
        IReadOnlyDictionary<DataSource, IReadOnlyDictionary<string, AccountAmount>> data) => source switch
    {
        DataSource.Sum sum =>
            sum.Sources.Sum(s => EvaluateSource(s, acct, data)),

        _ when data.TryGetValue(source, out var dict)
            && dict.TryGetValue(acct, out var aa) => aa.Amount,

        _ => 0m
    };

    /// <summary>
    /// Builds Detail rows for a Range (RA) format row — one output row per
    /// matching account. Accumulates into the group accumulator.
    /// </summary>
    private static List<ReportRow> BuildRangeRows(
        FormatRow fmtRow,
        IReadOnlyList<ColumnSpec> columns,
        IReadOnlyDictionary<string, Dictionary<string, decimal>> perAcct,
        IReadOnlyDictionary<string, (string AcctName, string Type)> meta,
        GLDto glInfo,
        GlQueryParameters glParams,
        bool wholeDollars,
        Dictionary<string, decimal> grpAcc)
    {
        var rows = new List<ReportRow>();
        var matching = ReportFormatHelpers.MatchAccounts(fmtRow.Ranges, perAcct.Keys);

        foreach (var acctNum in matching)
        {
            var raw = perAcct[acctNum];

            // Accumulate RAW into group (sign applied at emit time per row)
            foreach (var col in columns.Where(c => c.Source is not null))
                grpAcc[col.Id] += raw[col.Id];

            // Per-row accumulator used for derived-column evaluation of this row
            var rowAcc = ApplySignToAccumulator(raw, fmtRow);

            var (acctName, _) = meta.TryGetValue(acctNum, out var m) ? m : ("", "");
            var formattedAcct = AccountNumberFormatter.Format(
                acctNum, glInfo.AcctLgt, glInfo.AcctDsp);
            var displayLabel = $"{formattedAcct} · {acctName}";

            var drillContext = new DrillContext
            {
                AcctNums     = new[] { acctNum },
                EntityIds    = glParams.EntityIds,
                BasisList    = glParams.BasisList,
                DisplayLabel = displayLabel
            };

            rows.Add(new ReportRow
            {
                RowType     = RowType.Detail,
                AccountCode = formattedAcct,
                AccountName = acctName,
                Indent      = 1,
                Cells       = BuildCells(columns, rowAcc, drillContext, wholeDollars)
            });
        }

        return rows;
    }

    /// <summary>
    /// Builds a single Detail row for a Summary (SM) format row — all
    /// matching accounts summed. Accumulates into the group accumulator.
    /// </summary>
    private static ReportRow? BuildSummaryRow(
        FormatRow fmtRow,
        IReadOnlyList<ColumnSpec> columns,
        IReadOnlyDictionary<string, Dictionary<string, decimal>> perAcct,
        GlQueryParameters glParams,
        bool wholeDollars,
        Dictionary<string, decimal> grpAcc)
    {
        var matching = ReportFormatHelpers
            .MatchAccounts(fmtRow.Ranges, perAcct.Keys)
            .ToList();
        if (matching.Count == 0) return null;

        // Sum raw values per column across matching accounts
        var rawSummed = NewAccumulator(columns);
        foreach (var acctNum in matching)
        {
            var acctRaw = perAcct[acctNum];
            foreach (var col in columns.Where(c => c.Source is not null))
                rawSummed[col.Id] += acctRaw[col.Id];
        }

        // Accumulate RAW into group
        foreach (var col in columns.Where(c => c.Source is not null))
            grpAcc[col.Id] += rawSummed[col.Id];

        var signed = ApplySignToAccumulator(rawSummed, fmtRow);

        var drillContext = new DrillContext
        {
            AcctNums     = matching,
            EntityIds    = glParams.EntityIds,
            BasisList    = glParams.BasisList,
            DisplayLabel = matching.Count == 1 ? fmtRow.Label : $"{fmtRow.Label} ({matching.Count} accounts)"
        };

        return new ReportRow
        {
            RowType     = RowType.Detail,
            AccountCode = string.Empty,
            AccountName = fmtRow.Label,
            Indent      = 1,
            Cells       = BuildCells(columns, signed, drillContext, wholeDollars)
        };
    }

    /// <summary>
    /// Builds a Subtotal or GrandTotal row. No drill context — aggregate
    /// rows are not clickable by default.
    /// </summary>
    private static ReportRow BuildAggregateRow(
        string label,
        RowType rowType,
        IReadOnlyDictionary<string, decimal> signed,
        IReadOnlyList<ColumnSpec> columns,
        bool wholeDollars)
    {
        // DrillContext with empty AcctNums signals "no drill" to factories —
        // they can check and return null. Individual column specs that want
        // drillable aggregates must handle it in their factory.
        var drillContext = new DrillContext
        {
            AcctNums     = Array.Empty<string>(),
            EntityIds    = Array.Empty<string>(),
            BasisList    = Array.Empty<string>(),
            DisplayLabel = label
        };

        return new ReportRow
        {
            RowType     = rowType,
            AccountCode = string.Empty,
            AccountName = label,
            Cells       = BuildCells(columns, signed, drillContext, wholeDollars)
        };
    }

    /// <summary>
    /// Produces the cell dictionary for a row by: evaluating each accumulated
    /// column from the provided (already-signed) dict, then evaluating each
    /// derived column by invoking its DerivedFn against the same dict.
    /// Applies WholeDollars rounding. Attaches drill refs from ColumnSpec.Drill
    /// when non-null and the factory returns non-null.
    /// </summary>
    private static Dictionary<string, CellValue> BuildCells(
        IReadOnlyList<ColumnSpec> columns,
        IReadOnlyDictionary<string, decimal> signedAccumulator,
        DrillContext drillContext,
        bool wholeDollars)
    {
        var cells = new Dictionary<string, CellValue>();

        // Derived columns read from the same accumulator. We expose it
        // including computed derived values progressively so formulas can
        // reference prior derived columns if the user explicitly ordered them
        // (though by convention derivations use primitives — see docs).
        var workingAcc = new Dictionary<string, decimal>(signedAccumulator);

        foreach (var col in columns)
        {
            if (col.Hidden) continue;

            decimal value;
            if (col.Source is not null)
            {
                value = signedAccumulator.TryGetValue(col.Id, out var v) ? v : 0m;
            }
            else if (col.Derived is not null)
            {
                value = col.Derived(workingAcc);
                workingAcc[col.Id] = value;
            }
            else
            {
                throw new InvalidOperationException(
                    $"ColumnSpec '{col.Id}' has neither Source nor Derived set.");
            }

            if (wholeDollars)
                value = Math.Round(value, 0, MidpointRounding.AwayFromZero);

            CellValue cell;
            if (col.Drill is not null)
            {
                var drillRef = col.Drill(drillContext);
                cell = drillRef switch
                {
                    DrillDownRef gl       => new CellValue(value, gl),
                    BudgetDrillDownRef bd => new CellValue(value) { BudgetDrillDown = bd },
                    _                      => new CellValue(value)
                };
            }
            else
            {
                cell = new CellValue(value);
            }

            cells[col.Id] = cell;
        }

        return cells;
    }

    /// <summary>
    /// Returns a new dict with one entry per accumulated column, all zero.
    /// Derived columns don't accumulate — they're computed at emit time.
    /// </summary>
    private static Dictionary<string, decimal> NewAccumulator(IReadOnlyList<ColumnSpec> columns)
    {
        var dict = new Dictionary<string, decimal>();
        foreach (var col in columns.Where(c => c.Source is not null))
            dict[col.Id] = 0m;
        return dict;
    }

    /// <summary>
    /// Applies sign to every entry in the accumulator using the FormatRow's
    /// DebCred + ReverseAmount + ReverseVariance flags. Returns a new dict;
    /// the input is not mutated.
    /// </summary>
    private static Dictionary<string, decimal> ApplySignToAccumulator(
        IReadOnlyDictionary<string, decimal> acc,
        FormatRow fmtRow)
    {
        var result = new Dictionary<string, decimal>();
        foreach (var (k, v) in acc)
            result[k] = ReportFormatHelpers.ApplySign(v, fmtRow);
        return result;
    }

    /// <summary>
    /// True if every accumulated column value is 0 (used for suppression checks).
    /// Derived columns are not considered — they're computed downstream.
    /// </summary>
    private static bool AllZero(
        IReadOnlyDictionary<string, decimal> acc,
        IReadOnlyList<ColumnSpec> columns)
    {
        foreach (var col in columns.Where(c => c.Source is not null))
            if (acc.TryGetValue(col.Id, out var v) && v != 0m)
                return false;
        return true;
    }
}
