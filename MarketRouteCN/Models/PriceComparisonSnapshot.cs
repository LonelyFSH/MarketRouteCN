namespace MarketRouteCN.Models;

public sealed class PriceComparisonSnapshot
{
    public required DateTimeOffset QueryTime { get; init; }

    public required IReadOnlyDictionary<string, DataCenterPurchasePlan> Plans { get; init; }

    public DataCenterPurchasePlan? CheapestCompletePlan => Plans.Values
        .Where(static plan => plan.IsComplete)
        .OrderBy(static plan => plan.TotalCost)
        .ThenBy(static plan => plan.ServerCount)
        .FirstOrDefault();
}
