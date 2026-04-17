namespace BCPFinAnalytics.Common.DTOs;

/// <summary>
/// A single GL transaction line returned by the drill-down query.
/// Sourced from JOURNAL (open periods) UNION ALL GHIS (closed periods).
///
/// Column names match the MRI schema exactly — no aliasing of business meaning.
/// </summary>
public class GLDetailLineDto
{
    /// <summary>Posting period in YYYYMM format.</summary>
    public string Period { get; set; } = string.Empty;

    /// <summary>Journal entry reference number.</summary>
    public string Ref { get; set; } = string.Empty;

    /// <summary>Source code identifying the subsystem that created the entry.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Basis this entry was posted under (A, C, or B).</summary>
    public string Basis { get; set; } = string.Empty;

    /// <summary>Entity ID.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>Account number (raw, before display formatting).</summary>
    public string AcctNum { get; set; } = string.Empty;

    /// <summary>Department code.</summary>
    public string Department { get; set; } = string.Empty;

    /// <summary>Line item number within the journal entry.</summary>
    public int Item { get; set; }

    /// <summary>Job code (if applicable).</summary>
    public string JobCode { get; set; } = string.Empty;

    /// <summary>Entry date.</summary>
    public DateTime EntrDate { get; set; }

    /// <summary>Transaction description.</summary>
    public string Descrpn { get; set; } = string.Empty;

    /// <summary>Transaction amount.</summary>
    public decimal Amt { get; set; }
}
