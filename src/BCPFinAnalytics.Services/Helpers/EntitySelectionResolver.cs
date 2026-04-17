using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Helpers;

/// <summary>
/// Resolves the user's entity selection (SelectionMode + SelectedIds) into
/// a concrete list of entity IDs and a matching SQL fragment for use in queries.
///
/// All SelectionMode variants are supported:
///   Include → use SelectedIds directly as the entity list
///   Exclude → query all entity IDs then subtract SelectedIds
///   Range   → query all entity IDs BETWEEN SelectedIds[0] and SelectedIds[1]
///
/// The resolved entity list is used:
///   1. As Dapper parameters (@EntityIds) in GL queries
///   2. To build DrillDownRef.EntityIds for drill-down modal
///   3. To determine the representative entity for BALFORPD anchor queries
/// </summary>
public class EntitySelectionResolver
{
    private readonly ILookupRepository _lookupRepo;
    private readonly ILogger<EntitySelectionResolver> _logger;

    public EntitySelectionResolver(
        ILookupRepository lookupRepo,
        ILogger<EntitySelectionResolver> logger)
    {
        _lookupRepo = lookupRepo;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the effective entity ID list from the report options.
    ///
    /// Returns the concrete list of entity IDs that should be included
    /// in the report query. Always returns at minimum 1 entity (preflight
    /// has already validated this before we reach here).
    /// </summary>
    public async Task<ServiceResult<IReadOnlyList<string>>> ResolveAsync(
        string dbKey, ReportOptions options)
    {
        try
        {
            var result = await ResolveInternalAsync(dbKey, options);

            _logger.LogDebug(
                "EntitySelectionResolver — Mode={Mode} Input=[{Input}] Resolved=[{Resolved}]",
                options.SelectionMode,
                string.Join(",", options.SelectedIds),
                string.Join(",", result));

            return ServiceResult<IReadOnlyList<string>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "EntitySelectionResolver.ResolveAsync failed — DbKey={DbKey} Mode={Mode}",
                dbKey, options.SelectionMode);
            return ServiceResult<IReadOnlyList<string>>.FromException(
                ex, ErrorCode.DatabaseError);
        }
    }

    private async Task<IReadOnlyList<string>> ResolveInternalAsync(
        string dbKey, ReportOptions options)
    {
        switch (options.SelectionMode)
        {
            case SelectionMode.Include:
                // Use the selected IDs directly — already validated by preflight
                return options.SelectedIds
                    .Select(id => id.Trim().ToUpper())
                    .Distinct()
                    .OrderBy(id => id)
                    .ToList()
                    .AsReadOnly();

            case SelectionMode.Exclude:
            {
                // Load all entities then subtract the excluded ones
                var all = await _lookupRepo.GetEntitiesAsync(dbKey);
                var excluded = new HashSet<string>(
                    options.SelectedIds.Select(id => id.Trim().ToUpper()),
                    StringComparer.OrdinalIgnoreCase);

                return all
                    .Select(e => e.EntityId.Trim().ToUpper())
                    .Where(id => !excluded.Contains(id))
                    .OrderBy(id => id)
                    .ToList()
                    .AsReadOnly();
            }

            case SelectionMode.Range:
            {
                // Range: BETWEEN lo AND hi (inclusive, string comparison)
                // SelectedIds[0] = lo, SelectedIds[1] = hi (validated by preflight)
                var lo = options.SelectedIds[0].Trim().ToUpper();
                var hi = options.SelectedIds[1].Trim().ToUpper();

                var all = await _lookupRepo.GetEntitiesAsync(dbKey);
                return all
                    .Select(e => e.EntityId.Trim().ToUpper())
                    .Where(id => string.Compare(id, lo, StringComparison.OrdinalIgnoreCase) >= 0
                              && string.Compare(id, hi, StringComparison.OrdinalIgnoreCase) <= 0)
                    .OrderBy(id => id)
                    .ToList()
                    .AsReadOnly();
            }

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(options.SelectionMode),
                    options.SelectionMode,
                    "Unrecognised SelectionMode in EntitySelectionResolver");
        }
    }

    /// <summary>
    /// Returns a single representative entity ID from the resolved list.
    /// Used for scalar queries that require one entity (e.g. BALFORPD anchor).
    ///
    /// Since preflight has validated that all entities share the same YEAREND,
    /// any entity in the list will yield the same BALFORPD — we use the first.
    /// </summary>
    public static string GetRepresentativeEntity(IReadOnlyList<string> resolvedEntityIds)
    {
        if (!resolvedEntityIds.Any())
            throw new InvalidOperationException(
                "Cannot get representative entity from empty list. " +
                "Preflight should have rejected this report.");

        return resolvedEntityIds[0];
    }
}
