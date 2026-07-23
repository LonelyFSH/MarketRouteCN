using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed class PurchaseOptimizer
{
    private const int ExactDpMaximumQuantity = 5_000;
    private const int ExactDpMaximumListings = 160;

    public DataCenterPurchasePlan BuildPlan(
        string dataCenter,
        IReadOnlyList<ShoppingListEntry> shoppingList,
        IReadOnlyDictionary<uint, ItemMarketData> marketData,
        DateTimeOffset queryTime,
        PurchaseStrategy strategy,
        long additionalServerSavingsThreshold,
        int overbuyPenaltyPerUnit,
        int staleDataPenaltyPerHour,
        CancellationToken cancellationToken)
    {
        var worlds = marketData.Values
            .SelectMany(static item => item.Listings)
            .Select(static listing => listing.WorldName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static world => world, StringComparer.Ordinal)
            .ToArray();

        if (worlds.Length == 0)
            return BuildEmptyPlan(dataCenter, shoppingList, marketData, queryTime, strategy);

        var worldIndex = worlds.Select((world, index) => (world, index))
            .ToDictionary(static pair => pair.world, static pair => pair.index, StringComparer.Ordinal);

        var candidates = new List<CandidatePlan>();
        var maskCount = 1 << worlds.Length;
        for (var mask = 1; mask < maskCount; mask++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var itemPlans = new ItemPurchasePlan[shoppingList.Count];
            var complete = true;
            for (var index = 0; index < shoppingList.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var request = shoppingList[index];
                var data = marketData.GetValueOrDefault(request.ItemId);
                itemPlans[index] = BuildItemPlan(request, data, listing =>
                    worldIndex.TryGetValue(listing.WorldName, out var worldPosition) && (mask & (1 << worldPosition)) != 0);
                complete &= itemPlans[index].IsComplete;
            }

            var actualWorlds = itemPlans
                .SelectMany(static item => item.SelectedListings)
                .Select(static listing => listing.Listing.WorldName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static world => world, StringComparer.Ordinal)
                .ToArray();

            if (actualWorlds.Length == 0)
                continue;

            var totalCost = itemPlans.Sum(static item => item.TotalCost);
            var overbuy = itemPlans.Sum(static item => item.OverbuyQuantity);
            var stalePenalty = CalculateStalePenalty(itemPlans, staleDataPenaltyPerHour);
            var score = CalculateScore(strategy, totalCost, actualWorlds.Length, overbuy,
                additionalServerSavingsThreshold, overbuyPenaltyPerUnit, stalePenalty);

            candidates.Add(new CandidatePlan(itemPlans, actualWorlds, complete, totalCost, overbuy, score));
        }

        if (candidates.Count == 0)
            return BuildEmptyPlan(dataCenter, shoppingList, marketData, queryTime, strategy);

        var completeCandidates = candidates.Where(static candidate => candidate.IsComplete).ToArray();
        var selected = completeCandidates.Length > 0
            ? completeCandidates.OrderBy(candidate => GetSortKey(candidate, strategy)).First()
            : candidates
                .OrderByDescending(static candidate => candidate.ItemPlans.Count(static item => item.IsComplete))
                .ThenByDescending(static candidate => candidate.ItemPlans.Sum(static item =>
                    Math.Min(item.PurchasedQuantity, checked((int)item.Request.Quantity))))
                .ThenBy(candidate => GetSortKey(candidate, strategy))
                .First();
        var alternatives = completeCandidates
            .GroupBy(static candidate => candidate.Worlds.Count)
            .Select(group => group.OrderBy(static candidate => candidate.TotalCost)
                .ThenBy(static candidate => candidate.OverbuyQuantity)
                .First())
            .OrderBy(static candidate => candidate.Worlds.Count)
            .Select(static candidate => new RouteAlternative(
                candidate.Worlds.Count,
                candidate.TotalCost,
                candidate.OverbuyQuantity,
                candidate.Worlds))
            .ToArray();

        return BuildDataCenterPlan(dataCenter, selected.ItemPlans, alternatives, queryTime, strategy, selected.Score);
    }

    private static DataCenterPurchasePlan BuildEmptyPlan(
        string dataCenter,
        IReadOnlyList<ShoppingListEntry> shoppingList,
        IReadOnlyDictionary<uint, ItemMarketData> marketData,
        DateTimeOffset queryTime,
        PurchaseStrategy strategy)
    {
        var itemPlans = shoppingList
            .Select(entry => BuildItemPlan(entry, marketData.GetValueOrDefault(entry.ItemId), static _ => false))
            .ToArray();
        return BuildDataCenterPlan(dataCenter, itemPlans, [], queryTime, strategy, long.MaxValue);
    }

    private static DataCenterPurchasePlan BuildDataCenterPlan(
        string dataCenter,
        IReadOnlyList<ItemPurchasePlan> itemPlans,
        IReadOnlyList<RouteAlternative> alternatives,
        DateTimeOffset queryTime,
        PurchaseStrategy strategy,
        long score)
    {
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
            Strategy = strategy,
            ItemPlans = itemPlans,
            ServerPlans = serverPlans,
            Alternatives = alternatives,
            QueryTime = queryTime,
            OptimizationScore = score,
        };
    }

    private static ItemPurchasePlan BuildItemPlan(
        ShoppingListEntry request,
        ItemMarketData? marketData,
        Func<MarketListing, bool> worldFilter)
    {
        var eligibleListings = marketData?.Listings
            .Where(worldFilter)
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
                DataStatus = marketData?.Status ?? MarketDataStatus.RequestFailed,
                MarketDataTime = marketData?.LastUploadTime,
                EligibleListingCount = 0,
                LowestUnitPrice = 0,
                HasFallbackPlan = false,
                FallbackCost = 0,
            };
        }

        var requiredQuantity = checked((int)request.Quantity);
        var selection = Solve(eligibleListings, requiredQuantity);
        var selected = selection.Listings
            .Select(listing => new SelectedListing(
                Guid.NewGuid(),
                request.EntryId,
                request.ItemId,
                request.DisplayName,
                request.Quality,
                listing))
            .ToArray();

        var selectedMarketTime = selection.Listings
            .Select(static listing => listing.LastReviewTime)
            .Where(static time => time.HasValue)
            .Min() ?? marketData?.LastUploadTime;

        var fallbackCost = selection.TotalCost;
        var hasFallbackPlan = false;
        if (selection.TotalQuantity >= requiredQuantity && selection.Listings.Count > 0)
        {
            var firstSelected = selection.Listings[0];
            var fallbackListings = eligibleListings
                .Where(listing => !ReferenceEquals(listing, firstSelected))
                .ToArray();
            if (fallbackListings.Length > 0)
            {
                var fallback = Solve(fallbackListings, requiredQuantity);
                if (fallback.TotalQuantity >= requiredQuantity)
                {
                    hasFallbackPlan = true;
                    fallbackCost = fallback.TotalCost;
                }
            }
        }

        return new ItemPurchasePlan
        {
            Request = request,
            SelectedListings = selected,
            TotalCost = selection.TotalCost,
            PurchasedQuantity = selection.TotalQuantity,
            IsComplete = selection.TotalQuantity >= requiredQuantity,
            DataStatus = marketData?.Status ?? MarketDataStatus.RequestFailed,
            MarketDataTime = selectedMarketTime,
            EligibleListingCount = eligibleListings.Length,
            LowestUnitPrice = eligibleListings[0].PricePerUnit,
            HasFallbackPlan = hasFallbackPlan,
            FallbackCost = fallbackCost,
        };
    }

    private static ListingSelection Solve(IReadOnlyList<MarketListing> listings, int requiredQuantity)
    {
        var totalAvailable = listings.Sum(static listing => (long)listing.Quantity);
        if (totalAvailable < requiredQuantity)
        {
            var all = listings.OrderBy(static listing => listing.PricePerUnit).ThenBy(static listing => listing.TotalPrice).ToArray();
            return new ListingSelection(all, all.Sum(static listing => listing.TotalPrice), all.Sum(static listing => listing.Quantity));
        }

        var maximumListingQuantity = listings.Max(static listing => listing.Quantity);
        var quantityCap = checked(requiredQuantity + maximumListingQuantity - 1);
        if (quantityCap <= ExactDpMaximumQuantity && listings.Count <= ExactDpMaximumListings)
            return SolveExactly(listings, requiredQuantity, quantityCap);

        return SolveGreedy(listings, requiredQuantity);
    }

    private static ListingSelection SolveExactly(IReadOnlyList<MarketListing> listings, int requiredQuantity, int quantityCap)
    {
        var states = new PlanNode?[quantityCap + 1];
        states[0] = new PlanNode(0, 0, 0, null, -1);

        for (var listingIndex = 0; listingIndex < listings.Count; listingIndex++)
        {
            var listing = listings[listingIndex];
            for (var quantity = quantityCap - 1; quantity >= 0; quantity--)
            {
                var previous = states[quantity];
                if (previous is null)
                    continue;

                var nextQuantity = Math.Min(quantityCap, quantity + listing.Quantity);
                var candidate = new PlanNode(
                    checked(previous.Cost + listing.TotalPrice),
                    nextQuantity,
                    previous.ListingCount + 1,
                    previous,
                    listingIndex);

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

        var selected = new List<MarketListing>();
        for (var node = best; node is not null && node.ListingIndex >= 0; node = node.Previous)
            selected.Add(listings[node.ListingIndex]);
        selected.Reverse();
        return new ListingSelection(selected, best.Cost, best.Quantity);
    }

    private static ListingSelection SolveGreedy(IReadOnlyList<MarketListing> listings, int requiredQuantity)
    {
        var selected = new List<MarketListing>();
        var quantity = 0;
        long cost = 0;
        foreach (var listing in listings.OrderBy(static listing => listing.PricePerUnit).ThenBy(static listing => listing.TotalPrice))
        {
            selected.Add(listing);
            quantity += listing.Quantity;
            cost += listing.TotalPrice;
            if (quantity >= requiredQuantity)
                break;
        }
        return new ListingSelection(selected, cost, quantity);
    }

    private static long CalculateScore(
        PurchaseStrategy strategy,
        long cost,
        int serverCount,
        int overbuy,
        long serverPenalty,
        int overbuyPenalty,
        long stalePenalty)
    {
        return strategy switch
        {
            PurchaseStrategy.LowestPrice => cost,
            PurchaseStrategy.FewestServers => checked((long)serverCount * 1_000_000_000_000L + cost),
            _ => checked(cost + Math.Max(0, serverCount - 1) * serverPenalty + (long)overbuy * overbuyPenalty + stalePenalty),
        };
    }

    private static long CalculateStalePenalty(IEnumerable<ItemPurchasePlan> itemPlans, int penaltyPerHour)
    {
        if (penaltyPerHour <= 0)
            return 0;

        var now = DateTimeOffset.UtcNow;
        long total = 0;
        foreach (var item in itemPlans)
        {
            if (item.MarketDataTime is null)
                total += penaltyPerHour * 24L;
            else
                total += checked((long)Math.Max(0, Math.Floor((now - item.MarketDataTime.Value).TotalHours)) * penaltyPerHour);
        }
        return total;
    }

    private static (long Primary, long Secondary, long Tertiary) GetSortKey(CandidatePlan candidate, PurchaseStrategy strategy)
    {
        return strategy switch
        {
            PurchaseStrategy.LowestPrice => (candidate.TotalCost, candidate.Worlds.Count, candidate.OverbuyQuantity),
            PurchaseStrategy.FewestServers => (candidate.Worlds.Count, candidate.TotalCost, candidate.OverbuyQuantity),
            _ => (candidate.Score, candidate.TotalCost, candidate.Worlds.Count),
        };
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

    private sealed record CandidatePlan(
        IReadOnlyList<ItemPurchasePlan> ItemPlans,
        IReadOnlyList<string> Worlds,
        bool IsComplete,
        long TotalCost,
        int OverbuyQuantity,
        long Score);

    private sealed record PlanNode(long Cost, int Quantity, int ListingCount, PlanNode? Previous, int ListingIndex);

    private sealed record ListingSelection(IReadOnlyList<MarketListing> Listings, long TotalCost, int TotalQuantity);
}
