using Dalamud.Plugin.Services;
using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed class PriceRefreshService : IDisposable
{
    private readonly Configuration configuration;
    private readonly IFramework framework;
    private readonly UniversalisClient universalisClient;
    private readonly PurchaseOptimizer optimizer;
    private readonly QuoteHistoryService quoteHistoryService;
    private readonly IPluginLog log;
    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly object stateLock = new();

    private Task? activeRefresh;
    private CancellationTokenSource? activeRequestCancellation;
    private DateTimeOffset? nextRefreshTime;

    public PriceRefreshService(
        Configuration configuration,
        IFramework framework,
        UniversalisClient universalisClient,
        PurchaseOptimizer optimizer,
        QuoteHistoryService quoteHistoryService,
        IPluginLog log)
    {
        this.configuration = configuration;
        this.framework = framework;
        this.universalisClient = universalisClient;
        this.optimizer = optimizer;
        this.quoteHistoryService = quoteHistoryService;
        this.log = log;

        framework.Update += OnFrameworkUpdate;
        ResetAutomaticRefreshSchedule();
    }

    public PriceComparisonSnapshot? Snapshot { get; private set; }

    public bool IsRefreshing
    {
        get
        {
            lock (stateLock)
                return activeRefresh is { IsCompleted: false };
        }
    }

    public string? LastError { get; private set; }

    public DateTimeOffset? NextRefreshTime => nextRefreshTime;

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        activeRequestCancellation?.Cancel();
        activeRequestCancellation?.Dispose();
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    public void ResetAutomaticRefreshSchedule()
    {
        nextRefreshTime = configuration.AutoRefreshMinutes > 0
            ? DateTimeOffset.UtcNow.AddMinutes(configuration.AutoRefreshMinutes)
            : null;
    }

    public void RequestRefresh(bool forceRefresh = true)
    {
        var list = configuration.ActiveShoppingList;
        RequestRefreshCore(list.ListId, list.Name, list.Entries, null, forceRefresh);
    }

    public void RequestRemainingSessionRefresh(PurchaseSession session)
    {
        var remaining = session.Requirements
            .Select(requirement =>
            {
                var purchasedQuantity = session.Listings
                    .Where(listing => listing.IsPurchased && listing.EntryId == requirement.EntryId)
                    .Sum(static listing => listing.Quantity);
                var remainingQuantity = Math.Max(0, requirement.RequiredQuantity - purchasedQuantity);
                return remainingQuantity == 0
                    ? null
                    : new ShoppingListEntry
                    {
                        EntryId = requirement.EntryId,
                        ItemId = requirement.ItemId,
                        DisplayName = requirement.ItemName,
                        Quantity = checked((uint)remainingQuantity),
                        Quality = requirement.Quality,
                        SupportsHighQuality = requirement.SupportsHighQuality,
                    };
            })
            .Where(static entry => entry is not null)
            .Select(static entry => entry!)
            .ToArray();

        if (remaining.Length == 0)
            return;

        var dataCenters = DataCenterCatalog.IsCrossDataCenterPlan(session.DataCenterName)
            ? DataCenterCatalog.ChinaDataCenters
            : [session.DataCenterName];
        RequestRefreshCore(
            session.ShoppingListId,
            session.ShoppingListName + " 剩余",
            remaining,
            session.SessionId,
            true,
            dataCenters,
            DataCenterCatalog.IsCrossDataCenterPlan(session.DataCenterName));
    }

    public void CancelRefresh()
    {
        activeRequestCancellation?.Cancel();
    }

    private void RequestRefreshCore(
        Guid shoppingListId,
        string shoppingListName,
        IReadOnlyCollection<ShoppingListEntry> entries,
        Guid? sourceSessionId,
        bool forceRefresh,
        IReadOnlyCollection<string>? dataCenterOverride = null,
        bool includeCrossDataCenterPlan = false)
    {
        lock (stateLock)
        {
            if (activeRefresh is { IsCompleted: false })
                return;

            activeRequestCancellation?.Dispose();
            activeRequestCancellation = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token);
            var copiedEntries = entries.Select(static entry => entry.Clone()).ToArray();
            activeRefresh = RefreshAsync(
                shoppingListId,
                shoppingListName,
                copiedEntries,
                sourceSessionId,
                forceRefresh,
                dataCenterOverride,
                includeCrossDataCenterPlan,
                activeRequestCancellation.Token);
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (configuration.AutoRefreshMinutes <= 0 ||
            configuration.ActiveShoppingList.Entries.Count == 0 ||
            nextRefreshTime is null ||
            DateTimeOffset.UtcNow < nextRefreshTime.Value)
            return;

        RequestRefresh(false);
        ResetAutomaticRefreshSchedule();
    }

    private async Task RefreshAsync(
        Guid shoppingListId,
        string shoppingListName,
        IReadOnlyList<ShoppingListEntry> shoppingList,
        Guid? sourceSessionId,
        bool forceRefresh,
        IReadOnlyCollection<string>? dataCenterOverride,
        bool includeCrossDataCenterPlan,
        CancellationToken cancellationToken)
    {
        try
        {
            LastError = null;
            if (shoppingList.Count == 0)
            {
                LastError = "采购清单为空。";
                return;
            }

            var crossDataCenterMode = includeCrossDataCenterPlan ||
                                      (dataCenterOverride is null &&
                                       configuration.EnableAdvancedOptions &&
                                       configuration.EnableCrossDataCenterAnalysis &&
                                       configuration.Scope == PurchaseScope.CrossDataCenterMixed);
            var dataCenters = dataCenterOverride?.ToArray() ?? (configuration.Scope == PurchaseScope.SingleDataCenter
                ? [configuration.SelectedDataCenter]
                : DataCenterCatalog.ChinaDataCenters);

            var requestedAt = DateTimeOffset.UtcNow;
            var itemIds = shoppingList.Select(static entry => entry.ItemId).ToArray();
            var tasks = dataCenters.Select(async dataCenter =>
            {
                var data = await universalisClient.GetListingsAsync(
                    dataCenter,
                    itemIds,
                    configuration.CacheMinutes,
                    forceRefresh,
                    cancellationToken).ConfigureAwait(false);
                return (DataCenter: dataCenter, Data: data);
            });

            var fetched = await Task.WhenAll(tasks).ConfigureAwait(false);
            var marketDataByDataCenter = fetched.ToDictionary(
                static item => item.DataCenter,
                static item => item.Data,
                StringComparer.Ordinal);
            var plans = fetched.Select(item => optimizer.BuildPlan(
                    item.DataCenter,
                    shoppingList,
                    item.Data,
                    requestedAt,
                    configuration.Strategy,
                    configuration.AdditionalServerSavingsThreshold,
                    configuration.OverbuyPenaltyPerUnit,
                    configuration.StaleDataPenaltyPerHour,
                    cancellationToken))
                .ToList();

            if (crossDataCenterMode)
            {
                plans.Add(optimizer.BuildCrossDataCenterPlan(
                    shoppingList,
                    marketDataByDataCenter,
                    requestedAt,
                    configuration.Strategy,
                    configuration.AdditionalServerSavingsThreshold,
                    configuration.AdditionalDataCenterSavingsThreshold,
                    configuration.OverbuyPenaltyPerUnit,
                    configuration.StaleDataPenaltyPerHour,
                    cancellationToken));
            }

            var snapshot = new PriceComparisonSnapshot
            {
                ShoppingListId = shoppingListId,
                ShoppingListName = shoppingListName,
                SourceSessionId = sourceSessionId,
                RequestedAt = requestedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                Plans = plans.ToDictionary(static plan => plan.DataCenterName, StringComparer.Ordinal),
            };

            Snapshot = snapshot;
            quoteHistoryService.Store(snapshot);
            ResetAutomaticRefreshSchedule();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LastError = disposeCancellation.IsCancellationRequested ? null : "价格查询已取消。";
        }
        catch (Exception exception)
        {
            LastError = $"价格查询失败：{exception.Message}";
            log.Error(exception, "Failed to refresh market data.");
            ResetAutomaticRefreshSchedule();
        }
    }
}
