using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed class ItemCatalogService
{
    private readonly ItemSearchResult[] allItems;
    private readonly IReadOnlyDictionary<uint, ItemSearchResult> byId;
    private readonly IReadOnlyDictionary<string, ItemSearchResult> byName;

    public ItemCatalogService(IDataManager dataManager, IPluginLog log)
    {
        try
        {
            var sheet = dataManager.GetExcelSheet<Item>();
            allItems = sheet
                .Where(static item => item.RowId > 0 && !item.IsUntradable)
                .Select(static item => new ItemSearchResult(
                    item.RowId,
                    item.Name.ToString(),
                    item.Icon,
                    "可交易物品",
                    item.CanBeHq))
                .Where(static item => !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            byId = allItems.ToDictionary(static item => item.ItemId);
            byName = allItems
                .GroupBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

            log.Information("Loaded {ItemCount} marketable items.", allItems.Length);
        }
        catch (Exception exception)
        {
            log.Error(exception, "Failed to load the local item catalog.");
            allItems = [];
            byId = new Dictionary<uint, ItemSearchResult>();
            byName = new Dictionary<string, ItemSearchResult>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyList<ItemSearchResult> Search(string query, int maximumResults = 30)
    {
        var normalized = query.Trim();
        if (normalized.Length == 0)
            return [];

        return allItems
            .Where(item => item.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => GetMatchRank(item.Name, normalized))
            .ThenBy(item => item.Name.Length)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maximumResults)
            .ToArray();
    }

    public bool TryGetById(uint itemId, out ItemSearchResult result)
    {
        if (byId.TryGetValue(itemId, out var found))
        {
            result = found;
            return true;
        }

        result = null!;
        return false;
    }

    public bool TryGetExactName(string name, out ItemSearchResult result)
    {
        if (byName.TryGetValue(name.Trim(), out var found))
        {
            result = found;
            return true;
        }

        result = null!;
        return false;
    }

    private static int GetMatchRank(string value, string query)
    {
        if (value.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (value.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }
}
