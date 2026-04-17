using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Helpers;

/// <summary>
/// Assembles a complete GlQueryParameters object from user report options.
///
/// This is the central coordinator for all pre-query C# computation:
///   1. Resolve entity list from SelectionMode
///   2. Resolve ledger range bounds (SARGable lo/hi)
///   3. Query BALFORPD anchor from PERIOD table
///   4. Derive BEGYRPD from BALFORPD and EndPeriod
///   5. Expand basis list (add 'B' if 'A' or 'C' selected)
///
/// The resulting GlQueryParameters is passed to report repositories
/// as the complete, ready-to-use parameter set for GLSUM queries.
///
/// Called once per report execution — each report strategy calls BuildAsync()
/// at the start of its Execute() method.
/// </summary>
public class GlFilterBuilder
{
    private readonly IBalForRepository _balForRepo;
    private readonly EntitySelectionResolver _entityResolver;
    private readonly ILogger<GlFilterBuilder> _logger;

    public GlFilterBuilder(
        IBalForRepository balForRepo,
        EntitySelectionResolver entityResolver,
        ILogger<GlFilterBuilder> logger)
    {
        _balForRepo = balForRepo;
        _entityResolver = entityResolver;
        _logger = logger;
    }

    /// <summary>
    /// Builds the complete GlQueryParameters from report options and ledger code.
    /// </summary>
    /// <param name="dbKey">Active database connection key.</param>
    /// <param name="options">User-selected report options.</param>
    /// <param name="ledgCode">Ledger code from the selected format (GUSR.LEDGCODE).</param>
    public async Task<ServiceResult<GlQueryParameters>> BuildAsync(
        string dbKey,
        ReportOptions options,
        string ledgCode)
    {
        _logger.LogDebug(
            "GlFilterBuilder.BuildAsync — DbKey={DbKey} LedgCode={LedgCode} " +
            "StartPeriod={Start} EndPeriod={End}",
            dbKey, ledgCode, options.StartPeriod, options.EndPeriod);

        try
        {
            // ── Step 1: Resolve entity list ────────────────────────────
            var entityResult = await _entityResolver.ResolveAsync(dbKey, options);
            if (!entityResult.IsSuccess)
                return ServiceResult<GlQueryParameters>.Failure(
                    entityResult.ErrorMessage, entityResult.ErrorCode);

            var entityIds = entityResult.Data!;

            // ── Step 2: SARGable ledger range bounds ───────────────────
            var (ledgLo, ledgHi) = LedgerRange.For(ledgCode);

            // ── Step 3: Convert periods to YYYYMM ─────────────────────
            var endPeriod = FiscalCalendar.ToMriPeriod(options.EndPeriod);

            // ── Step 4: Query BALFORPD anchor ──────────────────────────
            var repEntity = EntitySelectionResolver.GetRepresentativeEntity(entityIds);
            var balForPd = await _balForRepo.GetBalForAnchorAsync(
                dbKey, repEntity, endPeriod);

            _logger.LogDebug(
                "GlFilterBuilder — BalForPd={BalForPd} RepEntity={RepEntity}",
                balForPd, repEntity);

            // ── Step 5: Derive BEGYRPD ─────────────────────────────────
            var begYrPd = FiscalCalendar.DeriveBegYrPd(balForPd, endPeriod);

            _logger.LogDebug(
                "GlFilterBuilder — BegYrPd={BegYrPd} EndPeriod={EndPeriod}",
                begYrPd, endPeriod);

            // ── Step 6: Expand basis list ──────────────────────────────
            var basisList = ExpandBasis(options.Basis);

            _logger.LogInformation(
                "GlFilterBuilder — Parameters resolved: " +
                "LedgLo={Lo} LedgHi={Hi} BalForPd={BalFor} BegYrPd={BegYr} " +
                "EndPeriod={End} Entities=[{Entities}] Basis=[{Basis}]",
                ledgLo, ledgHi, balForPd, begYrPd, endPeriod,
                string.Join(",", entityIds),
                string.Join(",", basisList));

            return ServiceResult<GlQueryParameters>.Success(new GlQueryParameters
            {
                LedgLo    = ledgLo,
                LedgHi    = ledgHi,
                BalForPd  = balForPd,
                BegYrPd   = begYrPd,
                EndPeriod = endPeriod,
                EntityIds = entityIds,
                BasisList = basisList
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GlFilterBuilder.BuildAsync failed — DbKey={DbKey} LedgCode={LedgCode}",
                dbKey, ledgCode);
            return ServiceResult<GlQueryParameters>.FromException(
                ex, Common.Enums.ErrorCode.DatabaseError);
        }
    }

    /// <summary>
    /// Applies the MRI basis expansion rule:
    /// If the user selected 'A' (Accrual) or 'C' (Cash), 'B' (Both) is automatically included.
    /// Returns a deduplicated, uppercase list.
    /// </summary>
    public static IReadOnlyList<string> ExpandBasis(IEnumerable<string> basisList)
    {
        var expanded = new HashSet<string>(
            basisList.Select(b => b.Trim().ToUpper()),
            StringComparer.OrdinalIgnoreCase);

        if (expanded.Contains("A") || expanded.Contains("C"))
            expanded.Add("B");

        return expanded.OrderBy(b => b).ToList().AsReadOnly();
    }
}
