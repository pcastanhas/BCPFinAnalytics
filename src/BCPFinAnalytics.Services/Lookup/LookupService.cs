using BCPFinAnalytics.Common.DTOs;
using BCPFinAnalytics.Common.Interfaces;
using BCPFinAnalytics.Common.Models;
using BCPFinAnalytics.Common.Wrappers;
using BCPFinAnalytics.DAL.Interfaces;
using BCPFinAnalytics.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Lookup;

/// <summary>
/// Wraps ILookupRepository — provides all dropdown data to the UI.
/// Fully implemented in Phase 4.
/// </summary>
public class LookupService : ILookupService
{
    private readonly ILookupRepository _repo;
    private readonly ILogger<LookupService> _logger;

    public LookupService(ILookupRepository repo, ILogger<LookupService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<ServiceResult<IEnumerable<FormatDto>>> GetFormatsAsync(string dbKey)
    {
        try
        {
            _logger.LogDebug("LookupService.GetFormatsAsync — DbKey={DbKey}", dbKey);
            var data = await _repo.GetFormatsAsync(dbKey);
            return ServiceResult<IEnumerable<FormatDto>>.Success(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LookupService.GetFormatsAsync failed — DbKey={DbKey}", dbKey);
            return ServiceResult<IEnumerable<FormatDto>>.FromException(ex,
                ErrorCode.DatabaseError);
        }
    }

    public async Task<ServiceResult<IEnumerable<BudgetDto>>> GetBudgetsAsync(string dbKey)
    {
        try
        {
            _logger.LogDebug("LookupService.GetBudgetsAsync — DbKey={DbKey}", dbKey);
            var data = await _repo.GetBudgetsAsync(dbKey);
            return ServiceResult<IEnumerable<BudgetDto>>.Success(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LookupService.GetBudgetsAsync failed — DbKey={DbKey}", dbKey);
            return ServiceResult<IEnumerable<BudgetDto>>.FromException(ex,
                ErrorCode.DatabaseError);
        }
    }

    public async Task<ServiceResult<IEnumerable<SFTypeDto>>> GetSFTypesAsync(string dbKey)
    {
        try
        {
            _logger.LogDebug("LookupService.GetSFTypesAsync — DbKey={DbKey}", dbKey);
            var data = await _repo.GetSFTypesAsync(dbKey);
            return ServiceResult<IEnumerable<SFTypeDto>>.Success(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LookupService.GetSFTypesAsync failed — DbKey={DbKey}", dbKey);
            return ServiceResult<IEnumerable<SFTypeDto>>.FromException(ex,
                ErrorCode.DatabaseError);
        }
    }

    public async Task<ServiceResult<IEnumerable<BasisDto>>> GetBasisAsync(string dbKey)
    {
        try
        {
            _logger.LogDebug("LookupService.GetBasisAsync — DbKey={DbKey}", dbKey);
            var data = await _repo.GetBasisAsync(dbKey);
            return ServiceResult<IEnumerable<BasisDto>>.Success(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LookupService.GetBasisAsync failed — DbKey={DbKey}", dbKey);
            return ServiceResult<IEnumerable<BasisDto>>.FromException(ex,
                ErrorCode.DatabaseError);
        }
    }

    public async Task<ServiceResult<IEnumerable<EntityDto>>> GetEntitiesAsync(string dbKey)
    {
        try
        {
            _logger.LogDebug("LookupService.GetEntitiesAsync — DbKey={DbKey}", dbKey);
            var data = await _repo.GetEntitiesAsync(dbKey);
            return ServiceResult<IEnumerable<EntityDto>>.Success(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LookupService.GetEntitiesAsync failed — DbKey={DbKey}", dbKey);
            return ServiceResult<IEnumerable<EntityDto>>.FromException(ex,
                ErrorCode.DatabaseError);
        }
    }

    public async Task<ServiceResult<IEnumerable<ProjectDto>>> GetProjectsAsync(string dbKey)
    {
        try
        {
            _logger.LogDebug("LookupService.GetProjectsAsync — DbKey={DbKey}", dbKey);
            var data = await _repo.GetProjectsAsync(dbKey);
            return ServiceResult<IEnumerable<ProjectDto>>.Success(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LookupService.GetProjectsAsync failed — DbKey={DbKey}", dbKey);
            return ServiceResult<IEnumerable<ProjectDto>>.FromException(ex,
                ErrorCode.DatabaseError);
        }
    }
}
