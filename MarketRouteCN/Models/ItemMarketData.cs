namespace MarketRouteCN.Models;

public sealed record ItemMarketData(
    uint ItemId,
    DateTimeOffset? LastUploadTime,
    IReadOnlyList<MarketListing> Listings);
