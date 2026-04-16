using System.Text.Json;
using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Interfaces;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using BCPFinAnalytics.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Settings;

/// <summary>
/// Manages saved report settings — serializes/deserializes SavedSettingOptions to JSON.
/// Fully implemented in Phase 4.
/// </summary>
public class SavedSettingsService : ISavedSettingsService
{
    private readonly ISavedSettingsRepository _repo;
    private readonly ILogger<SavedSettingsService> _logger;

    public SavedSettingsService(ISavedSettingsRepository repo, ILogger<SavedSettingsService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<ServiceResult<IEnumerable<SavedSettingDto>>> GetAllForUserAsync(string dbKey, string userId)
    {
        try
        {
            _logger.LogDebug("SavedSettingsService.GetAllForUserAsync — DbKey={DbKey} UserId={UserId}", dbKey, userId);
            var data = await _repo.GetAllForUserAsync(dbKey, userId);
            return ServiceResult<IEnumerable<SavedSettingDto>>.Success(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SavedSettingsService.GetAllForUserAsync failed — DbKey={DbKey}", dbKey);
            return ServiceResult<IEnumerable<SavedSettingDto>>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

    public async Task<ServiceResult<SavedSettingOptions>> GetSettingOptionsAsync(string dbKey, int settingId)
    {
        try
        {
            var setting = await _repo.GetByIdAsync(dbKey, settingId);
            if (setting == null)
                return ServiceResult<SavedSettingOptions>.Failure($"Setting {settingId} not found.", ErrorCode.NotFound);

            var options = JsonSerializer.Deserialize<SavedSettingOptions>(setting.SettingsJson);
            if (options == null)
                return ServiceResult<SavedSettingOptions>.Failure("Failed to deserialize saved setting.", ErrorCode.UnexpectedError);

            return ServiceResult<SavedSettingOptions>.Success(options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SavedSettingsService.GetSettingOptionsAsync failed — DbKey={DbKey} SettingId={SettingId}", dbKey, settingId);
            return ServiceResult<SavedSettingOptions>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

    public async Task<ServiceResult<int>> SaveAsync(string dbKey, string userId, string settingName, bool isPublic, SavedSettingOptions options)
    {
        try
        {
            var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = false });
            var model = new SavedSettingModel
            {
                SettingName = settingName,
                UserId = userId,
                IsPublic = isPublic,
                SettingsJson = json,
                CreatedDate = DateTime.Now,
                UpdatedDate = DateTime.Now
            };
            var newId = await _repo.InsertAsync(dbKey, model);
            _logger.LogInformation("SavedSettingsService — saved setting '{Name}' for user {UserId} — SettingId={Id}", settingName, userId, newId);
            return ServiceResult<int>.Success(newId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SavedSettingsService.SaveAsync failed — DbKey={DbKey}", dbKey);
            return ServiceResult<int>.FromException(ex, ErrorCode.DatabaseError);
        }
    }

    public async Task<ServiceResult<bool>> DeleteAsync(string dbKey, int settingId)
    {
        try
        {
            await _repo.DeleteAsync(dbKey, settingId);
            _logger.LogInformation("SavedSettingsService — deleted setting SettingId={SettingId}", settingId);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SavedSettingsService.DeleteAsync failed — DbKey={DbKey} SettingId={SettingId}", dbKey, settingId);
            return ServiceResult<bool>.FromException(ex, ErrorCode.DatabaseError);
        }
    }
}
