using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL;

/// <summary>
/// Resolves a named connection string from appsettings.json by db key
/// and returns an open SqlConnection.
///
/// Registered as Singleton — the factory itself is stateless;
/// connections are created fresh per call.
/// </summary>
public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DbConnectionFactory> _logger;

    public DbConnectionFactory(
        IConfiguration configuration,
        ILogger<DbConnectionFactory> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Creates and opens a SqlConnection using the connection string
    /// keyed by dbKey in appsettings.json ConnectionStrings section.
    /// </summary>
    /// <param name="dbKey">The key matching an entry in ConnectionStrings — e.g. "PROD"</param>
    public async Task<SqlConnection> CreateConnectionAsync(string dbKey)
    {
        var connectionString = _configuration.GetConnectionString(dbKey);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            _logger.LogError(
                "DbConnectionFactory — connection string not found for key '{DbKey}'", dbKey);
            throw new InvalidOperationException(
                $"Connection string for key '{dbKey}' was not found in configuration.");
        }

        _logger.LogDebug(
            "DbConnectionFactory — creating connection for DbKey={DbKey}", dbKey);

        var connection = new SqlConnection(connectionString);

        try
        {
            await connection.OpenAsync();
            _logger.LogDebug(
                "DbConnectionFactory — connection opened for DbKey={DbKey}", dbKey);
            return connection;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DbConnectionFactory — failed to open connection for DbKey={DbKey}", dbKey);
            throw;
        }
    }
}
