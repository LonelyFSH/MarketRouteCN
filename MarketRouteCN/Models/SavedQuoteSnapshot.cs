namespace MarketRouteCN.Models;

public sealed class SavedQuoteSnapshot
{
    public Guid SnapshotId { get; set; }

    public Guid ShoppingListId { get; set; }

    public string ShoppingListName { get; set; } = string.Empty;

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }

    public List<SavedDataCenterQuote> DataCenters { get; set; } = [];
}

public sealed class SavedDataCenterQuote
{
    public string DataCenterName { get; set; } = string.Empty;

    public bool IsComplete { get; set; }

    public long TotalCost { get; set; }

    public int ServerCount { get; set; }

    public int CompletedItems { get; set; }

    public int TotalItems { get; set; }

    public DateTimeOffset? OldestMarketDataTime { get; set; }

    public DateTimeOffset? NewestMarketDataTime { get; set; }
}
