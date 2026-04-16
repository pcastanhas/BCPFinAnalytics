namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// Defines which options panel controls are enabled/disabled
/// for a specific report type.
///
/// Each IReportStrategy implementation returns a hardcoded instance
/// of this class from GetOptionsConfig(). The UI reads this on report
/// type selection and enables/disables controls accordingly.
///
/// No if/switch logic in the UI — the strategy tells the UI what to do.
/// </summary>
public class ReportOptionsConfig
{
    public bool StartPeriodEnabled { get; set; } = true;
    public bool EndPeriodEnabled { get; set; } = true;
    public bool EndPeriodRequired { get; set; } = false;
    public bool BudgetEnabled { get; set; } = true;
    public bool SFTypeEnabled { get; set; } = true;
    public bool FormatEnabled { get; set; } = true;
    public bool BasisEnabled { get; set; } = true;
    public bool EntitySelectionEnabled { get; set; } = true;
    public bool WholeDollarsEnabled { get; set; } = true;

    /// <summary>
    /// When true, the PDF export button is conditionally disabled
    /// if the entity selection count exceeds CrosstabMaxEntitiesForPdf
    /// from appsettings.json.
    /// </summary>
    public bool IsCrosstab { get; set; } = false;

    /// <summary>
    /// Returns a default config with all options enabled.
    /// Use this as a starting point and override specific properties.
    /// </summary>
    public static ReportOptionsConfig AllEnabled() => new();

    /// <summary>
    /// Returns a config with all options disabled.
    /// Useful for reports that only need a subset of controls.
    /// </summary>
    public static ReportOptionsConfig AllDisabled() => new()
    {
        StartPeriodEnabled = false,
        EndPeriodEnabled = false,
        EndPeriodRequired = false,
        BudgetEnabled = false,
        SFTypeEnabled = false,
        FormatEnabled = false,
        BasisEnabled = false,
        EntitySelectionEnabled = false,
        WholeDollarsEnabled = false,
        IsCrosstab = false
    };
}
