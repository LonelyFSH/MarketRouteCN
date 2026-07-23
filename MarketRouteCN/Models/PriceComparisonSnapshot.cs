namespace MarketRouteCN.Models;

public sealed class PriceComparisonSnapshot
{
    public Guid SnapshotId { get; init; } = Guid.NewGuid();

    public Guid ShoppingListId { get; init; }

    public string ShoppingListName { get; init; } = string.Empty;

    public Guid? SourceSessionId { get; init; }

    public required DateTimeOffset RequestedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public required IReadOnlyDictionary<string, DataCenterPurchasePlan> Plans { get; init; }

    public DataCenterPurchasePlan? CheapestCompletePlan => Plans.Values
        .Where(static plan => plan.IsComplete)
        .OrderBy(static plan => plan.TotalCost)
        .ThenBy(static plan => plan.ServerCount)
        .FirstOrDefault();
}
