namespace BCPFinAnalytics.Common.Models;

/// <summary>
/// One account's aggregated amount, pre-summed across all entities in scope
/// for the query. Returned by the three canonical data primitives:
///   <c>IGlDataRepository.GetGlStartingBalanceAsync</c>
///   <c>IGlDataRepository.GetGlActivityAsync</c>
///   <c>IBudgetDataRepository.GetBudgetAmountAsync</c>
///
/// Every displayable number in every report is a composition of these three
/// primitives — this record is the shared shape returned by all of them so
/// strategies can treat the three data sources uniformly.
/// </summary>
public sealed record AccountAmount(
    string AcctNum,
    string AcctName,
    string Type,
    decimal Amount);
