using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Models.Format;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Format;

// ══════════════════════════════════════════════════════════════
//  IFormatLoader
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Loads, parses, and fully resolves a GUSR format definition.
/// The returned FormatDefinition is immutable and requires no further DB calls.
///
/// Loaded fresh on every report run — never cached — because users
/// may update format definitions in another MRI tab between runs.
/// </summary>
public interface IFormatLoader
{
    /// <summary>
    /// Loads the complete format definition for the given format code.
    ///
    /// Pipeline:
    ///   1. Load GUSR header (name, ledger code)
    ///   2. Load all MRIGLRW rows ordered by SORTORD
    ///   3. Parse each LINEDEF (~T=, ~O=, ~R= tokens)
    ///   4. Resolve all @GRP* account group references via GARR
    ///   5. Return immutable FormatDefinition
    ///
    /// Returns ServiceResult.Failure if the format code does not exist
    /// or if any DB call fails.
    /// </summary>
    Task<ServiceResult<FormatDefinition>> LoadAsync(string dbKey, string formatCode);
}

// ══════════════════════════════════════════════════════════════
//  FormatLoader
// ══════════════════════════════════════════════════════════════

/// <summary>
/// Orchestrates format loading, parsing, and @GRP* group resolution.
/// </summary>
public class FormatLoader : IFormatLoader
{
    private readonly IFormatRepository _formatRepo;
    private readonly ILogger<FormatLoader> _logger;

