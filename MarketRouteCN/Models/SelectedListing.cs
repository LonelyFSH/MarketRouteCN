namespace MarketRouteCN.Models;

public sealed record SelectedListing(
    Guid SelectionId,
    Guid EntryId,
    uint ItemId,
    string ItemName,
    PurchaseQuality RequestedQuality,
    MarketListing Listing);
