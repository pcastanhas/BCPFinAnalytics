namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// Represents all saveable report options.
/// This object is serialized to JSON and stored in the
/// BCPFinAnalyticsSavedSettings.SettingsJson column.
///
/// On load, this is deserialized and used to populate all
/// options panel controls, restoring the saved state exactly.
///
/// Using a JSON column means:
///   - Variable-length lists (entities, basis) are handled naturally
///   - Adding new options in future requires no schema migration
///   - One simple row per saved setting
/// </summary>
public class SavedSettingOptions
{
    public string ReportType { get; set; } = string.Empty;
    public string StartPeriod { get; set; } = string.Empty;
    public string EndPeriod { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string LedgCode { get; set; } = string.Empty;
    public string Budget { get; set; } = string.Empty;
    public string SFType { get; set; } = string.Empty;
    public bool WholeDollars { get; set; } = false;
    public bool SuppressZeroAccounts { get; set; } = false;
    public bool SuppressInactiveSubtotals { get; set; } = false;
    public string SelectionMode { get; set; } = "All";
    public List<string> SelectedIds { get; set; } = new();
    public List<string> Basis { get; set; } = new();
}
