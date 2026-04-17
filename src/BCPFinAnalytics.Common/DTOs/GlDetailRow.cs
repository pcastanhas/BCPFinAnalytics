namespace BCPFinAnalytics.Common.DTOs;

/// <summary>
/// A single GL transaction row returned by the drill-down query.
/// Maps to the shared column set of JOURNAL (open periods)
/// and GHIS (closed periods) — both tables have identical schemas
/// for the columns we select.
///
/// Column names match the MRI source exactly (JOURNAL/GHIS):
///   period, ref, source, basis, entityid, acctnum,
///   department, item, jobcode, entrdate, descrptn, amt
/// </summary>
public class GlDetailRow
{
    /// <summary>GL period in YYYYMM format — e.g. "202601".</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>Journal entry reference number.</summary>
    public string Ref { get; set; } = string.Empty;

    /// <summary>Source module code — e.g. "AP", "AR", "GL".</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Basis of the transaction — 'A', 'C', or 'B'.</summary>
    public string Basis { get; set; } = string.Empty;

    /// <summary>Entity ID the transaction belongs to.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Raw account number (char 11, may have trailing spaces).</summary>
    public string AcctNum { get; set; } = string.Empty;

    /// <summary>Department code.</summary>
    public string Department { get; set; } = string.Empty;

    /// <summary>Line item number within the journal entry.</summary>
    public string Item { get; set; } = string.Empty;

    /// <summary>Job code — may be blank.</summary>
    public string JobCode { get; set; } = string.Empty;

    /// <summary>Entry date of the journal entry.</summary>
    public DateTime? EntrDate { get; set; }

    /// <summary>Transaction description.</summary>
    public string Descrpn { get; set; } = string.Empty;

    /// <summary>
    /// Transaction amount.
    /// Positive = debit, negative = credit per MRI convention.
    /// </summary>
    public decimal Amt { get; set; }
}
