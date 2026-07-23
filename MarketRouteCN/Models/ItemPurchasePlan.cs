namespace MarketRouteCN.Models;

public sealed class ItemPurchasePlan
{
    public required ShoppingListEntry Request { get; init; }

    public required IReadOnlyList<SelectedListing> SelectedListings { get; init; }

    public required long TotalCost { get; init; }

    public required int PurchasedQuantity { get; init; }

    public required bool IsComplete { get; init; }

    public required MarketDataStatus DataStatus { get; init; }

    public DateTimeOffset? MarketDataTime { get; init; }

    public int EligibleListingCount { get; init; }

    public int LowestUnitPrice { get; init; }

    public bool HasFallbackPlan { get; init; }

    public long FallbackCost { get; init; }

    public int OverbuyQuantity => Math.Max(0, PurchasedQuantity - checked((int)Request.Quantity));

    public long RiskCostIncrease => !IsComplete || TotalCost <= 0
        ? 0
        : HasFallbackPlan
            ? Math.Max(0, FallbackCost - TotalCost)
            : TotalCost;
}
