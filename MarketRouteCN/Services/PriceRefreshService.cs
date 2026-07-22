using Dalamud.Plugin.Services;
using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed class PriceRefreshService : IDisposable
{
    private readonly Configuration configuration;
    private readonly IFramework framework;
    private readonly UniversalisClient universalisClient;
    private readonly PurchaseOptimizer optimizer;
    private readonly IPluginLog log;
    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly object stateLock = new();

    private Task? activeRefresh;
    private DateTimeOffset? nextRefreshTime;

    public PriceRefreshService(
        Configuration configuration,
        IFramework framework,
        UniversalisClient universalisClient,
        PurchaseOptimizer optimizer,
        IPluginLog log)
    {
        this.configuration = configuration;
        this.framework = framework;
        this.universalisClient = universalisClient;
        this.optimizer = optimizer;
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

    public DateTimeOffset? LastQueryTime => Snapshot?.QueryTime;

    public DateTimeOffset? NextRefreshTime => nextRefreshTime;

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    public void ResetAutomaticRefreshSchedule()
    {
        nextRefreshTime = configuration.AutoRefreshMinutes > 0
            ? DateTimeOffset.UtcNow.AddMinutes(configuration.AutoRefreshMinutes)
            : null;
    }

    // 启动价格更新
    public void RequestRefresh()
    {
        lock (stateLock)
        {
            if (activeRefresh is { IsCompleted: false })
                return;

            activeRefresh = RefreshAsync(disposeCancellation.Token);
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (configuration.AutoRefreshMinutes <= 0 ||
            configuration.ShoppingList.Count == 0 ||
            nextRefreshTime is null ||
            DateTimeOffset.UtcNow < nextRefreshTime.Value)
        {
            return;
        }

        RequestRefresh();
        ResetAutomaticRefreshSchedule();
    }

    // 生成最新采购方案
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            LastError = null;

            var shoppingList = configuration.ShoppingList
                .Select(static entry => new ShoppingListEntry
                {
                    EntryId = entry.EntryId,
                    ItemId = entry.ItemId,
                    DisplayName = entry.DisplayName,
                    Quantity = entry.Quantity,
                    Quality = entry.Quality,
                    SupportsHighQuality = entry.SupportsHighQuality,
                })
                .ToArray();

            if (shoppingList.Length == 0)
            {
                LastError = "采购清单为空。";
                return;
            }

            var dataCenters = configuration.Scope == PurchaseScope.SingleDataCenter
                ? new[] { configuration.SelectedDataCenter }
                : DataCenterCatalog.ChinaDataCenters;

            var queryTime = DateTimeOffset.UtcNow;
            var tasks = dataCenters.Select(async dataCenter =>
            {
                var data = await universalisClient.GetListingsAsync(
                    dataCenter,
                    shoppingList.Select(static entry => entry.ItemId).ToArray(),
                    cancellationToken).ConfigureAwait(false);

                return optimizer.BuildPlan(dataCenter, shoppingList, data, queryTime);
            });

            var plans = await Task.WhenAll(tasks).ConfigureAwait(false);
            Snapshot = new PriceComparisonSnapshot
            {
                QueryTime = queryTime,
                Plans = plans.ToDictionary(static plan => plan.DataCenterName, StringComparer.Ordinal),
            };

            ResetAutomaticRefreshSchedule();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 插件关闭时结束请求
        }
        catch (Exception exception)
        {
            LastError = $"价格查询失败：{exception.Message}";
            log.Error(exception, "Failed to refresh Universalis market data.");
            ResetAutomaticRefreshSchedule();
        }
    }
}
