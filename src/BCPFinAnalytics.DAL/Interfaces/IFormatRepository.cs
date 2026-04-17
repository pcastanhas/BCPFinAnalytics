using BCPFinAnalytics.Common.DTOs;

namespace BCPFinAnalytics.DAL.Interfaces;

/// <summary>
/// Provides raw format data from GUSR and MRIGLRW.
/// Returns unresolved DTOs — @GRP* expansion happens in the service layer.
/// </summary>
public interface IFormatRepository
{
    /// <summary>
    /// Returns the format header from GUSR for the given format code.
    /// Returns null if the format code does not exist.
    /// </summary>
    Task<FormatHeaderDto?> GetFormatHeaderAsync(string dbKey, string formatCode);

    /// <summary>
    /// Returns all MRIGLRW rows for the given format code, ordered by SORTORD.
    /// </summary>
    Task<IEnumerable<FormatRowDto>> GetFormatRowsAsync(string dbKey, string formatCode);

    /// <summary>
    /// Resolves a named account group from GARR.
    /// Returns all BEGACCT/ENDACCT pairs for the given GROUPID and LEDGCODE.
    /// Called once per @GRP* reference encountered during format loading.
    /// </summary>
    Task<IEnumerable<AccountRangeDto>> GetGroupRangesAsync(
        string dbKey, string groupId, string ledgCode);
}
