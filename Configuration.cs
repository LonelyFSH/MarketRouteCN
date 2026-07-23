using Dalamud.Configuration;
using Dalamud.Plugin;
using MarketRouteCN.Models;

namespace MarketRouteCN;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public int Version { get; set; } = 10;

    public PurchaseScope Scope { get; set; } = PurchaseScope.CompareAllDataCenters;

    public string SelectedDataCenter { get; set; } = "陆行鸟";

    public PurchaseStrategy Strategy { get; set; } = PurchaseStrategy.Balanced;

    public long AdditionalServerSavingsThreshold { get; set; } = 50_000;

    public long AdditionalDataCenterSavingsThreshold { get; set; } = 200_000;

    public int OverbuyPenaltyPerUnit { get; set; }

    public int StaleDataPenaltyPerHour { get; set; }

    public int AutoRefreshMinutes { get; set; } = 10;

    public int CacheMinutes { get; set; } = 2;

    public int SnapshotHistoryLimit { get; set; } = 20;

    public int StaleDataWarningHours { get; set; } = 2;

    public bool AutoMarkMarketPurchases { get; set; } = true;

    public bool AutoAdvanceCompletedWorld { get; set; } = true;

    public bool EnableInventorySuggestions { get; set; } = true;

    public bool AutoRecordInventoryChanges { get; set; } = true;

    public int InventoryScanIntervalMilliseconds { get; set; } = 500;

    public int InventoryDebounceMilliseconds { get; set; } = 1000;

    public bool EnableAdvancedOptions { get; set; }

    public bool EnableCrossDataCenterAnalysis { get; set; }

    public bool EnableTargetTotalPrice { get; set; }

    public long TargetTotalPriceGil { get; set; } = 1_000_000;

    public bool SimpleInterface { get; set; } = true;

    public bool CompactMode { get; set; }

    public bool ShowOnboarding { get; set; }

    public WorkspacePage LastPage { get; set; } = WorkspacePage.Overview;

    public List<string> RecentSearches { get; set; } = [];

    public Guid ActiveShoppingListId { get; set; }

    public List<SavedShoppingList> ShoppingLists { get; set; } = [];

    public List<SavedQuoteSnapshot> QuoteHistory { get; set; } = [];

    public PurchaseSession? ActiveSession { get; set; }

    public List<ShoppingListEntry> ShoppingList { get; set; } = [];

    public void Initialize(IDalamudPluginInterface value)
    {
        pluginInterface = value;
        var previousVersion = Version;
        ShoppingLists ??= [];
        QuoteHistory ??= [];
        ShoppingList ??= [];
        RecentSearches ??= [];

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
        {
            snapshot.DataCenters ??= [];
            foreach (var quote in snapshot.DataCenters)
            {
                if (quote.RiskAdjustedCost <= 0 && quote.TotalCost > 0)
                    quote.RiskAdjustedCost = quote.TotalCost;
            }
        }

        if (ActiveSession is not null)
        {
            ActiveSession.Requirements ??= [];
            ActiveSession.Listings ??= [];
            foreach (var listing in ActiveSession.Listings)
            {
                if (string.IsNullOrWhiteSpace(listing.DataCenterName) &&
                    !string.Equals(ActiveSession.DataCenterName, Services.DataCenterCatalog.CrossDataCenterPlanName, StringComparison.Ordinal))
                    listing.DataCenterName = ActiveSession.DataCenterName;

                listing.AcquiredQuantity = Math.Clamp(listing.AcquiredQuantity, 0, listing.Quantity);
                if (listing.IsPurchased && listing.AcquiredQuantity < listing.Quantity)
                    listing.AcquiredQuantity = listing.Quantity;
                if (!listing.IsPurchased && listing.AcquiredQuantity >= listing.Quantity)
                    listing.IsPurchased = true;
            }
        }

        RecentSearches = RecentSearches
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (ShoppingLists.All(list => list.ListId != ActiveShoppingListId))
            ActiveShoppingListId = ShoppingLists[0].ListId;

        if (!Enum.IsDefined(typeof(WorkspacePage), LastPage))
            LastPage = WorkspacePage.Overview;

        if (!EnableAdvancedOptions && Scope == PurchaseScope.CrossDataCenterMixed)
            Scope = PurchaseScope.CompareAllDataCenters;

        if (previousVersion < 9)
        {
            SimpleInterface = true;
            ShowOnboarding = false;
            AutoMarkMarketPurchases = true;
            AutoAdvanceCompletedWorld = true;
        }

        if (previousVersion < 10)
        {
            AutoRecordInventoryChanges = true;
            InventoryScanIntervalMilliseconds = 500;
            InventoryDebounceMilliseconds = 1000;
        }

        InventoryScanIntervalMilliseconds = Math.Clamp(InventoryScanIntervalMilliseconds, 250, 2000);
        InventoryDebounceMilliseconds = Math.Clamp(InventoryDebounceMilliseconds, 500, 3000);

        Version = 10;
        Save();
    }

    public SavedShoppingList ActiveShoppingList => ShoppingLists.First(list => list.ListId == ActiveShoppingListId);

    public void RememberSearch(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
            return;

        RecentSearches.RemoveAll(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase));
        RecentSearches.Insert(0, normalized);
        if (RecentSearches.Count > 8)
            RecentSearches.RemoveRange(8, RecentSearches.Count - 8);
        Save();
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
