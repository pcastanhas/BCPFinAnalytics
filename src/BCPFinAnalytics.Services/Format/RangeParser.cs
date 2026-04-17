using System.Text.RegularExpressions;

namespace BCPFinAnalytics.Services.Format;

/// <summary>
/// Parses the raw ~R= string from LINEDEF into typed segments.
/// Does NOT resolve @GRP* group references — that requires a DB call
/// and is handled by RangeResolver.
///
/// Handles two completely different R= syntaxes:
///   1. Account ranges (RA/SM rows) — MR400000000-MR499909999, @GRPP_SRVCST, @EXC...
///   2. Subtotal ID ranges (TO rows) — 1-12, 1-42, 53-55, "1 Thru 42-53 Thru 56"
/// </summary>
public static class RangeParser
{
    // ══════════════════════════════════════════════════════════════
    //  Account range parsing (RA / SM rows)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents one segment from an account R= string before @GRP* resolution.
    /// </summary>
    public sealed record RawRangeSegment
    {
        /// <summary>True when this segment starts with @EXC.</summary>
        public bool IsExclusion { get; init; }

        /// <summary>
        /// True when this segment references a named GARR group (@GRP*).
        /// When true, GroupId is populated and BegAcct/EndAcct are empty.
        /// When false, BegAcct/EndAcct are populated and GroupId is empty.
        /// </summary>
        public bool IsGroupRef { get; init; }

        /// <summary>GARR GROUPID — populated when IsGroupRef=true. e.g. "P_SRVCST"</summary>
        public string GroupId { get; init; } = string.Empty;

        /// <summary>Direct range start — populated when IsGroupRef=false.</summary>
        public string BegAcct { get; init; } = string.Empty;

        /// <summary>Direct range end — populated when IsGroupRef=false.</summary>
        public string EndAcct { get; init; } = string.Empty;

        /// <summary>Original text of this segment — for logging.</summary>
        public string SourceText { get; init; } = string.Empty;
    }

    /// <summary>
    /// Parses a raw account R= string into a list of unresolved segments.
    ///
    /// Input examples:
    ///   "MR400000000-MR499909999"
    ///   "@GRPP_SRVCST"
    ///   "MR400000000-MR499909999,@GRPI_OFFICE,@EXCMR450000000-MR450000000"
    ///   "@GRPT_RETHAN,@EXC@GRPT_RETHG"
    /// </summary>
    public static IReadOnlyList<RawRangeSegment> ParseAccountRanges(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<RawRangeSegment>();

        var segments = new List<RawRangeSegment>();

        // Split on commas — each segment is independent
        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var segment = ParseSingleAccountSegment(part.Trim());
            if (segment != null)
                segments.Add(segment);
        }

        return segments.AsReadOnly();
    }

    private static RawRangeSegment? ParseSingleAccountSegment(string part)
    {
        if (string.IsNullOrWhiteSpace(part)) return null;

        // Detect @EXC prefix
        bool isExclusion = false;
        var working = part;

        if (working.StartsWith("@EXC", StringComparison.OrdinalIgnoreCase))
        {
            isExclusion = true;
            working = working.Substring(4); // strip @EXC
        }

        // Detect @GRP* group reference
        if (working.StartsWith("@GRP", StringComparison.OrdinalIgnoreCase))
        {
            // Strip @GRP prefix → GARR GROUPID
            var groupId = working.Substring(4); // e.g. "P_SRVCST"
            return new RawRangeSegment
            {
                IsExclusion = isExclusion,
                IsGroupRef  = true,
                GroupId     = groupId,
                SourceText  = part
            };
        }

        // Direct account range: BEGACCT-ENDACCT
        // Account numbers contain letters and digits — split on the dash between accounts
        // Pattern: up to 11 chars, dash, up to 11 chars
        var dashMatch = Regex.Match(working, @"^([A-Z0-9]{1,11})-([A-Z0-9]{1,11})$");
        if (dashMatch.Success)
        {
            return new RawRangeSegment
            {
                IsExclusion = isExclusion,
                IsGroupRef  = false,
                BegAcct     = dashMatch.Groups[1].Value,
                EndAcct     = dashMatch.Groups[2].Value,
                SourceText  = part
            };
        }

        // Single account (BegAcct == EndAcct) — treat as point range
        if (Regex.IsMatch(working, @"^[A-Z0-9]{1,11}$"))
        {
            return new RawRangeSegment
            {
                IsExclusion = isExclusion,
                IsGroupRef  = false,
                BegAcct     = working,
                EndAcct     = working,
                SourceText  = part
            };
        }

        // Unrecognised format — return null (caller logs warning)
        return null;
    }

    // ══════════════════════════════════════════════════════════════
    //  Subtotal ID range parsing (TO rows only)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses a TO row ~R= string into a list of (Lo, Hi) SUBTOTID ranges.
    ///
    /// Input examples:
    ///   "1-12"            → [(1,12)]
    ///   "1-42, 53-55"     → [(1,42),(53,55)]
    ///   "1 Thru 42-53 Thru 56" → [(1,42),(53,56)]  (normalize Thru keyword)
    /// </summary>
    public static IReadOnlyList<(int Lo, int Hi)> ParseSubtotRefs(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<(int, int)>();

        // Normalize "Thru" keyword → comma-separated pairs
        // "1 Thru 42-53 Thru 56" → "1-42,53-56"
        var normalized = NormalizeThruSyntax(raw.Trim());

        var result = new List<(int Lo, int Hi)>();

        var parts = normalized.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var match = Regex.Match(part.Trim(), @"^(\d+)\s*-\s*(\d+)$");
            if (match.Success
                && int.TryParse(match.Groups[1].Value, out var lo)
                && int.TryParse(match.Groups[2].Value, out var hi))
            {
                result.Add((lo, hi));
            }
            else if (int.TryParse(part.Trim(), out var single))
            {
                // Single number — treat as point range
                result.Add((single, single));
            }
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// Normalizes the rare "Thru" keyword syntax used in one known format.
    /// "1 Thru 42-53 Thru 56" → "1-42,53-56"
    /// </summary>
    private static string NormalizeThruSyntax(string raw)
    {
        // Pattern: <number> Thru <number>-<number> Thru <number>
        // Generalized: replace " Thru " between numbers with "-" and use commas to separate groups
        if (!raw.Contains("Thru", StringComparison.OrdinalIgnoreCase))
            return raw;

        // Split on Thru keyword (case-insensitive)
        var parts = Regex.Split(raw, @"\s+[Tt]hru\s+");

        // "1 Thru 42-53 Thru 56" splits to ["1", "42-53", "56"]
        // Rebuild as ranges: 1-42, 53-56
        var result = new List<string>();
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var lo = parts[i].Trim().Split('-').Last().Trim();  // take last number of this group
            var hiGroup = parts[i + 1].Trim().Split('-');
            var hi = hiGroup.First().Trim();  // take first number of next group
            result.Add($"{lo}-{hi}");

            // If this isn't the last pair and the next part has a second number, start next range
            if (i < parts.Length - 2 && hiGroup.Length > 1)
            {
                // The remainder of parts[i+1] after the dash starts the next range's Lo
                // This is handled naturally as we advance i
            }
        }

        // Add final segment's second part if it was a range itself
        var lastParts = parts.Last().Trim().Split('-');
        if (lastParts.Length == 2)
        {
            // Already incorporated above — nothing to add
        }

        return string.Join(",", result);
    }
}
