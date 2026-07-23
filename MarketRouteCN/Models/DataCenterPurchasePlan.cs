namespace MarketRouteCN.Models;

public sealed class DataCenterPurchasePlan
{
    public required string DataCenterName { get; init; }

    public required PurchaseStrategy Strategy { get; init; }

    public required IReadOnlyList<ItemPurchasePlan> ItemPlans { get; init; }

    public required IReadOnlyList<ServerPurchasePlan> ServerPlans { get; init; }

    public required IReadOnlyList<RouteAlternative> Alternatives { get; init; }

    public required DateTimeOffset QueryTime { get; init; }

    public required long OptimizationScore { get; init; }

    public bool IsComplete => ItemPlans.Count > 0 && ItemPlans.All(static item => item.IsComplete);

    public int CompletedItems => ItemPlans.Count(static item => item.IsComplete);

    public int TotalItems => ItemPlans.Count;

    public long TotalCost => ItemPlans.Sum(static item => item.TotalCost);

    public long RiskCostIncrease => ItemPlans.Sum(static item => item.RiskCostIncrease);

    public long RiskAdjustedCost => TotalCost + RiskCostIncrease;

    public int RequiredUnits => ItemPlans.Sum(static item => checked((int)item.Request.Quantity));

    public int PurchasedUnits => ItemPlans.Sum(static item => item.PurchasedQuantity);

    public int OverbuyUnits => ItemPlans.Sum(static item => item.OverbuyQuantity);

    public int ServerCount => ServerPlans.Count;

    public int LowLiquidityItems => ItemPlans.Count(static item => item.IsComplete && item.EligibleListingCount <= 1);

    public DateTimeOffset? NewestMarketDataTime => ItemPlans
        .Select(static item => item.MarketDataTime)
        .Where(static time => time.HasValue)
        .Max();

    public DateTimeOffset? OldestMarketDataTime => ItemPlans
        .Select(static item => item.MarketDataTime)
        .Where(static time => time.HasValue)
        .Min();

    public int StaleItemCount(TimeSpan threshold)
    {
        var now = DateTimeOffset.UtcNow;
        return ItemPlans.Count(item => item.MarketDataTime is null || now - item.MarketDataTime.Value > threshold);
    }
}
