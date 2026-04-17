using System.Text.RegularExpressions;

namespace BCPFinAnalytics.Services.Format;

/// <summary>
/// Parses the tilde-delimited LINEDEF token string from MRIGLRW.
///
/// LINEDEF format: ~KEY=VALUE~KEY=VALUE~
/// Each key=value pair is delimited by tildes.
///
/// This parser extracts individual token values by key.
/// It does NOT interpret the values — that is done by FormatOptions.Parse()
/// and RangeParser respectively.
/// </summary>
public static class LineDefParser
{
    private static readonly Regex TokenRegex =
        new(@"~([^=~]+)=([^~]*)", RegexOptions.Compiled);

    /// <summary>
    /// Extracts the ~T= label value from a LINEDEF string.
    /// Returns empty string if not present.
    /// </summary>
    public static string GetLabel(string? lineDef)
    {
        if (string.IsNullOrWhiteSpace(lineDef)) return string.Empty;
        var match = Regex.Match(lineDef, @"~T=([^~]*)");
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    /// <summary>
    /// Extracts the ~O= options flag string from a LINEDEF string.
    /// Returns null if not present.
    /// </summary>
    public static string? GetOptions(string? lineDef)
    {
        if (string.IsNullOrWhiteSpace(lineDef)) return null;
        var match = Regex.Match(lineDef, @"~O=([^~]*)");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Extracts the ~R= range string from a LINEDEF string.
    /// Returns null if not present.
    /// The returned string is the raw value — not yet parsed or resolved.
    /// </summary>
    public static string? GetRange(string? lineDef)
    {
        if (string.IsNullOrWhiteSpace(lineDef)) return null;
        var match = Regex.Match(lineDef, @"~R=([^~]*)");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Extracts all key=value pairs from a LINEDEF string as a dictionary.
    /// Useful for debugging or future extension.
    /// </summary>
    public static Dictionary<string, string> GetAllTokens(string? lineDef)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(lineDef)) return result;

        foreach (Match match in TokenRegex.Matches(lineDef))
        {
            var key = match.Groups[1].Value.Trim();
            var value = match.Groups[2].Value.Trim();
            result.TryAdd(key, value);
        }

        return result;
    }
}
