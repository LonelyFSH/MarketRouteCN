using Dalamud.Game.Inventory;
using Dalamud.Plugin.Services;
using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed class PurchaseSessionService : IDisposable
{
    private static readonly GameInventoryType[] TrackedInventories =
    [
        GameInventoryType.Inventory1,
        GameInventoryType.Inventory2,
        GameInventoryType.Inventory3,
        GameInventoryType.Inventory4,
    ];

    private readonly Configuration configuration;
    private readonly IFramework framework;
    private readonly IGameInventory gameInventory;
    private readonly IPluginLog log;
    private readonly List<InventorySuggestion> suggestions = [];
    private Dictionary<InventoryKey, int> lastInventory = [];
    private DateTimeOffset nextInventoryScan = DateTimeOffset.MinValue;
    private bool scanRequested;

    public PurchaseSessionService(
        Configuration configuration,
        IFramework framework,
        IGameInventory gameInventory,
        IPluginLog log)
    {
        this.configuration = configuration;
        this.framework = framework;
        this.gameInventory = gameInventory;
        this.log = log;

        framework.Update += OnFrameworkUpdate;
        gameInventory.InventoryChanged += OnInventoryChanged;
        lastInventory = ReadInventoryCounts();
    }

    public PurchaseSession? Session => configuration.ActiveSession;

    public IReadOnlyList<InventorySuggestion> Suggestions => suggestions;

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        gameInventory.InventoryChanged -= OnInventoryChanged;
    }

    public void Start(PriceComparisonSnapshot snapshot, DataCenterPurchasePlan plan)
    {
        configuration.ActiveSession = new PurchaseSession
        {
            ShoppingListId = snapshot.ShoppingListId,
            ShoppingListName = snapshot.ShoppingListName,
            SnapshotId = snapshot.SnapshotId,
            DataCenterName = plan.DataCenterName,
            Strategy = plan.Strategy,
            Requirements = plan.ItemPlans
                .Select(static item => new SessionRequirement
                {
                    EntryId = item.Request.EntryId,
                    ItemId = item.Request.ItemId,
                    ItemName = item.Request.DisplayName,
                    RequiredQuantity = checked((int)item.Request.Quantity),
                    Quality = item.Request.Quality,
                    SupportsHighQuality = item.Request.SupportsHighQuality,
                })
                .ToList(),
            Listings = plan.ServerPlans
                .SelectMany(static server => server.Listings)
                .Select(static selected => new SessionListing
                {
                    EntryId = selected.EntryId,
                    ItemId = selected.ItemId,
                    ItemName = selected.ItemName,
                    RequestedQuality = selected.RequestedQuality,
                    WorldName = selected.Listing.WorldName,
                    Quantity = selected.Listing.Quantity,
                    PricePerUnit = selected.Listing.PricePerUnit,
                    IsHighQuality = selected.Listing.IsHighQuality,
                    MarketDataTime = selected.Listing.LastReviewTime,
                })
                .ToList(),
        };
        suggestions.Clear();
        lastInventory = ReadInventoryCounts();
        configuration.Save();
    }

    public void Pause()
    {
        if (Session is null)
            return;
        Session.State = PurchaseSessionState.Paused;
        Touch();
    }

    public void Resume()
    {
        if (Session is null)
            return;
        Session.State = PurchaseSessionState.Active;
        Touch();
    }

    public void Cancel()
    {
        if (Session is null)
            return;
        Session.State = PurchaseSessionState.Cancelled;
        Touch();
    }

    public void Finish()
    {
        if (Session is null)
            return;
        Session.State = PurchaseSessionState.Completed;
        Touch();
    }

    public void RemoveSession()
    {
        configuration.ActiveSession = null;
        suggestions.Clear();
        configuration.Save();
    }

    public void SetCurrentWorld(string worldName)
    {
        if (Session is null)
            return;
        var worlds = Session.Worlds;
        var index = worlds.ToList().FindIndex(world => string.Equals(world, worldName, StringComparison.Ordinal));
        if (index >= 0)
        {
            Session.CurrentServerIndex = index;
            Touch();
        }
    }

    public void SetListingPurchased(Guid sessionListingId, bool purchased)
    {
        var listing = Session?.Listings.FirstOrDefault(item => item.SessionListingId == sessionListingId);
        if (listing is null)
            return;
        listing.IsPurchased = purchased;
        if (Session!.IsComplete)
            Session.State = PurchaseSessionState.Completed;
        else if (Session.State == PurchaseSessionState.Completed)
            Session.State = PurchaseSessionState.Active;
        Touch();
    }

    public void SetWorldPurchased(string worldName, bool purchased)
    {
        if (Session is null)
            return;
        foreach (var listing in Session.Listings.Where(item => string.Equals(item.WorldName, worldName, StringComparison.Ordinal)))
            listing.IsPurchased = purchased;
        if (Session.IsComplete)
            Session.State = PurchaseSessionState.Completed;
        else if (Session.State == PurchaseSessionState.Completed)
            Session.State = PurchaseSessionState.Active;
        Touch();
    }

    public string? CurrentWorld => Session is null || Session.Worlds.Count == 0
        ? null
        : Session.Worlds[Math.Clamp(Session.CurrentServerIndex, 0, Session.Worlds.Count - 1)];

    public void AdvanceToNextWorld()
    {
        if (Session is null || Session.Worlds.Count == 0)
            return;

        Session.CurrentServerIndex = Math.Min(Session.CurrentServerIndex + 1, Session.Worlds.Count - 1);
        Touch();
    }

    public void CompleteCurrentWorldAndAdvance()
    {
        var currentWorld = CurrentWorld;
        if (currentWorld is null)
            return;

        SetWorldPurchased(currentWorld, true);
        if (Session is not null && !Session.IsComplete)
            AdvanceToNextWorld();
    }

    public void ApplyInventorySuggestion(Guid suggestionId)
    {
        var suggestion = suggestions.FirstOrDefault(item => item.SuggestionId == suggestionId);
        if (suggestion is null || Session is null)
            return;

        var remaining = suggestion.Quantity;
        foreach (var listing in Session.Listings
                     .Where(item => !item.IsPurchased && item.ItemId == suggestion.ItemId &&
                                    (item.RequestedQuality == PurchaseQuality.Any || item.IsHighQuality == suggestion.IsHighQuality))
                     .OrderBy(static item => item.WorldName, StringComparer.Ordinal))
        {
            if (remaining < listing.Quantity)
                break;
            listing.IsPurchased = true;
            remaining -= listing.Quantity;
        }

        suggestions.RemoveAll(item => item.SuggestionId == suggestionId);
        if (Session.IsComplete)
            Session.State = PurchaseSessionState.Completed;
        Touch();
    }

    public void IgnoreInventorySuggestion(Guid suggestionId)
    {
        suggestions.RemoveAll(item => item.SuggestionId == suggestionId);
    }

    public void ReplaceRemainingPlan(PriceComparisonSnapshot snapshot, DataCenterPurchasePlan plan)
    {
        if (Session is null || snapshot.SourceSessionId != Session.SessionId)
            return;

        var purchased = Session.Listings.Where(static listing => listing.IsPurchased).ToList();
        var remaining = plan.ServerPlans
            .SelectMany(static server => server.Listings)
            .Select(static selected => new SessionListing
            {
                EntryId = selected.EntryId,
                ItemId = selected.ItemId,
                ItemName = selected.ItemName,
                RequestedQuality = selected.RequestedQuality,
                WorldName = selected.Listing.WorldName,
                Quantity = selected.Listing.Quantity,
                PricePerUnit = selected.Listing.PricePerUnit,
                IsHighQuality = selected.Listing.IsHighQuality,
                MarketDataTime = selected.Listing.LastReviewTime,
            });

        Session.Listings = purchased.Concat(remaining).ToList();
        Session.SnapshotId = snapshot.SnapshotId;
        Session.CurrentServerIndex = 0;
        Touch();
    }

    private void OnInventoryChanged(IReadOnlyCollection<Dalamud.Game.Inventory.InventoryEventArgTypes.InventoryEventArgs> _)
    {
        scanRequested = true;
        nextInventoryScan = DateTimeOffset.UtcNow.AddMilliseconds(500);
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!configuration.EnableInventorySuggestions || Session is null || Session.State != PurchaseSessionState.Active)
            return;
        if (!scanRequested || DateTimeOffset.UtcNow < nextInventoryScan)
            return;

        scanRequested = false;
        ScanInventoryChanges();
    }

    private void ScanInventoryChanges()
    {
        try
        {
            var current = ReadInventoryCounts();
            foreach (var pair in current)
            {
                var previous = lastInventory.GetValueOrDefault(pair.Key);
                var delta = pair.Value - previous;
                if (delta <= 0)
                    continue;

                var matching = Session?.Listings.FirstOrDefault(listing =>
                    !listing.IsPurchased && listing.ItemId == pair.Key.ItemId &&
                    (listing.RequestedQuality == PurchaseQuality.Any || listing.IsHighQuality == pair.Key.IsHighQuality));
                if (matching is null)
                    continue;

                suggestions.Add(new InventorySuggestion(
                    Guid.NewGuid(),
                    pair.Key.ItemId,
                    matching.ItemName,
                    pair.Key.IsHighQuality,
                    delta,
                    DateTimeOffset.UtcNow));
            }
            lastInventory = current;
        }
        catch (Exception exception)
        {
            log.Warning(exception, "Failed to scan inventory changes.");
        }
    }

    private Dictionary<InventoryKey, int> ReadInventoryCounts()
    {
        var counts = new Dictionary<InventoryKey, int>();
        foreach (var inventoryType in TrackedInventories)
        {
            foreach (var item in gameInventory.GetInventoryItems(inventoryType))
            {
                if (item.IsEmpty || item.BaseItemId == 0 || item.Quantity <= 0)
                    continue;
                var key = new InventoryKey(item.BaseItemId, item.IsHq);
                counts[key] = counts.GetValueOrDefault(key) + item.Quantity;
            }
        }
        return counts;
    }

    private void Touch()
    {
        if (Session is not null)
            Session.UpdatedAt = DateTimeOffset.UtcNow;
        configuration.Save();
    }

    private readonly record struct InventoryKey(uint ItemId, bool IsHighQuality);
}
