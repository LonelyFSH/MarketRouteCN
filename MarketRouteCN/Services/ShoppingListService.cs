using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed class ShoppingListService
{
    private readonly Configuration configuration;

    public ShoppingListService(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public IReadOnlyList<SavedShoppingList> Lists => configuration.ShoppingLists;

    public SavedShoppingList ActiveList => configuration.ActiveShoppingList;

    public IReadOnlyList<ShoppingListEntry> Entries => ActiveList.Entries;

    public void SetActive(Guid listId)
    {
        if (configuration.ShoppingLists.All(list => list.ListId != listId))
            return;

        configuration.ActiveShoppingListId = listId;
        configuration.Save();
    }

    public SavedShoppingList Create(string name)
    {
        var list = new SavedShoppingList
        {
            Name = NormalizeName(name, "新采购清单"),
        };
        configuration.ShoppingLists.Add(list);
        configuration.ActiveShoppingListId = list.ListId;
        configuration.Save();
        return list;
    }

    public SavedShoppingList DuplicateActive(string? name = null)
    {
        var source = ActiveList;
        var copy = new SavedShoppingList
        {
            Name = NormalizeName(name, source.Name + " 副本"),
            Entries = source.Entries.Select(static entry =>
            {
                var clone = entry.Clone();
                clone.EntryId = Guid.NewGuid();
                return clone;
            }).ToList(),
        };
        configuration.ShoppingLists.Add(copy);
        configuration.ActiveShoppingListId = copy.ListId;
        configuration.Save();
        return copy;
    }

    public void RenameActive(string name)
    {
        ActiveList.Name = NormalizeName(name, ActiveList.Name);
        Touch();
    }

    public void DeleteActive()
    {
        if (configuration.ShoppingLists.Count <= 1)
        {
            ActiveList.Entries.Clear();
            ActiveList.Name = "默认清单";
            Touch();
            return;
        }

        var removedId = ActiveList.ListId;
        configuration.ShoppingLists.RemoveAll(list => list.ListId == removedId);
        configuration.ActiveShoppingListId = configuration.ShoppingLists[0].ListId;
        configuration.Save();
    }

    public void Add(ItemSearchResult item, uint quantity, PurchaseQuality quality)
    {
        if (quantity == 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        if (!item.SupportsHighQuality)
            quality = PurchaseQuality.Any;

        var existing = ActiveList.Entries.FirstOrDefault(entry =>
            entry.ItemId == item.ItemId && entry.Quality == quality);

        if (existing is not null)
        {
            existing.Quantity = checked(existing.Quantity + quantity);
        }
        else
        {
            ActiveList.Entries.Add(new ShoppingListEntry
            {
                ItemId = item.ItemId,
                DisplayName = item.Name,
                Quantity = quantity,
                Quality = quality,
                SupportsHighQuality = item.SupportsHighQuality,
            });
        }

        Touch();
    }

    public void Remove(Guid entryId)
    {
        ActiveList.Entries.RemoveAll(entry => entry.EntryId == entryId);
        Touch();
    }

    public void UpdateQuantity(Guid entryId, uint quantity)
    {
        var entry = ActiveList.Entries.FirstOrDefault(item => item.EntryId == entryId);
        if (entry is null || quantity == 0)
            return;

        entry.Quantity = quantity;
        Touch();
    }

    public void UpdateQuality(Guid entryId, PurchaseQuality quality)
    {
        var entry = ActiveList.Entries.FirstOrDefault(item => item.EntryId == entryId);
        if (entry is null)
            return;

        entry.Quality = entry.SupportsHighQuality ? quality : PurchaseQuality.Any;
        Touch();
    }

    public void Move(Guid entryId, int offset)
    {
        var entries = ActiveList.Entries;
        var current = entries.FindIndex(entry => entry.EntryId == entryId);
        if (current < 0)
            return;

        var target = Math.Clamp(current + offset, 0, entries.Count - 1);
        if (target == current)
            return;

        var entry = entries[current];
        entries.RemoveAt(current);
        entries.Insert(target, entry);
        Touch();
    }

    public void Clear()
    {
        ActiveList.Entries.Clear();
        Touch();
    }

    private void Touch()
    {
        ActiveList.UpdatedAt = DateTimeOffset.UtcNow;
        configuration.Save();
    }

    private static string NormalizeName(string? value, string fallback)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }
}
