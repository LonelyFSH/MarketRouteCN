namespace MarketRouteCN.Models;

public sealed record ItemMarketData(
    uint ItemId,
    DateTimeOffset? LastUploadTime,
    IReadOnlyList<MarketListing> Listings,
    MarketDataStatus Status,
    string? ErrorMessage = null)
{
    public DateTimeOffset? NewestListingTime => Listings
        .Select(static listing => listing.LastReviewTime)
        .Where(static time => time.HasValue)
        .Max();

    public DateTimeOffset? OldestListingTime => Listings
        .Select(static listing => listing.LastReviewTime)
        .Where(static time => time.HasValue)
        .Min();
}
