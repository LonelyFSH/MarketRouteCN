namespace MarketRouteCN.Models;

public sealed class DataCenterPurchasePlan
{
    public required string DataCenterName { get; init; }

    public required IReadOnlyList<ItemPurchasePlan> ItemPlans { get; init; }

    public required IReadOnlyList<ServerPurchasePlan> ServerPlans { get; init; }

    public required DateTimeOffset QueryTime { get; init; }

    public bool IsComplete => ItemPlans.Count > 0 && ItemPlans.All(static item => item.IsComplete);

    public int CompletedItems => ItemPlans.Count(static item => item.IsComplete);

    public int TotalItems => ItemPlans.Count;

    public long TotalCost => ItemPlans.Sum(static item => item.TotalCost);

    public int RequiredUnits => ItemPlans.Sum(static item => checked((int)item.Request.Quantity));

    public int PurchasedUnits => ItemPlans.Sum(static item => item.PurchasedQuantity);

    public int ServerCount => ServerPlans.Count;

    public DateTimeOffset? NewestMarketDataTime => ItemPlans
        .Select(static item => item.MarketDataTime)
        .Where(static time => time.HasValue)
        .Max();

    public DateTimeOffset? OldestMarketDataTime => ItemPlans
        .Select(static item => item.MarketDataTime)
        .Where(static time => time.HasValue)
        .Min();
}
