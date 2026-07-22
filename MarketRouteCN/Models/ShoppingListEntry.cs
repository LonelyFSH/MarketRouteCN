namespace MarketRouteCN.Models;

public sealed class ShoppingListEntry
{
    public Guid EntryId { get; set; } = Guid.NewGuid();

    public uint ItemId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public uint Quantity { get; set; } = 1;

    public PurchaseQuality Quality { get; set; } = PurchaseQuality.Any;

    public bool SupportsHighQuality { get; set; }
}
