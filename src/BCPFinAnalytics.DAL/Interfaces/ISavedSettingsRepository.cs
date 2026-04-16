using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;

namespace BCPFinAnalytics.DAL.Interfaces;

/// <summary>
/// CRUD operations for the BCPFinAnalyticsSavedSettings table.
/// Settings are stored as JSON in the SettingsJson column.
/// </summary>
public interface ISavedSettingsRepository
{
    /// <summary>Returns all settings visible to the given user — their own plus all public settings.</summary>
    Task<IEnumerable<SavedSettingDto>> GetAllForUserAsync(string dbKey, string userId);

    /// <summary>Returns a single saved setting by ID.</summary>
    Task<SavedSettingModel?> GetByIdAsync(string dbKey, int settingId);

    /// <summary>Inserts a new saved setting. Returns the new SettingId.</summary>
    Task<int> InsertAsync(string dbKey, SavedSettingModel setting);

    /// <summary>Updates an existing saved setting.</summary>
    Task UpdateAsync(string dbKey, SavedSettingModel setting);

    /// <summary>Deletes a saved setting by ID.</summary>
    Task DeleteAsync(string dbKey, int settingId);
}
