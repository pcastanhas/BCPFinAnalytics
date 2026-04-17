using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.DAL.Interfaces;
using Dapper;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.DAL.Repositories;

/// <summary>
/// Loads raw format data from GUSR, MRIGLRW, and GARR.
/// Returns unresolved DTOs — @GRP* group expansion is handled in FormatLoader (Services).
/// All queries wrapped in try/catch with full Serilog logging.
/// </summary>
public class FormatRepository : IFormatRepository
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<FormatRepository> _logger;

    public FormatRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<FormatRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FormatHeaderDto?> GetFormatHeaderAsync(string dbKey, string formatCode)
    {
        const string sql = """
            SELECT
                RTRIM(CODE)      AS Code,
                RTRIM(NAME)      AS Name,
                RTRIM(LEDGCODE)  AS LedgCode,
                RTRIM(FINANTYP)  AS FinanTyp
            FROM GUSR
            WHERE CODE = @FormatCode
            """;

        try
        {
            _logger.LogTrace(
                "FormatRepository.GetFormatHeaderAsync — DbKey={DbKey} FormatCode={Code} SQL={Sql}",
                dbKey, formatCode, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var result = await conn.QuerySingleOrDefaultAsync<FormatHeaderDto>(
                sql, new { FormatCode = formatCode });

            _logger.LogDebug(
                "FormatRepository.GetFormatHeaderAsync — DbKey={DbKey} FormatCode={Code} Found={Found}",
                dbKey, formatCode, result != null);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "FormatRepository.GetFormatHeaderAsync failed — DbKey={DbKey} FormatCode={Code}",
                dbKey, formatCode);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<FormatRowDto>> GetFormatRowsAsync(
        string dbKey, string formatCode)
    {
        const string sql = """
            SELECT
                RTRIM(FORMATID)  AS FormatId,
                SORTORD          AS SortOrd,
                RTRIM(TYPE)      AS Type,
                ISNULL(SUBTOTID, 0) AS SubtotId,
                RTRIM(DEBCRED)   AS DebCred,
                CAST(LINEDEF AS NVARCHAR(MAX)) AS LineDef
            FROM MRIGLRW
            WHERE FORMATID = @FormatCode
            ORDER BY SORTORD
            """;

        try
        {
            _logger.LogTrace(
                "FormatRepository.GetFormatRowsAsync — DbKey={DbKey} FormatCode={Code} SQL={Sql}",
                dbKey, formatCode, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var rows = await conn.QueryAsync<FormatRowDto>(
                sql, new { FormatCode = formatCode });

            var result = rows.ToList();
            _logger.LogDebug(
                "FormatRepository.GetFormatRowsAsync — DbKey={DbKey} FormatCode={Code} Rows={Count}",
                dbKey, formatCode, result.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "FormatRepository.GetFormatRowsAsync failed — DbKey={DbKey} FormatCode={Code}",
                dbKey, formatCode);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AccountRangeDto>> GetGroupRangesAsync(
        string dbKey, string groupId, string ledgCode)
    {
        const string sql = """
            SELECT
                RTRIM(BEGACCT)  AS BegAcct,
                RTRIM(ENDACCT)  AS EndAcct
            FROM GARR
            WHERE GROUPID  = @GroupId
              AND LEDGCODE  = @LedgCode
            ORDER BY BEGACCT
            """;

        try
        {
            _logger.LogTrace(
                "FormatRepository.GetGroupRangesAsync — DbKey={DbKey} GroupId={GroupId} " +
                "LedgCode={LedgCode} SQL={Sql}",
                dbKey, groupId, ledgCode, sql);

            await using var conn = await _connectionFactory.CreateConnectionAsync(dbKey);
            var ranges = await conn.QueryAsync<AccountRangeDto>(
                sql, new { GroupId = groupId, LedgCode = ledgCode });

            var result = ranges.ToList();
            _logger.LogDebug(
                "FormatRepository.GetGroupRangesAsync — DbKey={DbKey} GroupId={GroupId} " +
                "LedgCode={LedgCode} Ranges={Count}",
                dbKey, groupId, ledgCode, result.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "FormatRepository.GetGroupRangesAsync failed — DbKey={DbKey} " +
                "GroupId={GroupId} LedgCode={LedgCode}",
                dbKey, groupId, ledgCode);
            throw;
        }
    }
}
