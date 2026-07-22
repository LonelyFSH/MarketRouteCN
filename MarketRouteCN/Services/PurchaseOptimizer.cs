using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed class PurchaseOptimizer
{
    private const int ExactDpMaximumQuantity = 25_000;
    private const int ExactDpMaximumListings = 500;

    // 生成大区采购方案
    public DataCenterPurchasePlan BuildPlan(
        string dataCenter,
        IReadOnlyList<ShoppingListEntry> shoppingList,
        IReadOnlyDictionary<uint, ItemMarketData> marketData,
        DateTimeOffset queryTime)
    {
        var itemPlans = shoppingList
            .Select(entry => BuildItemPlan(entry, marketData.GetValueOrDefault(entry.ItemId)))
            .ToArray();

        var serverPlans = itemPlans
            .SelectMany(static item => item.SelectedListings)
            .GroupBy(static listing => listing.Listing.WorldName, StringComparer.Ordinal)
            .Select(group => new ServerPurchasePlan
            {
                WorldName = group.Key,
                Listings = group
                    .OrderBy(static listing => listing.ItemName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static listing => listing.Listing.PricePerUnit)
                    .ToArray(),
            })
            .OrderByDescending(static server => server.Listings.Count)
            .ThenByDescending(static server => server.TotalCost)
            .ThenBy(static server => server.WorldName, StringComparer.Ordinal)
            .ToArray();

        return new DataCenterPurchasePlan
        {
            DataCenterName = dataCenter,
            ItemPlans = itemPlans,
            ServerPlans = serverPlans,
            QueryTime = queryTime,
        };
    }

    private static ItemPurchasePlan BuildItemPlan(ShoppingListEntry request, ItemMarketData? marketData)
    {
        var eligibleListings = marketData?.Listings
            .Where(listing => request.Quality switch
            {
                PurchaseQuality.HighQuality => listing.IsHighQuality,
                PurchaseQuality.NormalQuality => !listing.IsHighQuality,
                _ => true,
            })
            .Where(static listing => listing.PricePerUnit > 0 && listing.Quantity > 0)
            .OrderBy(static listing => listing.PricePerUnit)
            .ThenBy(static listing => listing.TotalPrice)
            .ToArray() ?? [];

        if (eligibleListings.Length == 0)
        {
            return new ItemPurchasePlan
            {
                Request = request,
                SelectedListings = [],
                TotalCost = 0,
                PurchasedQuantity = 0,
                IsComplete = false,
                MarketDataTime = marketData?.LastUploadTime,
            };
        }

        var selection = Solve(eligibleListings, checked((int)request.Quantity));
        var selected = selection.Listings
            .Select(listing => new SelectedListing(request.ItemId, request.DisplayName, request.Quality, listing))
            .ToArray();

        return new ItemPurchasePlan
        {
            Request = request,
            SelectedListings = selected,
            TotalCost = selection.TotalCost,
            PurchasedQuantity = selection.TotalQuantity,
            IsComplete = selection.TotalQuantity >= request.Quantity,
            MarketDataTime = marketData?.LastUploadTime,
        };
    }

    private static ListingSelection Solve(IReadOnlyList<MarketListing> listings, int requiredQuantity)
    {
        var totalAvailable = listings.Sum(static listing => (long)listing.Quantity);
        if (totalAvailable < requiredQuantity)
        {
            var all = listings.OrderBy(static listing => listing.PricePerUnit).ToArray();
            return new ListingSelection(
                all,
                all.Sum(static listing => listing.TotalPrice),
                all.Sum(static listing => listing.Quantity));
        }

        var maximumListingQuantity = listings.Max(static listing => listing.Quantity);
        var quantityCap = checked(requiredQuantity + maximumListingQuantity - 1);

        if (quantityCap <= ExactDpMaximumQuantity && listings.Count <= ExactDpMaximumListings)
            return SolveExactly(listings, requiredQuantity, quantityCap);

        return SolveGreedy(listings, requiredQuantity);
    }

    // 计算最低成本组合
    private static ListingSelection SolveExactly(
        IReadOnlyList<MarketListing> listings,
        int requiredQuantity,
        int quantityCap)
    {
        var states = new PlanNode?[quantityCap + 1];
        states[0] = new PlanNode(0, 0, 0, null, -1);

        for (var listingIndex = 0; listingIndex < listings.Count; listingIndex++)
        {
            var listing = listings[listingIndex];
            var previousStates = (PlanNode?[])states.Clone();

            for (var quantity = 0; quantity < previousStates.Length; quantity++)
            {
                var previous = previousStates[quantity];
                if (previous is null)
                    continue;

                var nextQuantity = Math.Min(quantityCap, quantity + listing.Quantity);
                var nextCost = checked(previous.Cost + listing.TotalPrice);
                var nextCount = previous.ListingCount + 1;
                var candidate = new PlanNode(nextCost, nextQuantity, nextCount, previous, listingIndex);

                if (IsBetter(candidate, states[nextQuantity]))
                    states[nextQuantity] = candidate;
            }
        }

        PlanNode? best = null;
        for (var quantity = requiredQuantity; quantity < states.Length; quantity++)
        {
            var state = states[quantity];
            if (state is not null && IsBetter(state, best))
                best = state;
        }

        if (best is null)
            return SolveGreedy(listings, requiredQuantity);

        var selectedListings = new List<MarketListing>();
        for (var node = best; node is not null && node.ListingIndex >= 0; node = node.Previous)
            selectedListings.Add(listings[node.ListingIndex]);

        selectedListings.Reverse();
        return new ListingSelection(selectedListings, best.Cost, best.Quantity);
    }

    // 处理较大的挂单集合
    private static ListingSelection SolveGreedy(IReadOnlyList<MarketListing> listings, int requiredQuantity)
    {
        var selected = new List<MarketListing>();
        var totalQuantity = 0;
        long totalCost = 0;

        foreach (var listing in listings
                     .OrderBy(static listing => listing.PricePerUnit)
                     .ThenBy(static listing => listing.TotalPrice))
        {
            selected.Add(listing);
            totalQuantity += listing.Quantity;
            totalCost += listing.TotalPrice;

            if (totalQuantity >= requiredQuantity)
                break;
        }

        return new ListingSelection(selected, totalCost, totalQuantity);
    }

    private static bool IsBetter(PlanNode candidate, PlanNode? current)
    {
        if (current is null)
            return true;

        if (candidate.Cost != current.Cost)
            return candidate.Cost < current.Cost;

        if (candidate.Quantity != current.Quantity)
            return candidate.Quantity < current.Quantity;

        return candidate.ListingCount < current.ListingCount;
    }

    private sealed record PlanNode(
        long Cost,
        int Quantity,
        int ListingCount,
        PlanNode? Previous,
        int ListingIndex);

    private sealed record ListingSelection(
        IReadOnlyList<MarketListing> Listings,
        long TotalCost,
        int TotalQuantity);
}
