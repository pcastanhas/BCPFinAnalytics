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

    /// <summary>
    /// Variance % color coding thresholds for the Income Statement.
    /// Green when variance % is above GreenAbove.
    /// Red when variance % is below RedBelow.
    /// Yellow otherwise (between RedBelow and GreenAbove).
    /// Defaults: Green > 5%, Red < -5%, Yellow in between.
    /// </summary>
    public double VarianceGreenAbove { get; set; } = 5.0;
    public double VarianceRedBelow   { get; set; } = -5.0;
}
