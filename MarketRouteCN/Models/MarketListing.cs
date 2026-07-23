namespace MarketRouteCN.Models;

public sealed record MarketListing(
    uint ItemId,
    uint WorldId,
    string WorldName,
    int PricePerUnit,
    int Quantity,
    bool IsHighQuality,
    DateTimeOffset? LastReviewTime)
{
    public long TotalPrice => (long)PricePerUnit * Quantity;
}
