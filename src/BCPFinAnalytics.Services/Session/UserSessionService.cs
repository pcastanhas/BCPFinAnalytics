using BCPFinAnalytics.Common.Wrappers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Session;

/// <summary>
/// Scoped service that holds the user's session context for the lifetime
/// of a Blazor Server circuit.
///
/// Populated on first page load from URL query string parameters:
///   ?db=PROD&amp;userid=paul
///
/// The db key is validated against the ConnectionStrings section of
/// appsettings.json. The userid is trusted as-is (internal app).
/// </summary>
public class UserSessionService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<UserSessionService> _logger;

    public string DbKey { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public bool IsValid { get; private set; } = false;

    /// <summary>
    /// Timestamp when the session was initialized — useful for audit logging.
    /// </summary>
    public DateTime InitializedAt { get; private set; }

    public UserSessionService(
        IConfiguration configuration,
        ILogger<UserSessionService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the session from URL parameters.
    /// Called once from the Index page on first load.
    /// Returns a ServiceResult indicating success or describing the failure.
    /// </summary>
    public ServiceResult<bool> Initialize(string? dbKey, string? userId)
    {
        // ── Validate db key ────────────────────────────────────
        if (string.IsNullOrWhiteSpace(dbKey))
        {
            _logger.LogWarning("Session initialization failed — db parameter is missing from URL");
            return ServiceResult<bool>.Failure(
                "The 'db' parameter is missing from the URL.",
                ErrorCode.ValidationError);
        }

        // ── Validate db key exists in appsettings ──────────────
        var connectionString = _configuration.GetConnectionString(dbKey);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogWarning(
                "Session initialization failed — db key '{DbKey}' not found in ConnectionStrings",
                dbKey);
            return ServiceResult<bool>.Failure(
                $"The database key '{dbKey}' was not found in the application configuration.",
                ErrorCode.ConfigError);
        }

        // ── Validate userid ────────────────────────────────────
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Session initialization failed — userid parameter is missing from URL");
            return ServiceResult<bool>.Failure(
                "The 'userid' parameter is missing from the URL.",
                ErrorCode.ValidationError);
        }

        // ── All good — set session state ───────────────────────
        DbKey = dbKey.Trim().ToUpper();
        UserId = userId.Trim().ToLower();
        IsValid = true;
        InitializedAt = DateTime.Now;

        _logger.LogInformation(
            "Session initialized — DbKey={DbKey} UserId={UserId} At={InitializedAt:yyyy-MM-dd HH:mm:ss}",
            DbKey, UserId, InitializedAt);

        return ServiceResult<bool>.Success(true);
    }
}
