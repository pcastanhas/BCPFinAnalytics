using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Helpers;

/// <summary>
/// Formats a raw MRI account number using the COBOL-style PICTURE mask from GLCD.ACCTDSP.
///
/// ALGORITHM:
///   1. RTRIM the raw ACCTNUM (strip char(11) padding)
///   2. PadLeft to GLCD.ACCTLGT with '0' (left-pad with zeros)
///   3. Walk ACCTDSP mask character by character:
///        '9' → emit next digit from padded number, advance digit pointer
///        '-' or '.' → emit literal separator, do NOT advance digit pointer
///
/// EXAMPLE:
///   raw="400000000", acctLgt=9, acctDsp="999-99-9999"
///   padded="400000000"
///   formatted="400-00-0000"
///
/// This is a pure function — no database calls, no dependencies.
/// Unit testable in complete isolation.
/// </summary>
public static class AccountNumberFormatter
{
    /// <summary>
    /// Formats a raw account number using the ledger's display mask.
    /// </summary>
    /// <param name="rawAcctNum">
    /// Raw ACCTNUM as stored in GLSUM/GACC — may have trailing spaces (char 11).
    /// </param>
    /// <param name="acctLgt">
    /// Total digit length from GLCD.ACCTLGT — used to left-pad the raw number.
    /// </param>
    /// <param name="acctDsp">
    /// COBOL PICTURE mask from GLCD.ACCTDSP.
    /// Contains only '9' (digit placeholder), '-', or '.' (literal separators).
    /// </param>
    /// <param name="logger">
    /// Optional logger — when provided, logs a warning if the trimmed account
    /// number is shorter than acctLgt (length anomaly, silently corrected).
    /// </param>
    /// <returns>Formatted display string, e.g. "400-00-0000"</returns>
    public static string Format(
        string rawAcctNum,
        int acctLgt,
        string acctDsp,
        ILogger? logger = null)
    {
        if (string.IsNullOrEmpty(rawAcctNum))
            return string.Empty;

        if (string.IsNullOrEmpty(acctDsp))
            return rawAcctNum.Trim();

        // Step 1: trim trailing spaces
        var trimmed = rawAcctNum.TrimEnd();

        // Step 2: left-pad to acctLgt with zeros
        if (trimmed.Length < acctLgt)
        {
            logger?.LogWarning(
                "AccountNumberFormatter — length anomaly: " +
                "ACCTNUM '{Acct}' has {Actual} digits after trim, expected {Expected}. " +
                "Left-padding with zeros.",
                trimmed, trimmed.Length, acctLgt);
        }

        var padded = trimmed.PadLeft(acctLgt, '0');

        // Step 3: walk the mask
        var result = new System.Text.StringBuilder(acctDsp.Length);
        int digitPos = 0;

        foreach (var maskChar in acctDsp)
        {
            switch (maskChar)
            {
                case '9':
                    // Emit next digit — if we've run out, emit '0' as fallback
                    result.Append(digitPos < padded.Length
                        ? padded[digitPos]
                        : '0');
                    digitPos++;
                    break;

                case '-':
                case '.':
                    // Emit literal separator — do NOT advance digit pointer
                    result.Append(maskChar);
                    break;

                default:
                    // Unknown mask character — emit as-is (future-proofing)
                    result.Append(maskChar);
                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Formats a raw account number using a GLDto that carries ACCTLGT and ACCTDSP.
    /// Convenience overload for the common case where a GLDto is already in scope.
    /// </summary>
    public static string Format(
        string rawAcctNum,
        BCPFinAnalytics.Common.DTOs.GLDto glInfo,
        ILogger? logger = null)
        => Format(rawAcctNum, glInfo.AcctLgt, glInfo.AcctDsp, logger);
}
