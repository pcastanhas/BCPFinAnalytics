using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Preflight;

/// <summary>
/// Pre-execution validation service. Runs before every report strategy.
///
/// YEAR-END INVARIANT (rule #22):
/// All selected entities must share the same ENTITY.YEAREND.
/// This invariant allows BALFORPD and BEGYRPD to be computed as scalars in C#
/// rather than per-entity CTE outputs, dramatically simplifying every query.
///
/// VALIDATION FLOW:
///   1. Run COUNT(DISTINCT YEAREND) scoped to the effective entity selection.
///   2. If count == 1 → pass. Report may proceed.
///   3. If count > 1 → fail. Run follow-up SELECT DISTINCT YEAREND to get
///      the conflicting values and include them in the error message.
///   4. If count == 0 → fail. No entities matched the selection criteria.
///
/// The follow-up query (step 3) only runs on the failure path — never on success.
/// </summary>
public class ReportPreflightService : IReportPreflightService
{
    private readonly IEntityMetaRepository _entityMetaRepo;
    private readonly ILogger<ReportPreflightService> _logger;

    public ReportPreflightService(
        IEntityMetaRepository entityMetaRepo,
        ILogger<ReportPreflightService> logger)
    {
        _entityMetaRepo = entityMetaRepo;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> ValidateAsync(ReportOptions options)
    {
        _logger.LogInformation(
            "Preflight starting — ReportType={ReportType} DbKey={DbKey} UserId={UserId} " +
            "SelectionMode={SelectionMode} SelectedIds=[{Ids}]",
            options.ReportType, options.DbKey, options.UserId,
            options.SelectionMode,
            string.Join(", ", options.SelectedIds));

        // ── Guard: Range must have exactly 2 IDs ──────────────────────
        if (options.SelectionMode == SelectionMode.Range)
        {
            if (options.SelectedIds.Count != 2)
            {
                var msg = $"Range selection requires exactly 2 entity IDs. " +
                          $"{options.SelectedIds.Count} provided.";
                _logger.LogWarning("Preflight failed — {Message}", msg);
                return ServiceResult<bool>.Failure(msg, ErrorCode.ValidationError);
            }

            if (string.Compare(options.SelectedIds[0], options.SelectedIds[1],
                    StringComparison.OrdinalIgnoreCase) > 0)
            {
                var msg = $"Range selection: the second entity ID ('{options.SelectedIds[1]}') " +
                          $"must be greater than or equal to the first ('{options.SelectedIds[0]}').";
                _logger.LogWarning("Preflight failed — {Message}", msg);
                return ServiceResult<bool>.Failure(msg, ErrorCode.ValidationError);
            }
        }

        // ── Guard: Include/Exclude must have at least 1 ID ────────────
        // (All mode removed — users must always specify entities explicitly)
        if (options.SelectionMode is SelectionMode.Include or SelectionMode.Exclude
            && options.SelectedIds.Count == 0)
        {
            var mode = options.SelectionMode == SelectionMode.Include ? "Include" : "Exclude";
            var msg = $"{mode} selection requires at least one entity ID. " +
                      $"Please select one or more entities.";
            _logger.LogWarning("Preflight failed — {Message}", msg);
            return ServiceResult<bool>.Failure(msg, ErrorCode.ValidationError);
        }

        // ── Year-end invariant check ───────────────────────────────────
        try
        {
            var count = await _entityMetaRepo.GetDistinctYearEndCountAsync(
                options.DbKey,
                options.SelectionMode,
                options.SelectedIds.AsReadOnly());

            _logger.LogDebug(
                "Preflight year-end count — DbKey={DbKey} Mode={Mode} DistinctYearEnds={Count}",
                options.DbKey, options.SelectionMode, count);

            // No entities matched
            if (count == 0)
            {
                const string msg = "No entities were found for the selected criteria. " +
                                   "Please review your entity selection.";
                _logger.LogWarning("Preflight failed — no entities found. Mode={Mode} Ids=[{Ids}]",
                    options.SelectionMode, string.Join(", ", options.SelectedIds));
                return ServiceResult<bool>.Failure(msg, ErrorCode.ValidationError);
            }

            // Year-end invariant satisfied — now validate LEDGCODE
            if (count == 1)
            {
                var ledgCodes = (await _entityMetaRepo.GetDistinctLedgCodesAsync(
                    options.DbKey,
                    options.SelectionMode,
                    options.SelectedIds.AsReadOnly())).ToList();

                if (ledgCodes.Count > 1)
                {
                    var msg = $"The selected entities span {ledgCodes.Count} different " +
                              $"ledger codes ({string.Join(", ", ledgCodes)}). " +
                              $"All entities in a single report must share the same ledger code.";
                    _logger.LogWarning("Preflight failed — mixed LEDGCODE: {Codes}",
                        string.Join(", ", ledgCodes));
                    return ServiceResult<bool>.Failure(msg, ErrorCode.ValidationError);
                }

                if (ledgCodes.Count == 0)
                {
                    const string msg = "Could not determine the ledger code for the selected entities.";
                    _logger.LogWarning("Preflight failed — no LEDGCODE found");
                    return ServiceResult<bool>.Failure(msg, ErrorCode.ValidationError);
                }

                // Derive LedgCode from entities — user no longer specifies it
                options.LedgCode = ledgCodes[0].Trim();

                _logger.LogInformation(
                    "Preflight passed — YearEnd consistent, LedgCode={LedgCode}. " +
                    "ReportType={ReportType} DbKey={DbKey}",
                    options.LedgCode, options.ReportType, options.DbKey);
                return ServiceResult<bool>.Success(true);
            }

            // Invariant violated — get the conflicting year-ends for the error message
            _logger.LogWarning(
                "Preflight failed — {Count} distinct year-ends found. " +
                "DbKey={DbKey} Mode={Mode} Ids=[{Ids}]",
                count, options.DbKey, options.SelectionMode,
                string.Join(", ", options.SelectedIds));

            var yearEnds = await _entityMetaRepo.GetDistinctYearEndsAsync(
                options.DbKey,
                options.SelectionMode,
                options.SelectedIds.AsReadOnly());

            var yearEndList = string.Join(", ", yearEnds);
            var errorMessage =
                $"The selected entities span {count} different fiscal year-ends ({yearEndList}). " +
                $"All entities in a single report run must share the same fiscal year-end. " +
                $"Please adjust your entity selection and try again.";

            return ServiceResult<bool>.Failure(errorMessage, ErrorCode.ValidationError);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Preflight encountered a database error — DbKey={DbKey} ReportType={ReportType}",
                options.DbKey, options.ReportType);
            return ServiceResult<bool>.FromException(ex, ErrorCode.DatabaseError);
        }
    }
}
