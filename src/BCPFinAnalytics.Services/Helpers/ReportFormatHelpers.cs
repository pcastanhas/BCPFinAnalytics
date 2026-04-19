using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Enums;
using BCPFinAnalytics.Common.Models.Format;

namespace BCPFinAnalytics.Services.Helpers;

/// <summary>
/// Small utilities shared across the 6 report strategies. Previously each
/// strategy had its own copy of these functions; consolidating here so any
/// fix or behavioral change lands in one place.
///
/// Functions here are pure, stateless, and have no dependencies on
/// report-specific aggregate types. Callers pass primitives
/// (decimal, string, ResolvedAccountRange) and get primitives back.
/// </summary>
public static class ReportFormatHelpers
{
    /// <summary>
    /// Applies the FormatRow's sign-flipping options to a raw decimal.
    /// Order of operations: DebCred → ReverseAmount → ReverseVariance.
    /// Each flip multiplies by -1; an even number of flips cancels out.
    ///
    /// Notes per option:
    ///   DebCred='C'        — credit-natured account, negate for display
    ///   ReverseAmount      — format row's 'O=R' flag
    ///   ReverseVariance    — format row's '^' flag (variance reports only)
    ///
    /// Reports without a variance column will never set ReverseVariance,
    /// so the flag is a no-op in those contexts.
    /// </summary>
    public static decimal ApplySign(decimal amount, FormatRow fmtRow)
    {
        var result = amount;
        if (fmtRow.DebCred == "C")           result = -result;
        if (fmtRow.Options.ReverseAmount)    result = -result;
        if (fmtRow.Options.ReverseVariance)  result = -result;
        return result;
    }

    /// <summary>
    /// Returns the account numbers from <paramref name="acctNums"/> that match
    /// the format row's range list, honouring exclusions, in sorted order.
    ///
    /// Matching rule: an account is included if it falls within at least one
    /// non-exclusion range AND no exclusion range. Exclusion short-circuits.
    ///
    /// Returns strings (not dictionary entries) so each report can look up its
    /// own aggregate type using the returned keys.
    /// </summary>
    public static IEnumerable<string> MatchAccounts(
        IReadOnlyList<ResolvedAccountRange> ranges,
        IEnumerable<string> acctNums)
    {
        return acctNums.Where(acct =>
        {
            bool included = false;
            foreach (var range in ranges)
            {
                bool inRange =
                    string.Compare(acct, range.BegAcct, StringComparison.OrdinalIgnoreCase) >= 0
                 && string.Compare(acct, range.EndAcct, StringComparison.OrdinalIgnoreCase) <= 0;

                if (range.IsExclusion && inRange) return false;
                if (!range.IsExclusion && inRange) included = true;
            }
            return included;
        }).OrderBy(a => a, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A blank section-header row — empty name, empty code. Used for 'BL'
    /// format rows. Previously duplicated across 3 strategies (and inlined
    /// with anonymous objects in the other 3).
    /// </summary>
    public static ReportRow BuildBlankRow() => new()
    {
        RowType     = RowType.SectionHeader,
        AccountCode = string.Empty,
        AccountName = string.Empty
    };

    /// <summary>
    /// A title section-header row. Used for 'TI' format rows.
    /// </summary>
    public static ReportRow BuildTitleRow(string label) => new()
    {
        RowType     = RowType.SectionHeader,
        AccountCode = string.Empty,
        AccountName = label
    };
}
