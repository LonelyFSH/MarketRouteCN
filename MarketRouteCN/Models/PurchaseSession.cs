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

    public long ConfirmedCost => Listings
        .Where(static listing => listing.AcquiredQuantity > 0)
        .Sum(static listing =>
        {
            var unitPrice = listing.ActualPricePerUnit ?? listing.PricePerUnit;
            return (long)listing.AcquiredQuantity * unitPrice;
        });

    public int TotalListings => Listings.Count;

    public int PurchasedListings => Listings.Count(static listing => listing.IsPurchased);

    public int PlannedQuantity => Listings.Sum(static listing => listing.Quantity);

    public int AcquiredQuantity => Listings.Sum(static listing => listing.AcquiredQuantity);

    public bool IsComplete => Listings.Count > 0 && Listings.All(static listing => listing.IsPurchased);

    public IReadOnlyList<PurchaseStop> Stops => Listings
        .GroupBy(static listing => new { listing.DataCenterName, listing.WorldName })
        .Select(static group => new PurchaseStop(group.Key.DataCenterName, group.Key.WorldName))
        .ToArray();

    public IReadOnlyList<string> Worlds => Stops.Select(static stop => stop.WorldName).ToArray();
}

public sealed class SessionListing
{
    public Guid SessionListingId { get; set; } = Guid.NewGuid();

    public Guid EntryId { get; set; }

    public uint ItemId { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public PurchaseQuality RequestedQuality { get; set; }

    public string DataCenterName { get; set; } = string.Empty;

    public string WorldName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int PricePerUnit { get; set; }

    public bool IsHighQuality { get; set; }

    public DateTimeOffset? MarketDataTime { get; set; }

    public bool IsPurchased { get; set; }

    public int AcquiredQuantity { get; set; }

    public bool AutoConfirmed { get; set; }

    public int? ActualPricePerUnit { get; set; }

    public DateTimeOffset? PurchasedAt { get; set; }

    public int RemainingQuantity => Math.Max(0, Quantity - AcquiredQuantity);

    public long TotalPrice => (long)Quantity * PricePerUnit;

    public long? ActualTotalPrice => ActualPricePerUnit is null ? null : (long)Quantity * ActualPricePerUnit.Value;
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

public sealed record PurchaseStop(string DataCenterName, string WorldName)
{
    public string Label => string.IsNullOrWhiteSpace(DataCenterName)
        ? WorldName
        : $"{DataCenterName} · {WorldName}";
}
