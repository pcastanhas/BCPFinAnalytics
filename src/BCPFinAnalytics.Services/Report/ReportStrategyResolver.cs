using BCPFinAnalytics.Common.Interfaces;
using BCPFinAnalytics.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace BCPFinAnalytics.Services.Report;

/// <summary>
/// Resolves the correct IReportStrategy implementation for a given report code.
///
/// Each report strategy is registered in the _strategies dictionary.
/// As each report is built in later phases, it is added here.
/// The UI and Services layer use this resolver — they never reference
/// concrete strategy types directly.
/// </summary>
public class ReportStrategyResolver : IReportStrategyResolver
{
    private readonly ILogger<ReportStrategyResolver> _logger;
    private readonly Dictionary<string, IReportStrategy> _strategies;

    public ReportStrategyResolver(
        ILogger<ReportStrategyResolver> logger,
        IEnumerable<IReportStrategy> strategies)
    {
        _logger = logger;

        // Build lookup dictionary from all registered strategies
        _strategies = strategies.ToDictionary(
            s => s.ReportCode.ToUpper(),
            s => s);

        _logger.LogDebug(
            "ReportStrategyResolver initialized with {Count} strategies: {Codes}",
            _strategies.Count,
            string.Join(", ", _strategies.Keys));
    }

    /// <summary>
    /// Returns the strategy for the given report code.
    /// Throws InvalidOperationException if the code is not registered.
    /// </summary>
    public IReportStrategy Resolve(string reportCode)
    {
        if (string.IsNullOrWhiteSpace(reportCode))
            throw new ArgumentException("Report code cannot be null or empty.", nameof(reportCode));

        var key = reportCode.ToUpper();

        if (!_strategies.TryGetValue(key, out var strategy))
        {
            _logger.LogError("ReportStrategyResolver — no strategy found for code '{ReportCode}'", reportCode);
            throw new InvalidOperationException($"No report strategy registered for code '{reportCode}'.");
        }

        _logger.LogDebug("ReportStrategyResolver — resolved '{ReportCode}' to {StrategyType}",
            reportCode, strategy.GetType().Name);

        return strategy;
    }

    /// <summary>
    /// Returns all registered report types as (Code, Name) pairs.
    /// Used to populate the Report Type dropdown in the UI.
    /// </summary>
    public IEnumerable<(string Code, string Name)> GetAllReportTypes()
    {
        return _strategies.Values
            .OrderBy(s => s.ReportName)
            .Select(s => (s.ReportCode, s.ReportName));
    }
}
