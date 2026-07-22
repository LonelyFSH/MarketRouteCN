using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed class ItemCatalogService
{
    private readonly ItemSearchResult[] allItems;

    // 读取本地可交易物品
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
                    item.CanBeHq))
                .Where(static item => !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            log.Information("Loaded {ItemCount} marketable items from the local game data.", allItems.Length);
        }
        catch (Exception exception)
        {
            log.Error(exception, "Failed to load the local item catalog.");
            allItems = [];
        }
    }

    // 按名称筛选物品
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

    private static int GetMatchRank(string value, string query)
    {
        if (value.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (value.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 1;

        return 2;
    }
}
