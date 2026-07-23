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

    public SavedShoppingList CreateWithEntries(string name, IEnumerable<ShoppingListEntry> entries)
    {
        var list = new SavedShoppingList
        {
            Name = NormalizeName(name, "导入采购清单"),
            Entries = MergeEntries(entries).ToList(),
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
        AddEntry(new ShoppingListEntry
        {
            ItemId = item.ItemId,
            DisplayName = item.Name,
            Quantity = quantity,
            Quality = item.SupportsHighQuality ? quality : PurchaseQuality.Any,
            SupportsHighQuality = item.SupportsHighQuality,
        });
    }

    public void AddEntry(ShoppingListEntry source)
    {
        if (source.Quantity == 0)
            return;

        var quality = source.SupportsHighQuality ? source.Quality : PurchaseQuality.Any;
        var existing = ActiveList.Entries.FirstOrDefault(entry =>
            entry.ItemId == source.ItemId && entry.Quality == quality);

        if (existing is not null)
        {
            existing.Quantity = checked(existing.Quantity + source.Quantity);
        }
        else
        {
            var clone = source.Clone();
            clone.EntryId = Guid.NewGuid();
            clone.Quality = quality;
            ActiveList.Entries.Add(clone);
        }

        Touch();
    }

    public void AppendEntries(IEnumerable<ShoppingListEntry> entries)
    {
        foreach (var entry in entries)
        {
            var quality = entry.SupportsHighQuality ? entry.Quality : PurchaseQuality.Any;
            var existing = ActiveList.Entries.FirstOrDefault(item => item.ItemId == entry.ItemId && item.Quality == quality);
            if (existing is not null)
                existing.Quantity = checked(existing.Quantity + entry.Quantity);
            else
            {
                var clone = entry.Clone();
                clone.EntryId = Guid.NewGuid();
                clone.Quality = quality;
                ActiveList.Entries.Add(clone);
            }
        }
        Touch();
    }

    public void ReplaceEntries(IEnumerable<ShoppingListEntry> entries)
    {
        ActiveList.Entries = MergeEntries(entries).ToList();
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
        NormalizeDuplicates();
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

    public void SortByName()
    {
        ActiveList.Entries = ActiveList.Entries
            .OrderBy(static entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Quality)
            .ToList();
        Touch();
    }

    public void SortByQuantity()
    {
        ActiveList.Entries = ActiveList.Entries
            .OrderByDescending(static entry => entry.Quantity)
            .ThenBy(static entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Touch();
    }

    public void Clear()
    {
        ActiveList.Entries.Clear();
        Touch();
    }

    private void NormalizeDuplicates()
    {
        ActiveList.Entries = MergeEntries(ActiveList.Entries).ToList();
    }

    private static IEnumerable<ShoppingListEntry> MergeEntries(IEnumerable<ShoppingListEntry> entries)
    {
        return entries
            .Where(static entry => entry.ItemId > 0 && entry.Quantity > 0)
            .GroupBy(entry => new
            {
                entry.ItemId,
                Quality = entry.SupportsHighQuality ? entry.Quality : PurchaseQuality.Any,
            })
            .Select(group =>
            {
                var first = group.First();
                return new ShoppingListEntry
                {
                    EntryId = Guid.NewGuid(),
                    ItemId = first.ItemId,
                    DisplayName = first.DisplayName,
                    Quantity = checked((uint)group.Sum(static entry => (long)entry.Quantity)),
                    Quality = group.Key.Quality,
                    SupportsHighQuality = first.SupportsHighQuality,
                };
            });
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
