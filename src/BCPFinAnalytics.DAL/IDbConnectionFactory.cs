using Microsoft.Data.SqlClient;

namespace BCPFinAnalytics.DAL;

/// <summary>
/// Resolves a named connection string from appsettings.json by db key
/// and creates an open SqlConnection for use by repositories.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Creates and returns an open SqlConnection for the given db key.
    /// The db key maps directly to a named entry in ConnectionStrings.
    /// </summary>
    Task<SqlConnection> CreateConnectionAsync(string dbKey);
}
