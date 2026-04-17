namespace BCPFinAnalytics.Services.Helpers;

/// <summary>
/// Builds SARGable account number range bounds for a given ledger code.
///
/// MRI stores ACCTNUM as char(11). The first two characters are the LEDGCODE.
/// To filter by ledger without breaking index usage, we use a range predicate:
///
///   WHERE ACCTNUM >= @LedgLo AND ACCTNUM &lt; @LedgHi
///
/// NOT: WHERE SUBSTRING(ACCTNUM, 1, 2) = @LedgCode  ← NOT SARGable
///
/// EXAMPLE for LEDGCODE = 'MR':
///   LedgLo = "MR000000000"  (ledger code + 9 zeros)
///   LedgHi = "MS000000000"  (next character code + 9 zeros)
///
/// The Hi bound uses the next ASCII character after the last character of the
/// ledger code to create an exclusive upper bound that covers all accounts
/// in the ledger regardless of their suffix.
///
/// This is a pure function — no database calls, no dependencies.
/// </summary>
public static class LedgerRange
{
    private const int AcctNumLength = 11;

    /// <summary>
    /// Builds the SARGable Lo/Hi bounds for a ledger code.
    /// </summary>
    /// <param name="ledgCode">
    /// Two-character ledger code from GLCD.LEDGCODE — e.g. "MR".
    /// </param>
    /// <returns>
    /// (Lo, Hi) where:
    ///   Lo = ledgCode padded to 11 chars with '0'  — inclusive lower bound
    ///   Hi = next-char sentinel padded to 11 chars  — exclusive upper bound
    /// Use in SQL: WHERE ACCTNUM >= @Lo AND ACCTNUM &lt; @Hi
    /// </returns>
    public static (string Lo, string Hi) For(string ledgCode)
    {
        if (string.IsNullOrEmpty(ledgCode))
            throw new ArgumentException("Ledger code cannot be null or empty.", nameof(ledgCode));

        var trimmed = ledgCode.TrimEnd();
        int suffixLength = AcctNumLength - trimmed.Length;

        // Lo: ledgCode + '0' × suffixLength
        var lo = trimmed + new string('0', suffixLength);

        // Hi: increment the last character of ledgCode, then pad with '0'
        // This gives us the exclusive upper bound for all accounts in this ledger.
        var hiPrefix = IncrementLastChar(trimmed);
        var hi = hiPrefix + new string('0', suffixLength);

        return (lo, hi);
    }

    /// <summary>
    /// Increments the last character of the string by one ASCII value.
    /// e.g. "MR" → "MS", "MZ" → "N0" (overflow wraps to next prefix char)
    /// Used to build the exclusive upper bound for the ledger range.
    /// </summary>
    private static string IncrementLastChar(string s)
    {
        var chars = s.ToCharArray();
        int i = chars.Length - 1;

        while (i >= 0)
        {
            if (chars[i] < char.MaxValue)
            {
                chars[i]++;
                return new string(chars);
            }
            // Overflow — carry to next position
            chars[i] = '\0';
            i--;
        }

        // Complete overflow (extremely unlikely for 2-char ledger codes)
        // Return a sentinel that is beyond all possible account numbers
        return new string(char.MaxValue, s.Length);
    }
}
