namespace MarketRouteCN.Models;

public sealed class PurchaseSession
{
    public Guid SessionId { get; set; } = Guid.NewGuid();

    public Guid ShoppingListId { get; set; }

    public string ShoppingListName { get; set; } = string.Empty;

    public Guid SnapshotId { get; set; }

    public string DataCenterName { get; set; } = string.Empty;

    public PurchaseStrategy Strategy { get; set; }

    public PurchaseSessionState State { get; set; } = PurchaseSessionState.Active;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int CurrentServerIndex { get; set; }

    public List<SessionRequirement> Requirements { get; set; } = [];

    public List<SessionListing> Listings { get; set; } = [];

    public long PlannedCost => Listings.Sum(static listing => listing.TotalPrice);

    public long ConfirmedCost => Listings.Where(static listing => listing.IsPurchased).Sum(static listing => listing.TotalPrice);

    public int TotalListings => Listings.Count;

    public int PurchasedListings => Listings.Count(static listing => listing.IsPurchased);

    public bool IsComplete => Listings.Count > 0 && Listings.All(static listing => listing.IsPurchased);

    public IReadOnlyList<string> Worlds => Listings
        .Select(static listing => listing.WorldName)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

public sealed class SessionListing
{
    public Guid SessionListingId { get; set; } = Guid.NewGuid();

    public Guid EntryId { get; set; }

    public uint ItemId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public PurchaseQuality RequestedQuality { get; set; }

    public string WorldName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int PricePerUnit { get; set; }

    public bool IsHighQuality { get; set; }

    public DateTimeOffset? MarketDataTime { get; set; }

    public bool IsPurchased { get; set; }

    public long TotalPrice => (long)Quantity * PricePerUnit;
}

public sealed class SessionRequirement
{
    public Guid EntryId { get; set; }

    public uint ItemId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public int RequiredQuantity { get; set; }

    public PurchaseQuality Quality { get; set; }

    public bool SupportsHighQuality { get; set; }
}
