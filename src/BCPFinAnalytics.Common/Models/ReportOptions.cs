using BCPFinAnalytics.Common.Enums;

namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// All user-selected options from the report options panel.
/// Passed to IReportStrategy.Execute() to drive report generation.
/// Also serialized to JSON and stored in ReportMetadata.OptionsSnapshot
/// for every report run — logged to Serilog for audit and debugging.
/// </summary>
public class ReportOptions
{
    /// <summary>Report type code from GUSR — identifies which strategy to use.</summary>
    public string ReportType { get; set; } = string.Empty;

    /// <summary>Start period in MM/YYYY format.</summary>
    public string StartPeriod { get; set; } = string.Empty;

    /// <summary>End period in MM/YYYY format. May be empty if not applicable to the report.</summary>
    public string EndPeriod { get; set; } = string.Empty;

    /// <summary>Selected format code from GUSR.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>Selected General Ledger code from GLCD.LEDGCODE — defaults to the first ledger.</summary>
    public string LedgCode { get; set; } = string.Empty;

    /// <summary>Selected budget type code from GBTY.</summary>
    public string Budget { get; set; } = string.Empty;

    /// <summary>Selected square footage type from SQTY.</summary>
    public string SFType { get; set; } = string.Empty;

    /// <summary>Controls how SelectedIds is interpreted — All, Include, Exclude, or Range.</summary>
    public SelectionMode SelectionMode { get; set; } = SelectionMode.All;

    /// <summary>
    /// Entity or project IDs entered by the user.
    /// Interpretation depends on SelectionMode:
    ///   Include → only these IDs
    ///   Exclude → all except these IDs
    ///   Range   → exactly two IDs defining a range
    ///   All     → ignored
    /// </summary>
    public List<string> SelectedIds { get; set; } = new();

    /// <summary>Selected basis codes from BTYP.</summary>
    public List<string> Basis { get; set; } = new();

    /// <summary>When true, all currency values are rounded to whole dollars.</summary>
    public bool WholeDollars { get; set; } = false;

    /// <summary>When true, account/detail rows whose values are all zero are hidden from the report.</summary>
    public bool SuppressZeroAccounts { get; set; } = false;

    /// <summary>When true, subtotal rows with no underlying activity (all-zero children) are hidden from the report.</summary>
    public bool SuppressInactiveSubtotals { get; set; } = false;

    /// <summary>Database key — passed through to DAL for connection resolution.</summary>
    public string DbKey { get; set; } = string.Empty;

    /// <summary>MRI user ID — included in metadata and audit logging.</summary>
    public string UserId { get; set; } = string.Empty;
}
