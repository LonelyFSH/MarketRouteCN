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
    private readonly Dictionary<InventoryKey, PendingInventoryDelta> pendingInventoryDeltas = [];
    private Dictionary<InventoryKey, int> lastInventory = [];
    private AutomaticRecord? lastAutomaticRecord;
    private DateTimeOffset nextInventoryScan = DateTimeOffset.MinValue;
    private string? lastObservedWorld;
    private bool inventoryTrackingActive;

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

        ResetInventoryTracking();
        inventoryTrackingActive = Session?.State == PurchaseSessionState.Active;
        lastObservedWorld = GetActualWorldName();
    }

    public PurchaseSession? Session => configuration.ActiveSession;

    public IReadOnlyList<InventorySuggestion> Suggestions => suggestions;

    public string? LastAutomaticMessage { get; private set; }

    public bool CanUndoLastAutomaticRecord => lastAutomaticRecord is not null;

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
        pendingInventoryDeltas.Clear();
        lastAutomaticRecord = null;
        LastAutomaticMessage = null;
        inventoryTrackingActive = true;
        ResetInventoryTracking();
        SyncSessionStopToActualWorld();
        configuration.Save();
    }

    public void Pause()
    {
        if (Session is null)
            return;

        Session.State = PurchaseSessionState.Paused;
        StopInventoryTracking();
        Touch();
    }

    public void Resume()
    {
        if (Session is null)
            return;

        Session.State = PurchaseSessionState.Active;
        inventoryTrackingActive = true;
        ResetInventoryTracking();
        Touch();
    }

    public void Cancel()
    {
        if (Session is null)
            return;

        Session.State = PurchaseSessionState.Cancelled;
        StopInventoryTracking();
        Touch();
    }

    public void Finish()
    {
        if (Session is null)
            return;

        Session.State = PurchaseSessionState.Completed;
        StopInventoryTracking();
        Touch();
    }

    public void RemoveSession()
    {
        configuration.ActiveSession = null;
        suggestions.Clear();
        pendingPurchases.Clear();
        pendingInventoryDeltas.Clear();
        lastAutomaticRecord = null;
        LastAutomaticMessage = null;
        inventoryTrackingActive = false;
        configuration.Save();
    }

    public void SetCurrentWorld(string worldName)
    {
        if (Session is null)
            return;

        var index = Session.Stops.ToList().FindIndex(stop =>
            string.Equals(stop.WorldName, worldName, StringComparison.Ordinal));
        if (index < 0)
            return;

        Session.CurrentServerIndex = index;
        Touch();
    }

    public void SetCurrentStop(string dataCenterName, string worldName)
    {
        if (Session is null)
            return;

        var index = Session.Stops.ToList().FindIndex(stop =>
            string.Equals(stop.DataCenterName, dataCenterName, StringComparison.Ordinal) &&
            string.Equals(stop.WorldName, worldName, StringComparison.Ordinal));
        if (index < 0)
            return;

        Session.CurrentServerIndex = index;
        Touch();
    }

    public void SetListingPurchased(Guid sessionListingId, bool purchased)
    {
        var listing = Session?.Listings.FirstOrDefault(item => item.SessionListingId == sessionListingId);
        if (listing is null)
            return;

        listing.IsPurchased = purchased;
        listing.AcquiredQuantity = purchased ? listing.Quantity : 0;
        if (!purchased)
        {
            listing.AutoConfirmed = false;
            listing.PurchasedAt = null;
            listing.ActualPricePerUnit = null;
        }
        else
        {
            listing.PurchasedAt ??= DateTimeOffset.UtcNow;
        }

        lastAutomaticRecord = null;
        UpdateSessionState();
        Touch();
    }

    public void SetWorldPurchased(string worldName, bool purchased)
    {
        if (Session is null)
            return;

        foreach (var listing in Session.Listings.Where(item =>
                     string.Equals(item.WorldName, worldName, StringComparison.Ordinal)))
            SetPurchasedState(listing, purchased);

        lastAutomaticRecord = null;
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
            SetPurchasedState(listing, purchased);

        lastAutomaticRecord = null;
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
            SetPurchasedState(listing, true);

        lastAutomaticRecord = null;
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

        var actualWorld = GetActualWorldName();
        var remaining = suggestion.Quantity;
        if (!string.IsNullOrWhiteSpace(actualWorld))
            remaining -= ApplyProgress(
                suggestion.ItemId,
                suggestion.IsHighQuality,
                remaining,
                actualWorld,
                false,
                null);

        if (remaining > 0)
            ApplyProgress(
                suggestion.ItemId,
                suggestion.IsHighQuality,
                remaining,
                null,
                false,
                null);

        suggestions.RemoveAll(item => item.SuggestionId == suggestionId);
        lastAutomaticRecord = null;
        UpdateSessionState();
        Touch();
    }

    public void IgnoreInventorySuggestion(Guid suggestionId)
    {
        suggestions.RemoveAll(item => item.SuggestionId == suggestionId);
    }

    public void UndoLastAutomaticRecord()
    {
        if (Session is null || lastAutomaticRecord is null)
            return;

        foreach (var change in lastAutomaticRecord.Changes)
        {
            var listing = Session.Listings.FirstOrDefault(item =>
                item.SessionListingId == change.SessionListingId);
            if (listing is null)
                continue;

            listing.AcquiredQuantity = change.PreviousAcquiredQuantity;
            listing.IsPurchased = change.PreviousIsPurchased;
            listing.AutoConfirmed = change.PreviousAutoConfirmed;
            listing.PurchasedAt = change.PreviousPurchasedAt;
        }

        Session.State = lastAutomaticRecord.PreviousState;
        Session.CurrentServerIndex = Math.Clamp(
            lastAutomaticRecord.PreviousServerIndex,
            0,
            Math.Max(0, Session.Stops.Count - 1));

        LastAutomaticMessage = "已撤销最近一次自动记录";
        lastAutomaticRecord = null;
        Touch();
    }

    public void ReplaceRemainingPlan(PriceComparisonSnapshot snapshot, DataCenterPurchasePlan plan)
    {
        if (Session is null || snapshot.SourceSessionId != Session.SessionId)
            return;

        var acquired = Session.Listings
            .Where(static listing => listing.AcquiredQuantity > 0)
            .Select(static listing => new SessionListing
            {
                SessionListingId = listing.SessionListingId,
                EntryId = listing.EntryId,
                ItemId = listing.ItemId,
                ItemName = listing.ItemName,
                RequestedQuality = listing.RequestedQuality,
                DataCenterName = listing.DataCenterName,
                WorldName = listing.WorldName,
                Quantity = listing.AcquiredQuantity,
                AcquiredQuantity = listing.AcquiredQuantity,
                PricePerUnit = listing.PricePerUnit,
                IsHighQuality = listing.IsHighQuality,
                MarketDataTime = listing.MarketDataTime,
                IsPurchased = true,
                AutoConfirmed = listing.AutoConfirmed,
                ActualPricePerUnit = listing.ActualPricePerUnit,
                PurchasedAt = listing.PurchasedAt,
            });

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

        Session.Listings = acquired.Concat(remaining).ToList();
        Session.DataCenterName = plan.DataCenterName;
        Session.SnapshotId = snapshot.SnapshotId;
        Session.CurrentServerIndex = 0;
        Session.State = Session.IsComplete
            ? PurchaseSessionState.Completed
            : PurchaseSessionState.Active;

        suggestions.Clear();
        pendingPurchases.Clear();
        pendingInventoryDeltas.Clear();
        lastAutomaticRecord = null;
        inventoryTrackingActive = Session.State == PurchaseSessionState.Active;
        ResetInventoryTracking();
        SyncSessionStopToActualWorld();
        Touch();
    }

    private void OnMarketItemPurchased(IMarketBoardPurchase purchase)
    {
        if (!configuration.AutoMarkMarketPurchases ||
            Session is null ||
            Session.State != PurchaseSessionState.Active)
            return;

        var worldName = GetActualWorldName();
        if (string.IsNullOrWhiteSpace(worldName))
            return;

        pendingPurchases.Add(new PendingMarketPurchase(
            purchase.CatalogId,
            checked((int)purchase.ItemQuantity),
            worldName,
            DateTimeOffset.UtcNow));

        RemoveExpiredMarketPurchases(DateTimeOffset.UtcNow);
        nextInventoryScan = DateTimeOffset.UtcNow;
    }

    private void OnInventoryChanged(
        IReadOnlyCollection<Dalamud.Game.Inventory.InventoryEventArgTypes.InventoryEventArgs> _)
    {
        if (Session?.State == PurchaseSessionState.Active)
            nextInventoryScan = DateTimeOffset.UtcNow;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = DateTimeOffset.UtcNow;
        var active = Session is not null && Session.State == PurchaseSessionState.Active;

        if (active != inventoryTrackingActive)
        {
            inventoryTrackingActive = active;
            if (active)
                ResetInventoryTracking();
            else
                StopInventoryTracking();
        }

        if (!active)
            return;

        var actualWorld = GetActualWorldName();
        if (!string.Equals(actualWorld, lastObservedWorld, StringComparison.Ordinal))
        {
            lastObservedWorld = actualWorld;
            SyncSessionStopToActualWorld();
            ResetInventoryTracking();
        }

        RemoveExpiredMarketPurchases(now);

        if (now < nextInventoryScan)
            return;

        nextInventoryScan = now.AddMilliseconds(
            Math.Clamp(configuration.InventoryScanIntervalMilliseconds, 250, 2000));

        ScanInventory(now);
        FinalizeStableInventoryDeltas(now);
    }

    private void ScanInventory(DateTimeOffset now)
    {
        try
        {
            var current = ReadInventoryCounts();
            var keys = current.Keys.Concat(lastInventory.Keys).Distinct().ToArray();

            foreach (var key in keys)
            {
                var delta = current.GetValueOrDefault(key) - lastInventory.GetValueOrDefault(key);
                if (delta == 0)
                    continue;

                if (!pendingInventoryDeltas.TryGetValue(key, out var pending))
                {
                    if (delta > 0)
                        pendingInventoryDeltas[key] = new PendingInventoryDelta(delta, now);
                    continue;
                }

                pending.Quantity = Math.Max(0, pending.Quantity + delta);
                pending.LastChangedAt = now;
                if (pending.Quantity == 0)
                    pendingInventoryDeltas.Remove(key);
            }

            lastInventory = current;
        }
        catch (Exception exception)
        {
            log.Warning(exception, "Failed to scan inventory changes.");
        }
    }

    private void FinalizeStableInventoryDeltas(DateTimeOffset now)
    {
        var debounce = TimeSpan.FromMilliseconds(
            Math.Clamp(configuration.InventoryDebounceMilliseconds, 500, 3000));
        var stable = pendingInventoryDeltas
            .Where(pair => now - pair.Value.LastChangedAt >= debounce)
            .Select(static pair => pair.Key)
            .ToArray();

        foreach (var key in stable)
        {
            if (!pendingInventoryDeltas.Remove(key, out var pending) || pending.Quantity <= 0)
                continue;

            ProcessStableInventoryIncrease(key, pending.Quantity, now);
        }
    }

    private void ProcessStableInventoryIncrease(
        InventoryKey key,
        int quantity,
        DateTimeOffset now)
    {
        if (Session is null || quantity <= 0)
            return;

        var actualWorld = GetActualWorldName();
        var remaining = quantity;
        var automaticRecord = new AutomaticRecord(
            Session.State,
            Session.CurrentServerIndex,
            []);

        if (!string.IsNullOrWhiteSpace(actualWorld))
        {
            foreach (var pending in pendingPurchases
                         .Where(item =>
                             item.ItemId == key.ItemId &&
                             string.Equals(item.WorldName, actualWorld, StringComparison.Ordinal) &&
                             item.Quantity <= remaining)
                         .OrderBy(static item => item.CreatedAt)
                         .ToArray())
            {
                var applied = ApplyProgress(
                    key.ItemId,
                    key.IsHighQuality,
                    pending.Quantity,
                    actualWorld,
                    true,
                    automaticRecord);

                if (applied <= 0)
                    continue;

                pendingPurchases.Remove(pending);
                remaining -= applied;
                if (remaining <= 0)
                    break;
            }
        }

        if (remaining > 0 &&
            configuration.AutoRecordInventoryChanges &&
            !string.IsNullOrWhiteSpace(actualWorld))
        {
            remaining -= ApplyProgress(
                key.ItemId,
                key.IsHighQuality,
                remaining,
                actualWorld,
                true,
                automaticRecord);
        }

        var appliedTotal = quantity - remaining;
        if (appliedTotal > 0)
        {
            UpdateSessionState();
            AutoAdvanceIfCurrentStopCompleted();
            lastAutomaticRecord = automaticRecord.Changes.Count > 0
                ? automaticRecord
                : null;

            var itemName = Session.Listings
                .FirstOrDefault(item => item.ItemId == key.ItemId)?.ItemName
                ?? key.ItemId.ToString();
            var totalProgress = Session.Listings
                .Where(item => item.ItemId == key.ItemId &&
                               QualityMatches(item, key.IsHighQuality))
                .Sum(static item => item.AcquiredQuantity);
            var totalPlanned = Session.Listings
                .Where(item => item.ItemId == key.ItemId &&
                               QualityMatches(item, key.IsHighQuality))
                .Sum(static item => item.Quantity);

            LastAutomaticMessage =
                $"已自动记录 {actualWorld} {itemName} +{appliedTotal}  已购 {totalProgress}/{totalPlanned}";
            Touch();
        }

        if (remaining > 0 && configuration.EnableInventorySuggestions)
            AddOrMergeSuggestion(key, remaining, now);
    }

    private int ApplyProgress(
        uint itemId,
        bool isHighQuality,
        int quantity,
        string? worldName,
        bool automatic,
        AutomaticRecord? automaticRecord)
    {
        if (Session is null || quantity <= 0)
            return 0;

        var candidates = Session.Listings
            .Where(item =>
                item.RemainingQuantity > 0 &&
                item.ItemId == itemId &&
                QualityMatches(item, isHighQuality) &&
                (worldName is null ||
                 string.Equals(item.WorldName, worldName, StringComparison.Ordinal)))
            .OrderBy(item => item.RequestedQuality == PurchaseQuality.Any ? 1 : 0)
            .ThenBy(item => CurrentStop is not null &&
                            string.Equals(item.DataCenterName, CurrentStop.DataCenterName, StringComparison.Ordinal) &&
                            string.Equals(item.WorldName, CurrentStop.WorldName, StringComparison.Ordinal)
                ? 0
                : 1)
            .ThenBy(static item => item.PricePerUnit)
            .ToArray();

        var remaining = quantity;
        foreach (var listing in candidates)
        {
            if (remaining <= 0)
                break;

            if (automaticRecord is not null &&
                automaticRecord.Changes.All(change =>
                    change.SessionListingId != listing.SessionListingId))
            {
                automaticRecord.Changes.Add(new AutomaticProgressChange(
                    listing.SessionListingId,
                    listing.AcquiredQuantity,
                    listing.IsPurchased,
                    listing.AutoConfirmed,
                    listing.PurchasedAt));
            }

            var applied = Math.Min(remaining, listing.RemainingQuantity);
            listing.AcquiredQuantity += applied;
            listing.IsPurchased = listing.AcquiredQuantity >= listing.Quantity;
            listing.AutoConfirmed = automatic || listing.AutoConfirmed;
            listing.PurchasedAt ??= DateTimeOffset.UtcNow;
            remaining -= applied;
        }

        return quantity - remaining;
    }

    private void AddOrMergeSuggestion(
        InventoryKey key,
        int quantity,
        DateTimeOffset detectedAt)
    {
        if (Session is null ||
            Session.Listings.All(item =>
                item.RemainingQuantity == 0 ||
                item.ItemId != key.ItemId ||
                !QualityMatches(item, key.IsHighQuality)))
            return;

        var existingIndex = suggestions.FindIndex(item =>
            item.ItemId == key.ItemId &&
            item.IsHighQuality == key.IsHighQuality);

        if (existingIndex >= 0)
        {
            var existing = suggestions[existingIndex];
            suggestions[existingIndex] = existing with
            {
                Quantity = existing.Quantity + quantity,
                DetectedAt = detectedAt,
            };
            return;
        }

        var itemName = Session.Listings
            .First(item => item.ItemId == key.ItemId).ItemName;
        suggestions.Add(new InventorySuggestion(
            Guid.NewGuid(),
            key.ItemId,
            itemName,
            key.IsHighQuality,
            quantity,
            detectedAt));
    }

    private void AutoAdvanceIfCurrentStopCompleted()
    {
        if (Session is null ||
            Session.IsComplete ||
            !configuration.AutoAdvanceCompletedWorld)
            return;

        var currentStop = CurrentStop;
        if (currentStop is null ||
            !IsStopComplete(currentStop.DataCenterName, currentStop.WorldName))
            return;

        AdvanceToNextIncompleteStop();
    }

    private bool IsStopComplete(string dataCenterName, string worldName)
    {
        return Session is not null && Session.Listings
            .Where(item =>
                string.Equals(item.DataCenterName, dataCenterName, StringComparison.Ordinal) &&
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
            if (IsStopComplete(stop.DataCenterName, stop.WorldName))
                continue;

            Session.CurrentServerIndex = index;
            return;
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
        if (index >= 0)
            Session.CurrentServerIndex = index;
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

    private void ResetInventoryTracking()
    {
        try
        {
            lastInventory = ReadInventoryCounts();
        }
        catch (Exception exception)
        {
            log.Warning(exception, "Failed to initialize inventory tracking.");
            lastInventory = [];
        }

        pendingInventoryDeltas.Clear();
        nextInventoryScan = DateTimeOffset.UtcNow.AddMilliseconds(
            Math.Clamp(configuration.InventoryScanIntervalMilliseconds, 250, 2000));
    }

    private void StopInventoryTracking()
    {
        pendingInventoryDeltas.Clear();
        pendingPurchases.Clear();
        nextInventoryScan = DateTimeOffset.MinValue;
    }

    private void RemoveExpiredMarketPurchases(DateTimeOffset now)
    {
        pendingPurchases.RemoveAll(item =>
            now - item.CreatedAt > TimeSpan.FromSeconds(12));
    }

    private void UpdateSessionState()
    {
        if (Session is null)
            return;

        if (Session.IsComplete)
        {
            Session.State = PurchaseSessionState.Completed;
            StopInventoryTracking();
            inventoryTrackingActive = false;
        }
        else if (Session.State == PurchaseSessionState.Completed)
        {
            Session.State = PurchaseSessionState.Active;
            inventoryTrackingActive = true;
            ResetInventoryTracking();
        }
    }

    private void SetPurchasedState(SessionListing listing, bool purchased)
    {
        listing.IsPurchased = purchased;
        listing.AcquiredQuantity = purchased ? listing.Quantity : 0;
        if (purchased)
        {
            listing.PurchasedAt ??= DateTimeOffset.UtcNow;
            return;
        }

        listing.AutoConfirmed = false;
        listing.PurchasedAt = null;
        listing.ActualPricePerUnit = null;
    }

    private static bool QualityMatches(
        SessionListing listing,
        bool isHighQuality)
    {
        return listing.RequestedQuality switch
        {
            PurchaseQuality.HighQuality => isHighQuality,
            PurchaseQuality.NormalQuality => !isHighQuality,
            _ => true,
        };
    }

    private void Touch()
    {
        if (Session is not null)
            Session.UpdatedAt = DateTimeOffset.UtcNow;

        configuration.Save();
    }

    private readonly record struct InventoryKey(
        uint ItemId,
        bool IsHighQuality);

    private sealed class PendingInventoryDelta
    {
        public PendingInventoryDelta(
            int quantity,
            DateTimeOffset lastChangedAt)
        {
            Quantity = quantity;
            LastChangedAt = lastChangedAt;
        }

        public int Quantity { get; set; }

        public DateTimeOffset LastChangedAt { get; set; }
    }

    private sealed record PendingMarketPurchase(
        uint ItemId,
        int Quantity,
        string WorldName,
        DateTimeOffset CreatedAt);

    private sealed record AutomaticProgressChange(
        Guid SessionListingId,
        int PreviousAcquiredQuantity,
        bool PreviousIsPurchased,
        bool PreviousAutoConfirmed,
        DateTimeOffset? PreviousPurchasedAt);

    private sealed record AutomaticRecord(
        PurchaseSessionState PreviousState,
        int PreviousServerIndex,
        List<AutomaticProgressChange> Changes);
}
