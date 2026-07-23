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

    public int OverbuyQuantity => Math.Max(0, PurchasedQuantity - checked((int)Request.Quantity));
}
