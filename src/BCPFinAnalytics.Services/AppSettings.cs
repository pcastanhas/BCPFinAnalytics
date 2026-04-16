namespace BCPFinAnalytics.Services;

/// <summary>
/// Strongly typed binding for the AppSettings section in appsettings.json.
/// </summary>
public class AppSettings
{
    public string AppName { get; set; } = "BCPFinAnalytics";
    public string AppVersion { get; set; } = "1.0";

    /// <summary>
    /// Maximum number of entities allowed for PDF export on crosstab reports.
    /// If the user selects more than this number, the PDF button is disabled.
    /// Configurable in appsettings.json — no code change required to adjust.
    /// </summary>
    public int CrosstabMaxEntitiesForPdf { get; set; } = 10;
}
