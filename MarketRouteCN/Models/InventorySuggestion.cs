namespace MarketRouteCN.Models;

public sealed record InventorySuggestion(
    Guid SuggestionId,
    uint ItemId,
    string ItemName,
    bool IsHighQuality,
    int Quantity,
    DateTimeOffset DetectedAt);
