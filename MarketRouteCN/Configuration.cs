using Dalamud.Configuration;
using Dalamud.Plugin;
using MarketRouteCN.Models;

namespace MarketRouteCN;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public int Version { get; set; } = 5;

    public PurchaseScope Scope { get; set; } = PurchaseScope.CompareAllDataCenters;

    public string SelectedDataCenter { get; set; } = "陆行鸟";

    public PurchaseStrategy Strategy { get; set; } = PurchaseStrategy.Balanced;

    public long AdditionalServerSavingsThreshold { get; set; } = 50_000;

    public int OverbuyPenaltyPerUnit { get; set; } = 0;

    public int StaleDataPenaltyPerHour { get; set; } = 0;

    public int AutoRefreshMinutes { get; set; } = 10;

    public int CacheMinutes { get; set; } = 2;

    public int SnapshotHistoryLimit { get; set; } = 12;

    public int StaleDataWarningHours { get; set; } = 2;

    public bool EnableInventorySuggestions { get; set; } = true;

    public Guid ActiveShoppingListId { get; set; }

    public List<SavedShoppingList> ShoppingLists { get; set; } = [];

    public List<SavedQuoteSnapshot> QuoteHistory { get; set; } = [];

    public PurchaseSession? ActiveSession { get; set; }

    public List<ShoppingListEntry> ShoppingList { get; set; } = [];

    public void Initialize(IDalamudPluginInterface value)
    {
        pluginInterface = value;
        ShoppingLists ??= [];
        QuoteHistory ??= [];
        ShoppingList ??= [];

        if (ShoppingLists.Count == 0)
        {
            var migrated = new SavedShoppingList
            {
                Name = "默认清单",
                Entries = ShoppingList.Select(static entry => entry.Clone()).ToList(),
            };
            ShoppingLists.Add(migrated);
            ActiveShoppingListId = migrated.ListId;
            ShoppingList.Clear();
        }

        foreach (var list in ShoppingLists)
        {
            list.Entries ??= [];
            if (string.IsNullOrWhiteSpace(list.Name))
                list.Name = "未命名清单";
        }

        foreach (var snapshot in QuoteHistory)
            snapshot.DataCenters ??= [];

        if (ActiveSession is not null)
        {
            ActiveSession.Requirements ??= [];
            ActiveSession.Listings ??= [];
        }

        if (ShoppingLists.All(list => list.ListId != ActiveShoppingListId))
            ActiveShoppingListId = ShoppingLists[0].ListId;

        Version = 5;
        Save();
    }

    public SavedShoppingList ActiveShoppingList => ShoppingLists.First(list => list.ListId == ActiveShoppingListId);

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
