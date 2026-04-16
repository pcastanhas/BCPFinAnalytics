using BCPFinAnalytics.Common.DTOs;

namespace BCPFinAnalytics.DAL.Interfaces;

/// <summary>
/// Provides all dropdown/lookup data queries against the MRI PMX database.
/// All methods return raw DTOs — no business logic applied here.
/// </summary>
public interface ILookupRepository
{
    Task<IEnumerable<FormatDto>> GetFormatsAsync(string dbKey);
    Task<IEnumerable<BudgetDto>> GetBudgetsAsync(string dbKey);
    Task<IEnumerable<SFTypeDto>> GetSFTypesAsync(string dbKey);
    Task<IEnumerable<BasisDto>> GetBasisAsync(string dbKey);
    Task<IEnumerable<EntityDto>> GetEntitiesAsync(string dbKey);
    Task<IEnumerable<ProjectDto>> GetProjectsAsync(string dbKey);
}
