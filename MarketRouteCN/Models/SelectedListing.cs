namespace MarketRouteCN.Models;

public sealed record SelectedListing(
    uint ItemId,
    string ItemName,
    PurchaseQuality RequestedQuality,
    MarketListing Listing);
