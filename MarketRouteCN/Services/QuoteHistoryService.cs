using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed class QuoteHistoryService
{
    private readonly Configuration configuration;

    public QuoteHistoryService(Configuration configuration)
    {
        this.configuration = configuration;
    }

    public IReadOnlyList<SavedQuoteSnapshot> History => configuration.QuoteHistory;

    public void Store(PriceComparisonSnapshot snapshot)
    {
        var saved = new SavedQuoteSnapshot
        {
            SnapshotId = snapshot.SnapshotId,
            ShoppingListId = snapshot.ShoppingListId,
            ShoppingListName = snapshot.ShoppingListName,
            RequestedAt = snapshot.RequestedAt,
            CompletedAt = snapshot.CompletedAt,
            DataCenters = snapshot.Plans.Values
                .OrderBy(static plan => plan.DataCenterName, StringComparer.Ordinal)
                .Select(static plan => new SavedDataCenterQuote
                {
                    DataCenterName = plan.DataCenterName,
                    IsComplete = plan.IsComplete,
                    TotalCost = plan.TotalCost,
                    ServerCount = plan.ServerCount,
                    CompletedItems = plan.CompletedItems,
                    TotalItems = plan.TotalItems,
                    OldestMarketDataTime = plan.OldestMarketDataTime,
                    NewestMarketDataTime = plan.NewestMarketDataTime,
                })
                .ToList(),
        };

        configuration.QuoteHistory.RemoveAll(item => item.SnapshotId == saved.SnapshotId);
        configuration.QuoteHistory.Insert(0, saved);

        var limit = Math.Clamp(configuration.SnapshotHistoryLimit, 1, 50);
        if (configuration.QuoteHistory.Count > limit)
            configuration.QuoteHistory.RemoveRange(limit, configuration.QuoteHistory.Count - limit);

        configuration.Save();
    }

    public void Clear()
    {
        configuration.QuoteHistory.Clear();
        configuration.Save();
    }
}
