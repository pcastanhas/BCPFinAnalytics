using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.DAL.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL.Repositories;

/// <summary>
/// CRUD operations for the BCPFinAnalyticsSavedSettings table.
/// All settings are stored as JSON in the SettingsJson column.
/// Serialization/deserialization is handled by SavedSettingsService — not here.
/// </summary>
public class SavedSettingsRepository : ISavedSettingsRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<SavedSettingsRepository> _logger;

    public SavedSettingsRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<SavedSettingsRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Returns all settings visible to the user:
    ///   - Their own settings (any visibility)
    ///   - All public settings from other users
    /// </summary>
    public async Task<IEnumerable<SavedSettingDto>> GetAllForUserAsync(string dbKey, string userId)
    {
        const string sql = """
            SELECT
                SettingId,
                SettingName,
                UserId,
                IsPublic
            FROM BCPFinAnalyticsSavedSettings
            WHERE UserId = @UserId
               OR IsPublic = 1
            ORDER BY SettingName
            """;

        try
        {
            _logger.LogVerbose(
                "SavedSettingsRepository.GetAllForUserAsync — DbKey={DbKey} UserId={UserId} SQL={Sql}",
                dbKey, userId, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var result = await conn.QueryAsync<SavedSettingDto>(sql, new { UserId = userId });

            _logger.LogDebug(
                "SavedSettingsRepository.GetAllForUserAsync — returned {Count} settings for UserId={UserId}",
                result.Count(), userId);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SavedSettingsRepository.GetAllForUserAsync failed — DbKey={DbKey} UserId={UserId}",
                dbKey, userId);
            throw;
        }
    }

    /// <summary>
    /// Returns the full saved setting record by ID, including the SettingsJson column.
    /// Returns null if not found.
    /// </summary>
    public async Task<SavedSettingModel?> GetByIdAsync(string dbKey, int settingId)
    {
        const string sql = """
            SELECT
                SettingId,
                SettingName,
                UserId,
                IsPublic,
                CreatedDate,
                UpdatedDate,
                SettingsJson
            FROM BCPFinAnalyticsSavedSettings
            WHERE SettingId = @SettingId
            """;

        try
        {
            _logger.LogVerbose(
                "SavedSettingsRepository.GetByIdAsync — DbKey={DbKey} SettingId={SettingId} SQL={Sql}",
                dbKey, settingId, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var result = await conn.QuerySingleOrDefaultAsync<SavedSettingModel>(
                sql, new { SettingId = settingId });

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SavedSettingsRepository.GetByIdAsync failed — DbKey={DbKey} SettingId={SettingId}",
                dbKey, settingId);
            throw;
        }
    }

    /// <summary>
    /// Inserts a new saved setting and returns the generated SettingId.
    /// </summary>
    public async Task<int> InsertAsync(string dbKey, SavedSettingModel setting)
    {
        const string sql = """
            INSERT INTO BCPFinAnalyticsSavedSettings
                (SettingName, UserId, IsPublic, CreatedDate, UpdatedDate, SettingsJson)
            VALUES
                (@SettingName, @UserId, @IsPublic, @CreatedDate, @UpdatedDate, @SettingsJson);

            SELECT CAST(SCOPE_IDENTITY() AS INT);
            """;

        try
        {
            _logger.LogVerbose(
                "SavedSettingsRepository.InsertAsync — DbKey={DbKey} SettingName={Name} UserId={UserId} SQL={Sql}",
                dbKey, setting.SettingName, setting.UserId, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var newId = await conn.QuerySingleAsync<int>(sql, new
            {
                setting.SettingName,
                setting.UserId,
                setting.IsPublic,
                setting.CreatedDate,
                setting.UpdatedDate,
                setting.SettingsJson
            });

            _logger.LogInformation(
                "SavedSettingsRepository.InsertAsync — inserted SettingId={SettingId} Name='{Name}' UserId={UserId}",
                newId, setting.SettingName, setting.UserId);

            return newId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SavedSettingsRepository.InsertAsync failed — DbKey={DbKey} SettingName={Name}",
                dbKey, setting.SettingName);
            throw;
        }
    }

    /// <summary>
    /// Updates an existing saved setting by SettingId.
    /// </summary>
    public async Task UpdateAsync(string dbKey, SavedSettingModel setting)
    {
        const string sql = """
            UPDATE BCPFinAnalyticsSavedSettings
            SET
                SettingName  = @SettingName,
                IsPublic     = @IsPublic,
                UpdatedDate  = @UpdatedDate,
                SettingsJson = @SettingsJson
            WHERE SettingId = @SettingId
            """;

        try
        {
            _logger.LogVerbose(
                "SavedSettingsRepository.UpdateAsync — DbKey={DbKey} SettingId={SettingId} SQL={Sql}",
                dbKey, setting.SettingId, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            await conn.ExecuteAsync(sql, new
            {
                setting.SettingName,
                setting.IsPublic,
                UpdatedDate = DateTime.Now,
                setting.SettingsJson,
                setting.SettingId
            });

            _logger.LogInformation(
                "SavedSettingsRepository.UpdateAsync — updated SettingId={SettingId}",
                setting.SettingId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SavedSettingsRepository.UpdateAsync failed — DbKey={DbKey} SettingId={SettingId}",
                dbKey, setting.SettingId);
            throw;
        }
    }

    /// <summary>
    /// Deletes a saved setting by SettingId.
    /// </summary>
    public async Task DeleteAsync(string dbKey, int settingId)
    {
        const string sql = """
            DELETE FROM BCPFinAnalyticsSavedSettings
            WHERE SettingId = @SettingId
            """;

        try
        {
            _logger.LogVerbose(
                "SavedSettingsRepository.DeleteAsync — DbKey={DbKey} SettingId={SettingId} SQL={Sql}",
                dbKey, settingId, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            await conn.ExecuteAsync(sql, new { SettingId = settingId });

            _logger.LogInformation(
                "SavedSettingsRepository.DeleteAsync — deleted SettingId={SettingId}", settingId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SavedSettingsRepository.DeleteAsync failed — DbKey={DbKey} SettingId={SettingId}",
                dbKey, settingId);
            throw;
        }
    }
}
