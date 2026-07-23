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
        var staleThreshold = TimeSpan.FromHours(Math.Max(1, configuration.StaleDataWarningHours));
        var saved = new SavedQuoteSnapshot
        {
            SnapshotId = snapshot.SnapshotId,
            ShoppingListId = snapshot.ShoppingListId,
            ShoppingListName = snapshot.ShoppingListName,
            RequestedAt = snapshot.RequestedAt,
            CompletedAt = snapshot.CompletedAt,
            DataCenters = snapshot.Plans.Values
                .OrderBy(static plan => plan.DataCenterName, StringComparer.Ordinal)
                .Select(plan => new SavedDataCenterQuote
                {
                    DataCenterName = plan.DataCenterName,
                    IsComplete = plan.IsComplete,
                    TotalCost = plan.TotalCost,
                    RiskAdjustedCost = plan.RiskAdjustedCost,
                    ServerCount = plan.ServerCount,
                    CompletedItems = plan.CompletedItems,
                    TotalItems = plan.TotalItems,
                    StaleItems = plan.StaleItemCount(staleThreshold),
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

    public SavedDataCenterQuote? FindPreviousQuote(Guid shoppingListId, Guid currentSnapshotId, string dataCenterName)
    {
        return configuration.QuoteHistory
            .Where(snapshot => snapshot.ShoppingListId == shoppingListId && snapshot.SnapshotId != currentSnapshotId)
            .OrderByDescending(static snapshot => snapshot.CompletedAt)
            .SelectMany(static snapshot => snapshot.DataCenters)
            .FirstOrDefault(quote => string.Equals(quote.DataCenterName, dataCenterName, StringComparison.Ordinal));
    }

    public void Clear()
    {
        configuration.QuoteHistory.Clear();
        configuration.Save();
    }
}
