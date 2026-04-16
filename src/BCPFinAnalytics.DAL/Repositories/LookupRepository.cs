using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.DAL.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL.Repositories;

/// <summary>
/// Provides all dropdown/lookup queries against the MRI PMX database.
/// Every method follows the same pattern:
///   1. Open connection via factory
///   2. Execute query via Dapper
///   3. Wrap in try/catch — log and rethrow on failure
///   4. Log the SQL at Verbose level for debugging
/// </summary>
public class LookupRepository : ILookupRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<LookupRepository> _logger;

    public LookupRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<LookupRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Format / Report Type dropdown.
    /// SOURCE: GUSR table — financial formats with type B or I.
    /// Used for BOTH the Format dropdown and the Report Type dropdown.
    /// </summary>
    public async Task<IEnumerable<FormatDto>> GetFormatsAsync(string dbKey)
    {
        const string sql = """
            SELECT
                code                                        AS Code,
                '(' + RTRIM(code) + ') ' + RTRIM(name)     AS DisplayName
            FROM GUSR
            WHERE FINANTYP IN ('B', 'I')
            ORDER BY code
            """;

        try
        {
            _logger.LogVerbose("LookupRepository.GetFormatsAsync — DbKey={DbKey} SQL={Sql}", dbKey, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var result = await conn.QueryAsync<FormatDto>(sql);

            _logger.LogDebug("LookupRepository.GetFormatsAsync — returned {Count} rows", result.Count());
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LookupRepository.GetFormatsAsync failed — DbKey={DbKey} SQL={Sql}", dbKey, sql);
            throw;
        }
    }

    /// <summary>
    /// Budget type dropdown.
    /// SOURCE: GBTY table.
    /// </summary>
    public async Task<IEnumerable<BudgetDto>> GetBudgetsAsync(string dbKey)
    {
        const string sql = """
            SELECT
                budtype                                             AS BudType,
                '(' + RTRIM(budtype) + ') ' + RTRIM(descrptn)     AS DisplayName
            FROM GBTY
            ORDER BY budtype
            """;

        try
        {
            _logger.LogVerbose("LookupRepository.GetBudgetsAsync — DbKey={DbKey} SQL={Sql}", dbKey, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var result = await conn.QueryAsync<BudgetDto>(sql);

            _logger.LogDebug("LookupRepository.GetBudgetsAsync — returned {Count} rows", result.Count());
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LookupRepository.GetBudgetsAsync failed — DbKey={DbKey} SQL={Sql}", dbKey, sql);
            throw;
        }
    }

    /// <summary>
    /// Square footage type dropdown.
    /// SOURCE: SQTY table.
    /// </summary>
    public async Task<IEnumerable<SFTypeDto>> GetSFTypesAsync(string dbKey)
    {
        const string sql = """
            SELECT
                SQFTTYPE                                                AS SFType,
                '(' + RTRIM(sqfttype) + ') ' + RTRIM(descrptn)        AS DisplayName
            FROM SQTY
            ORDER BY SQFTTYPE
            """;

        try
        {
            _logger.LogVerbose("LookupRepository.GetSFTypesAsync — DbKey={DbKey} SQL={Sql}", dbKey, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var result = await conn.QueryAsync<SFTypeDto>(sql);

            _logger.LogDebug("LookupRepository.GetSFTypesAsync — returned {Count} rows", result.Count());
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LookupRepository.GetSFTypesAsync failed — DbKey={DbKey} SQL={Sql}", dbKey, sql);
            throw;
        }
    }

    /// <summary>
    /// Basis multi-select list.
    /// SOURCE: BTYP table — excludes basis 'B'.
    /// </summary>
    public async Task<IEnumerable<BasisDto>> GetBasisAsync(string dbKey)
    {
        const string sql = """
            SELECT
                basis       AS Basis,
                DESCRPTN    AS DisplayName
            FROM BTYP
            WHERE basis <> 'B'
            ORDER BY basis
            """;

        try
        {
            _logger.LogVerbose("LookupRepository.GetBasisAsync — DbKey={DbKey} SQL={Sql}", dbKey, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var result = await conn.QueryAsync<BasisDto>(sql);

            _logger.LogDebug("LookupRepository.GetBasisAsync — returned {Count} rows", result.Count());
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LookupRepository.GetBasisAsync failed — DbKey={DbKey} SQL={Sql}", dbKey, sql);
            throw;
        }
    }

    /// <summary>
    /// Entity searchable selection list.
    /// SOURCE: Entity table.
    /// </summary>
    public async Task<IEnumerable<EntityDto>> GetEntitiesAsync(string dbKey)
    {
        const string sql = """
            SELECT
                ENTITYID    AS EntityId,
                NAME        AS Name
            FROM Entity
            ORDER BY ENTITYID
            """;

        try
        {
            _logger.LogVerbose("LookupRepository.GetEntitiesAsync — DbKey={DbKey} SQL={Sql}", dbKey, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var result = await conn.QueryAsync<EntityDto>(sql);

            _logger.LogDebug("LookupRepository.GetEntitiesAsync — returned {Count} rows", result.Count());
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LookupRepository.GetEntitiesAsync failed — DbKey={DbKey} SQL={Sql}", dbKey, sql);
            throw;
        }
    }

    /// <summary>
    /// Project searchable selection list.
    /// SOURCE: PROJ table.
    /// </summary>
    public async Task<IEnumerable<ProjectDto>> GetProjectsAsync(string dbKey)
    {
        const string sql = """
            SELECT
                PROJID      AS ProjId,
                PROJNAME    AS Name
            FROM PROJ
            ORDER BY PROJID
            """;

        try
        {
            _logger.LogVerbose("LookupRepository.GetProjectsAsync — DbKey={DbKey} SQL={Sql}", dbKey, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var result = await conn.QueryAsync<ProjectDto>(sql);

            _logger.LogDebug("LookupRepository.GetProjectsAsync — returned {Count} rows", result.Count());
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "LookupRepository.GetProjectsAsync failed — DbKey={DbKey} SQL={Sql}", dbKey, sql);
            throw;
        }
    }
}
