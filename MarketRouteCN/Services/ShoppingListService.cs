using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed class ShoppingListService
{
    private readonly Configuration configuration;

    public ShoppingListService(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public IReadOnlyList<ShoppingListEntry> Entries => configuration.ShoppingList;

    // 添加或合并清单物品
    public void Add(ItemSearchResult item, uint quantity, PurchaseQuality quality)
    {
        if (quantity == 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        if (!item.SupportsHighQuality)
            quality = PurchaseQuality.Any;

        var existing = configuration.ShoppingList.FirstOrDefault(entry =>
            entry.ItemId == item.ItemId && entry.Quality == quality);

        if (existing is not null)
        {
            existing.Quantity = checked(existing.Quantity + quantity);
        }
        else
        {
            configuration.ShoppingList.Add(new ShoppingListEntry
            {
                EntryId = Guid.NewGuid(),
                ItemId = item.ItemId,
                DisplayName = item.Name,
                Quantity = quantity,
                Quality = quality,
                SupportsHighQuality = item.SupportsHighQuality,
            });
        }

        configuration.Save();
    }

    public void Remove(Guid entryId)
    {
        configuration.ShoppingList.RemoveAll(entry => entry.EntryId == entryId);
        configuration.Save();
    }

    public void UpdateQuantity(Guid entryId, uint quantity)
    {
        var entry = configuration.ShoppingList.FirstOrDefault(item => item.EntryId == entryId);
        if (entry is null || quantity == 0)
            return;

        entry.Quantity = quantity;
        configuration.Save();
    }

    public void Clear()
    {
        configuration.ShoppingList.Clear();
        configuration.Save();
    }
}
