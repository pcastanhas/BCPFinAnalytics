namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// Carries all contextual information about a report run.
/// Included in ReportResult so renderers have everything needed
/// for headers, footers, and audit trails without calling back
/// into the services or database layer.
/// </summary>
public class ReportMetadata
{
    /// <summary>Display title of the report — e.g. "Trial Balance".</summary>
    public string ReportTitle { get; set; } = string.Empty;

    /// <summary>Report type code from GUSR — e.g. "TB001".</summary>
    public string ReportCode { get; set; } = string.Empty;

    /// <summary>Entity or portfolio name — used in report headers.</summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>Report start period in MM/YYYY format.</summary>
    public string StartPeriod { get; set; } = string.Empty;

    /// <summary>Report end period in MM/YYYY format.</summary>
    public string EndPeriod { get; set; } = string.Empty;

    /// <summary>Date and time the report was executed.</summary>
    public DateTime RunDate { get; set; } = DateTime.Now;

    /// <summary>MRI user ID of the person who ran the report.</summary>
    public string RunByUserId { get; set; } = string.Empty;

    /// <summary>Database key used — e.g. "PROD". Shown in report footers.</summary>
    public string DbKey { get; set; } = string.Empty;

    /// <summary>When true, all currency values are rounded to whole dollars.</summary>
    public bool WholeDollars { get; set; } = false;
    public bool ShadeAlternateRows { get; set; } = false;

    /// <summary>
    /// JSON snapshot of all user-selected options at the time the report was run.
    /// Logged to Serilog on every report execution for audit and debugging.
    /// </summary>
    public string OptionsSnapshot { get; set; } = string.Empty;
}