    public FormatLoader(IFormatRepository formatRepo, ILogger<FormatLoader> logger)
    {
        _formatRepo = formatRepo;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<FormatDefinition>> LoadAsync(string dbKey, string formatCode)
    {
        _logger.LogInformation(
            "FormatLoader.LoadAsync — DbKey={DbKey} FormatCode={Code}",
            dbKey, formatCode);

        try
        {
            // ── Step 1: Load format header ─────────────────────────────
            var header = await _formatRepo.GetFormatHeaderAsync(dbKey, formatCode);
            if (header == null)
            {
                _logger.LogWarning(
                    "FormatLoader — format not found: DbKey={DbKey} FormatCode={Code}",
                    dbKey, formatCode);
                return ServiceResult<FormatDefinition>.Failure(
                    $"Format '{formatCode}' was not found.",
                    ErrorCode.NotFound);
            }

            _logger.LogDebug(
                "FormatLoader — header loaded: FormatCode={Code} Name={Name} LedgCode={LedgCode}",
                formatCode, header.Name, header.LedgCode);

            // ── Step 2: Load raw format rows ───────────────────────────
            var rawRows = (await _formatRepo.GetFormatRowsAsync(dbKey, formatCode)).ToList();

            _logger.LogDebug(
                "FormatLoader — {Count} raw rows loaded for FormatCode={Code}",
                rawRows.Count, formatCode);

            // ── Step 3 & 4: Parse each row, resolve @GRP* references ───
            // Cache group lookups within this load call to avoid redundant DB hits
            // when the same group appears multiple times in one format.
            var groupCache = new Dictionary<string, IReadOnlyList<(string Beg, string End)>>(
                StringComparer.OrdinalIgnoreCase);

            var parsedRows = new List<FormatRow>(rawRows.Count);

            foreach (var raw in rawRows)
            {
                var parsed = await ParseRowAsync(dbKey, raw, header.LedgCode, groupCache);
                parsedRows.Add(parsed);
            }

            // ── Step 5: Return immutable FormatDefinition ──────────────
            var definition = new FormatDefinition
            {
                FormatId   = header.Code,
                FormatName = header.Name,
                LedgCode   = header.LedgCode,
                Rows       = parsedRows.AsReadOnly()
            };

            _logger.LogInformation(
                "FormatLoader — loaded FormatCode={Code} Rows={Total} " +
                "(BL={Blank} TI={Title} RA={Range} SM={Summary} SU={Subtotal} TO={GrandTotal})",
                formatCode,
                parsedRows.Count,
                parsedRows.Count(r => r.RowType == FormatRowType.Blank),
                parsedRows.Count(r => r.RowType == FormatRowType.Title),
                parsedRows.Count(r => r.RowType == FormatRowType.Range),
                parsedRows.Count(r => r.RowType == FormatRowType.Summary),
                parsedRows.Count(r => r.RowType == FormatRowType.Subtotal),
                parsedRows.Count(r => r.RowType == FormatRowType.GrandTotal));

            return ServiceResult<FormatDefinition>.Success(definition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "FormatLoader.LoadAsync failed — DbKey={DbKey} FormatCode={Code}",
                dbKey, formatCode);
            return ServiceResult<FormatDefinition>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

    // ── Private: parse one raw row ─────────────────────────────────────

    private async Task<FormatRow> ParseRowAsync(
        string dbKey,
        Common.DTOs.FormatRowDto raw,
        string ledgCode,
        Dictionary<string, IReadOnlyList<(string Beg, string End)>> groupCache)
    {
        var rowType = ParseRowType(raw.Type, raw.SortOrd, raw.FormatId);
        var options = FormatOptions.Parse(LineDefParser.GetOptions(raw.LineDef));

        // ── BL: blank line — no further parsing ───────────────────────
        if (rowType == FormatRowType.Blank)
        {
            return new FormatRow
            {
                RowType  = FormatRowType.Blank,
                SortOrd  = raw.SortOrd,
                SubtotId = 0,
                Options  = options
            };
        }

        // ── TI: title — label and options only ────────────────────────
        if (rowType == FormatRowType.Title)
        {
            return new FormatRow
            {
                RowType  = FormatRowType.Title,
                SortOrd  = raw.SortOrd,
                Label    = LineDefParser.GetLabel(raw.LineDef),
                DebCred  = raw.DebCred,
                Options  = options
            };
        }

        // ── SU: subtotal — label, options, subtotid ───────────────────
        if (rowType == FormatRowType.Subtotal)
        {
            return new FormatRow
            {
                RowType  = FormatRowType.Subtotal,
                SortOrd  = raw.SortOrd,
                SubtotId = raw.SubtotId,
                Label    = LineDefParser.GetLabel(raw.LineDef),
                DebCred  = raw.DebCred,
                Options  = options
            };
        }

        // ── TO: grand total — label, options, numeric subtot refs ─────
        if (rowType == FormatRowType.GrandTotal)
        {
            var rawR = LineDefParser.GetRange(raw.LineDef);
            var subtotRefs = RangeParser.ParseSubtotRefs(rawR);

            _logger.LogTrace(
                "FormatLoader — TO row FormatId={FormatId} SortOrd={SortOrd} " +
                "RawR='{RawR}' Refs={Count}",
                raw.FormatId, raw.SortOrd, rawR, subtotRefs.Count);

            return new FormatRow
            {
                RowType    = FormatRowType.GrandTotal,
                SortOrd    = raw.SortOrd,
                Label      = LineDefParser.GetLabel(raw.LineDef),
                DebCred    = raw.DebCred,
                Options    = options,
                SubtotRefs = subtotRefs
            };
        }

        // ── RA / SM: data rows — resolve account ranges ───────────────
        var rawRange = LineDefParser.GetRange(raw.LineDef);
        var rawSegments = RangeParser.ParseAccountRanges(rawRange);
        var resolvedRanges = await ResolveRangesAsync(
            dbKey, ledgCode, rawSegments, raw.FormatId, raw.SortOrd, groupCache);

        // RA: label is empty — comes from GACC.ACCTNAME at query time
        // SM: label comes from ~T=
        var label = rowType == FormatRowType.Summary
            ? LineDefParser.GetLabel(raw.LineDef)
            : string.Empty;

        return new FormatRow
        {
            RowType  = rowType,
            SortOrd  = raw.SortOrd,
            Label    = label,
            DebCred  = raw.DebCred,
            Options  = options,
            Ranges   = resolvedRanges
        };
    }

    // ── Private: resolve @GRP* references to concrete ranges ──────────

    private async Task<IReadOnlyList<Common.Models.Format.ResolvedAccountRange>> ResolveRangesAsync(
        string dbKey,
        string ledgCode,
        IReadOnlyList<RangeParser.RawRangeSegment> segments,
        string formatId,
        int sortOrd,
        Dictionary<string, IReadOnlyList<(string Beg, string End)>> groupCache)
    {
        var resolved = new List<Common.Models.Format.ResolvedAccountRange>();

        foreach (var seg in segments)
        {
            if (!seg.IsGroupRef)
            {
                // Direct inline range — no DB lookup needed
                resolved.Add(new Common.Models.Format.ResolvedAccountRange
                {
                    BegAcct     = seg.BegAcct,
                    EndAcct     = seg.EndAcct,
                    IsExclusion = seg.IsExclusion,
                    SourceRef   = seg.SourceText
                });
                continue;
            }

            // @GRP* reference — look up GARR (with in-call cache)
            var cacheKey = $"{seg.GroupId}|{ledgCode}";
            if (!groupCache.TryGetValue(cacheKey, out var groupRanges))
            {
                var dtos = await _formatRepo.GetGroupRangesAsync(dbKey, seg.GroupId, ledgCode);
                groupRanges = dtos.Select(d => (d.BegAcct, d.EndAcct)).ToList().AsReadOnly();
                groupCache[cacheKey] = groupRanges;

                if (!groupRanges.Any())
                {
                    _logger.LogWarning(
                        "FormatLoader — @GRP group not found: FormatId={FormatId} SortOrd={SortOrd} " +
                        "GroupId={GroupId} LedgCode={LedgCode}",
                        formatId, sortOrd, seg.GroupId, ledgCode);
                }
                else
                {
                    _logger.LogTrace(
                        "FormatLoader — resolved @GRP{GroupId} to {Count} ranges for LedgCode={LedgCode}",
                        seg.GroupId, groupRanges.Count, ledgCode);
                }
            }

            foreach (var (beg, end) in groupRanges)
            {
                resolved.Add(new Common.Models.Format.ResolvedAccountRange
                {
                    BegAcct     = beg,
                    EndAcct     = end,
                    IsExclusion = seg.IsExclusion,
                    SourceRef   = $"@GRP{seg.GroupId}"
                });
            }
        }

        return resolved.AsReadOnly();
    }

    // ── Private: map raw TYPE string to enum ──────────────────────────

    private FormatRowType ParseRowType(string type, int sortOrd, string formatId)
    {
        return type.Trim().ToUpper() switch
        {
            "BL" => FormatRowType.Blank,
            "TI" => FormatRowType.Title,
            "RA" => FormatRowType.Range,
            "SM" => FormatRowType.Summary,
            "SU" => FormatRowType.Subtotal,
            "TO" => FormatRowType.GrandTotal,
            _ => LogAndDefaultUnknown(type, sortOrd, formatId)
        };
    }

    private FormatRowType LogAndDefaultUnknown(string type, int sortOrd, string formatId)
    {
        _logger.LogWarning(
            "FormatLoader — unknown TYPE '{Type}' at FormatId={FormatId} SortOrd={SortOrd} — defaulting to Blank",
            type, formatId, sortOrd);
        return FormatRowType.Blank;
    }
}
