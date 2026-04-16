namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// Full saved setting record — maps to the BCPFinAnalyticsSavedSettings table.
/// SettingsJson stores the serialized SavedSettingOptions object.
/// </summary>
public class SavedSettingModel
{
    public int SettingId { get; set; }
    public string SettingName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public bool IsPublic { get; set; } = false;
    public DateTime CreatedDate { get; set; }
    public DateTime UpdatedDate { get; set; }

    /// <summary>
    /// JSON-serialized SavedSettingOptions.
    /// Stored as NVARCHAR(MAX) in the database.
    /// Deserialized by SavedSettingsService when loading a setting.
    /// </summary>
    public string SettingsJson { get; set; } = string.Empty;
}
