namespace BCPFinAnalytics.Common.Models.Format;

/// <summary>
/// Parsed representation of the MRIGLRW LINEDEF ~O= flag string.
///
/// The raw O= value is a composite string of single-character flags, e.g. "Z^", "^R", "=^".
/// Each character is an independent flag — they can appear in any combination and order.
///
/// Parsing rule: walk the string character by character, set the corresponding flag.
/// O=^ = ReverseVariance: reverses variance direction (Budget-Actual instead of Actual-Budget).
/// O=R = ReverseAmount: reverses the displayed amount (negates for display).
/// Double-caret (^^) = ReverseVariance applied twice = net zero effect (seen in cash flow formats).
/// </summary>
public sealed record FormatOptions
{
    /// <summary>
    /// ^ — Flip sign. Negate the amount (multiply by -1) before display.
    /// Used on credit-normal accounts (income/revenue) so they display as positive numbers.
    /// GL stores income as credits (negative); ^ makes them show as positive.
    /// </summary>
    /// <summary>
    /// O=^ flag. Reverses variance direction for income statement reports.
    /// Normal: Actual - Budget. Reversed: Budget - Actual.
    /// Does NOT affect the displayed actual or budget amounts.
    /// </summary>
    public bool ReverseVariance { get; init; }

    /// <summary>
    /// R — Reverse sign. Reverse the display sign relative to DEBCRED convention.
    /// Used on liability/equity rows in balance sheet formats.
    /// </summary>
    /// <summary>
    /// O=R flag. Reverses (negates) the displayed amount for this row.
    /// Applied in ApplySign() for actual and budget column display.
    /// </summary>
    public bool ReverseAmount { get; init; }

    /// <summary>
    /// S — Suppress if zero. Hide this row if its computed value is zero.
    /// On TI rows: suppress the section header if the section has no activity.
    /// </summary>
    public bool SuppressIfZero { get; init; }

    /// <summary>
    /// Z — Suppress zero subtotal. Hide the SU row if its accumulated value is zero.
    /// Primarily used on SU rows.
    /// </summary>
    public bool SuppressZeroSubtotal { get; init; }

    /// <summary>
    /// E — Expand. Show individual account detail lines within the row.
    /// </summary>
    public bool Expand { get; init; }

    /// <summary>
    /// = — Double underline styling. Indicates grand total presentation.
    /// </summary>
    public bool DoubleUnderline { get; init; }

    /// <summary>
    /// U — Single underline styling.
    /// </summary>
    public bool Underline { get; init; }

    /// <summary>
    /// P — Page break before this row.
    /// </summary>
    public bool PageBreak { get; init; }

    /// <summary>Returns a FormatOptions with all flags false (default/no options).</summary>
    public static FormatOptions None => new();

    /// <summary>
    /// Parses the raw O= flag string from LINEDEF into a FormatOptions record.
    /// Handles null/empty input gracefully — returns FormatOptions.None.
    /// Double-caret (^^) nets to zero ReverseVariance (two negations cancel out).
    /// </summary>
    public static FormatOptions Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return None;

        bool flipSign = false;
        bool reverseSign = false;
        bool suppressIfZero = false;
        bool suppressZeroSubtotal = false;
        bool expand = false;
        bool doubleUnderline = false;
        bool underline = false;
        bool pageBreak = false;

        foreach (var ch in raw.Trim())
        {
            switch (ch)
            {
                case '^': flipSign = !flipSign; break;   // toggle — ^^ cancels out
                case 'R': reverseSign = true;   break;
                case 'S': suppressIfZero = true; break;
                case 'Z': suppressZeroSubtotal = true; break;
                case 'E': expand = true;         break;
                case '=': doubleUnderline = true; break;
                case 'U': underline = true;      break;
                case 'P': pageBreak = true;      break;
                // Unknown chars silently ignored — future-proofing
            }
        }

        return new FormatOptions
        {
            ReverseVariance             = flipSign,
            ReverseAmount          = reverseSign,
            SuppressIfZero       = suppressIfZero,
            SuppressZeroSubtotal = suppressZeroSubtotal,
            Expand               = expand,
            DoubleUnderline      = doubleUnderline,
            Underline            = underline,
            PageBreak            = pageBreak
        };
    }
}
