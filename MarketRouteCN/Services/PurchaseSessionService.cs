using Dalamud.Game.Inventory;
using Dalamud.Game.Network.Structures;
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
    private readonly IMarketBoard marketBoard;
    private readonly IPlayerState playerState;
    private readonly IPluginLog log;
    private readonly List<InventorySuggestion> suggestions = [];
    private readonly List<PendingMarketPurchase> pendingPurchases = [];
    private Dictionary<InventoryKey, int> lastInventory = [];
    private DateTimeOffset nextInventoryScan = DateTimeOffset.MinValue;
    private bool scanRequested;
    private string? lastObservedWorld;

    public PurchaseSessionService(
        Configuration configuration,
        IFramework framework,
        IGameInventory gameInventory,
        IMarketBoard marketBoard,
        IPlayerState playerState,
        IPluginLog log)
    {
        this.configuration = configuration;
        this.framework = framework;
        this.gameInventory = gameInventory;
        this.marketBoard = marketBoard;
        this.playerState = playerState;
        this.log = log;

        framework.Update += OnFrameworkUpdate;
        gameInventory.InventoryChanged += OnInventoryChanged;
        marketBoard.ItemPurchased += OnMarketItemPurchased;
        lastInventory = ReadInventoryCounts();
        lastObservedWorld = GetActualWorldName();
    }

    public PurchaseSession? Session => configuration.ActiveSession;

    public IReadOnlyList<InventorySuggestion> Suggestions => suggestions;

    public string? LastAutomaticMessage { get; private set; }

    public string? ActualWorldName => GetActualWorldName();

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        gameInventory.InventoryChanged -= OnInventoryChanged;
        marketBoard.ItemPurchased -= OnMarketItemPurchased;
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
                    DataCenterName = selected.Listing.DataCenterName,
                    WorldName = selected.Listing.WorldName,
                    Quantity = selected.Listing.Quantity,
                    PricePerUnit = selected.Listing.PricePerUnit,
                    IsHighQuality = selected.Listing.IsHighQuality,
                    MarketDataTime = selected.Listing.LastReviewTime,
                })
                .ToList(),
        };
        suggestions.Clear();
        pendingPurchases.Clear();
        LastAutomaticMessage = null;
        lastInventory = ReadInventoryCounts();
        SyncSessionStopToActualWorld();
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
        pendingPurchases.Clear();
        LastAutomaticMessage = null;
        configuration.Save();
    }

    public void SetCurrentWorld(string worldName)
    {
        if (Session is null)
            return;
        var index = Session.Stops.ToList().FindIndex(stop =>
            string.Equals(stop.WorldName, worldName, StringComparison.Ordinal));
        if (index >= 0)
        {
            Session.CurrentServerIndex = index;
            Touch();
        }
    }

    public void SetCurrentStop(string dataCenterName, string worldName)
    {
        if (Session is null)
            return;
        var index = Session.Stops.ToList().FindIndex(stop =>
            string.Equals(stop.DataCenterName, dataCenterName, StringComparison.Ordinal) &&
            string.Equals(stop.WorldName, worldName, StringComparison.Ordinal));
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
        if (!purchased)
        {
            listing.AutoConfirmed = false;
            listing.PurchasedAt = null;
            listing.ActualPricePerUnit = null;
        }
        UpdateSessionState();
        Touch();
    }

    public void SetWorldPurchased(string worldName, bool purchased)
    {
        if (Session is null)
            return;
        foreach (var listing in Session.Listings.Where(item => string.Equals(item.WorldName, worldName, StringComparison.Ordinal)))
        {
            listing.IsPurchased = purchased;
            if (!purchased)
            {
                listing.AutoConfirmed = false;
                listing.PurchasedAt = null;
                listing.ActualPricePerUnit = null;
            }
        }
        UpdateSessionState();
        Touch();
    }

    public void SetStopPurchased(string dataCenterName, string worldName, bool purchased)
    {
        if (Session is null)
            return;

        foreach (var listing in Session.Listings.Where(item =>
                     string.Equals(item.DataCenterName, dataCenterName, StringComparison.Ordinal) &&
                     string.Equals(item.WorldName, worldName, StringComparison.Ordinal)))
        {
            listing.IsPurchased = purchased;
            if (!purchased)
            {
                listing.AutoConfirmed = false;
                listing.PurchasedAt = null;
                listing.ActualPricePerUnit = null;
            }
        }

        UpdateSessionState();
        Touch();
    }

    public PurchaseStop? CurrentStop => Session is null || Session.Stops.Count == 0
        ? null
        : Session.Stops[Math.Clamp(Session.CurrentServerIndex, 0, Session.Stops.Count - 1)];

    public string? CurrentWorld => CurrentStop?.WorldName;

    public void AdvanceToNextWorld()
    {
        AdvanceToNextIncompleteStop();
    }

    public void CompleteCurrentWorldAndAdvance()
    {
        var currentStop = CurrentStop;
        if (currentStop is null || Session is null)
            return;

        foreach (var listing in Session.Listings.Where(item =>
                     string.Equals(item.DataCenterName, currentStop.DataCenterName, StringComparison.Ordinal) &&
                     string.Equals(item.WorldName, currentStop.WorldName, StringComparison.Ordinal)))
            listing.IsPurchased = true;

        UpdateSessionState();
        if (!Session.IsComplete)
            AdvanceToNextIncompleteStop();
        Touch();
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
                     .OrderBy(item => item.WorldName == ActualWorldName ? 0 : 1)
                     .ThenBy(static item => item.WorldName, StringComparer.Ordinal))
        {
            if (remaining < listing.Quantity)
                break;
            listing.IsPurchased = true;
            listing.PurchasedAt = DateTimeOffset.UtcNow;
            remaining -= listing.Quantity;
        }

        suggestions.RemoveAll(item => item.SuggestionId == suggestionId);
        UpdateSessionState();
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
                DataCenterName = selected.Listing.DataCenterName,
                WorldName = selected.Listing.WorldName,
                Quantity = selected.Listing.Quantity,
                PricePerUnit = selected.Listing.PricePerUnit,
                IsHighQuality = selected.Listing.IsHighQuality,
                MarketDataTime = selected.Listing.LastReviewTime,
            });

        Session.Listings = purchased.Concat(remaining).ToList();
        Session.DataCenterName = plan.DataCenterName;
        Session.SnapshotId = snapshot.SnapshotId;
        Session.CurrentServerIndex = 0;
        SyncSessionStopToActualWorld();
        Touch();
    }

    private void OnMarketItemPurchased(IMarketBoardPurchase purchase)
    {
        if (!configuration.AutoMarkMarketPurchases || Session is null || Session.State != PurchaseSessionState.Active)
            return;

        var worldName = GetActualWorldName();
        if (string.IsNullOrWhiteSpace(worldName))
            return;

        var pending = new PendingMarketPurchase(
            purchase.CatalogId,
            checked((int)purchase.ItemQuantity),
            worldName,
            DateTimeOffset.UtcNow);
        pendingPurchases.Add(pending);
        pendingPurchases.RemoveAll(item => DateTimeOffset.UtcNow - item.CreatedAt > TimeSpan.FromSeconds(10));

        if (TryConfirmMarketPurchase(pending, null))
            pendingPurchases.Remove(pending);
    }

    private void OnInventoryChanged(IReadOnlyCollection<Dalamud.Game.Inventory.InventoryEventArgTypes.InventoryEventArgs> _)
    {
        scanRequested = true;
        nextInventoryScan = DateTimeOffset.UtcNow.AddMilliseconds(450);
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (Session is not null && Session.State == PurchaseSessionState.Active)
        {
            var actualWorld = GetActualWorldName();
            if (!string.Equals(actualWorld, lastObservedWorld, StringComparison.Ordinal))
            {
                lastObservedWorld = actualWorld;
                SyncSessionStopToActualWorld();
            }
        }

        pendingPurchases.RemoveAll(item => DateTimeOffset.UtcNow - item.CreatedAt > TimeSpan.FromSeconds(10));

        if (Session is null || Session.State != PurchaseSessionState.Active)
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

                var remainingDelta = delta;
                var matchingPurchases = pendingPurchases
                    .Where(item => item.ItemId == pair.Key.ItemId && item.Quantity <= remainingDelta)
                    .OrderBy(static item => item.CreatedAt)
                    .ToArray();
                foreach (var pending in matchingPurchases)
                {
                    if (pending.Quantity > remainingDelta || !TryConfirmMarketPurchase(pending, pair.Key.IsHighQuality))
                        continue;
                    pendingPurchases.Remove(pending);
                    remainingDelta -= pending.Quantity;
                    if (remainingDelta == 0)
                        break;
                }

                if (remainingDelta == 0 || !configuration.EnableInventorySuggestions)
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
                    remainingDelta,
                    DateTimeOffset.UtcNow));
            }
            lastInventory = current;
        }
        catch (Exception exception)
        {
            log.Warning(exception, "Failed to scan inventory changes.");
        }
    }

    private bool TryConfirmMarketPurchase(PendingMarketPurchase purchase, bool? isHighQuality)
    {
        if (Session is null)
            return false;

        var candidates = Session.Listings
            .Where(listing => !listing.IsPurchased &&
                              listing.ItemId == purchase.ItemId &&
                              listing.Quantity == purchase.Quantity &&
                              string.Equals(listing.WorldName, purchase.WorldName, StringComparison.Ordinal))
            .Where(listing => isHighQuality is null || listing.IsHighQuality == isHighQuality.Value)
            .ToArray();
        if (candidates.Length == 0)
            return false;
        if (isHighQuality is null && candidates.Any(static item => item.RequestedQuality != PurchaseQuality.Any))
            return false;
        if (isHighQuality is null && candidates.Select(static item => item.IsHighQuality).Distinct().Count() > 1)
            return false;

        var listing = candidates
            .OrderBy(item => item.RequestedQuality == PurchaseQuality.Any ? 1 : 0)
            .ThenBy(static item => item.PricePerUnit)
            .First();
        listing.IsPurchased = true;
        listing.AutoConfirmed = true;
        listing.PurchasedAt = DateTimeOffset.UtcNow;
        LastAutomaticMessage = $"已自动记录 {purchase.WorldName} 购买 {listing.ItemName} ×{listing.Quantity}";
        UpdateSessionState();

        var currentStop = CurrentStop;
        if (!Session.IsComplete &&
            configuration.AutoAdvanceCompletedWorld &&
            currentStop is not null &&
            string.Equals(currentStop.DataCenterName, listing.DataCenterName, StringComparison.Ordinal) &&
            string.Equals(currentStop.WorldName, listing.WorldName, StringComparison.Ordinal) &&
            IsStopComplete(listing.DataCenterName, listing.WorldName))
            AdvanceToNextIncompleteStop();

        Touch();
        return true;
    }

    private bool IsStopComplete(string dataCenterName, string worldName)
    {
        return Session is not null && Session.Listings
            .Where(item => string.Equals(item.DataCenterName, dataCenterName, StringComparison.Ordinal) &&
                           string.Equals(item.WorldName, worldName, StringComparison.Ordinal))
            .All(static item => item.IsPurchased);
    }

    private void AdvanceToNextIncompleteStop()
    {
        if (Session is null || Session.Stops.Count == 0)
            return;

        var stops = Session.Stops;
        for (var offset = 1; offset <= stops.Count; offset++)
        {
            var index = (Session.CurrentServerIndex + offset) % stops.Count;
            var stop = stops[index];
            if (!IsStopComplete(stop.DataCenterName, stop.WorldName))
            {
                Session.CurrentServerIndex = index;
                Touch();
                return;
            }
        }
    }

    private void SyncSessionStopToActualWorld()
    {
        if (Session is null)
            return;
        var actualWorld = GetActualWorldName();
        if (string.IsNullOrWhiteSpace(actualWorld))
            return;

        var index = Session.Stops.ToList().FindIndex(stop =>
            string.Equals(stop.WorldName, actualWorld, StringComparison.Ordinal));
        if (index >= 0 && index != Session.CurrentServerIndex)
        {
            Session.CurrentServerIndex = index;
            Touch();
        }
    }

    private string? GetActualWorldName()
    {
        try
        {
            if (!playerState.IsLoaded)
                return null;
            return playerState.CurrentWorld.ValueNullable?.Name.ToString();
        }
        catch
        {
            return null;
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

    private void UpdateSessionState()
    {
        if (Session is null)
            return;
        if (Session.IsComplete)
            Session.State = PurchaseSessionState.Completed;
        else if (Session.State == PurchaseSessionState.Completed)
            Session.State = PurchaseSessionState.Active;
    }

    private void Touch()
    {
        if (Session is not null)
            Session.UpdatedAt = DateTimeOffset.UtcNow;
        configuration.Save();
    }

    private readonly record struct InventoryKey(uint ItemId, bool IsHighQuality);

    private sealed record PendingMarketPurchase(uint ItemId, int Quantity, string WorldName, DateTimeOffset CreatedAt);
}
