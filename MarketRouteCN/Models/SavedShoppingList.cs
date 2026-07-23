namespace MarketRouteCN.Models;

public sealed class SavedShoppingList
{
    public Guid ListId { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "默认清单";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ShoppingListEntry> Entries { get; set; } = [];
}
