namespace MarketRouteCN.Models;

public sealed class ServerPurchasePlan
{
    public required string WorldName { get; init; }

    public required IReadOnlyList<SelectedListing> Listings { get; init; }

    public long TotalCost => Listings.Sum(static item => item.Listing.TotalPrice);

    public int TotalUnits => Listings.Sum(static item => item.Listing.Quantity);
}
