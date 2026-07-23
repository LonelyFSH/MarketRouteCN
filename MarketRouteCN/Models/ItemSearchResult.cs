namespace MarketRouteCN.Models;

public sealed record ItemSearchResult(
    uint ItemId,
    string Name,
    uint IconId,
    string Category,
    bool SupportsHighQuality);
