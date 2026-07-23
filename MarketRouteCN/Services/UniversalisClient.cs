using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Dalamud.Plugin.Services;
using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed class UniversalisClient : IDisposable
{
    private const string BaseAddress = "https://universalis.app/api/v2/";
    private const int MaximumAttempts = 3;

    private readonly HttpClient httpClient;
    private readonly IPluginLog log;
    private readonly SemaphoreSlim requestGate = new(4, 4);
    private readonly object cacheLock = new();
    private readonly Dictionary<(string DataCenter, uint ItemId), CacheEntry> cache = [];

    public UniversalisClient(IPluginLog log)
    {
        this.log = log;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseAddress),
            Timeout = TimeSpan.FromSeconds(45),
        };

        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MarketRouteCN", "0.8.0"));
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/LonelyFSH/MarketRouteCN)"));
    }

    public void Dispose()
    {
        requestGate.Dispose();
        httpClient.Dispose();
    }

    public async Task<IReadOnlyDictionary<uint, ItemMarketData>> GetListingsAsync(
        string dataCenter,
        IReadOnlyCollection<uint> itemIds,
        int cacheMinutes,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var requested = itemIds.Distinct().ToArray();
        var result = new Dictionary<uint, ItemMarketData>();
        var missing = new List<uint>();
        var now = DateTimeOffset.UtcNow;
        var cacheLifetime = TimeSpan.FromMinutes(Math.Clamp(cacheMinutes, 0, 60));

        lock (cacheLock)
        {
            foreach (var itemId in requested)
            {
                if (!forceRefresh && cache.TryGetValue((dataCenter, itemId), out var entry) && now - entry.CachedAt <= cacheLifetime)
                    result[itemId] = entry.Data;
                else
                    missing.Add(itemId);
            }
        }

        var tasks = missing.Chunk(100)
            .Select(batch => FetchBatchAsync(dataCenter, batch, cancellationToken))
            .ToArray();

        var batches = await Task.WhenAll(tasks).ConfigureAwait(false);
        foreach (var batch in batches)
        {
            foreach (var pair in batch)
            {
                result[pair.Key] = pair.Value;
                if (pair.Value.Status != MarketDataStatus.RequestFailed)
                {
                    lock (cacheLock)
                        cache[(dataCenter, pair.Key)] = new CacheEntry(DateTimeOffset.UtcNow, pair.Value);
                }
            }
        }

        foreach (var itemId in requested)
        {
            if (!result.ContainsKey(itemId))
                result[itemId] = new ItemMarketData(itemId, null, [], MarketDataStatus.UnresolvedItem);
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<uint, ItemMarketData>> FetchBatchAsync(
        string dataCenter,
        uint[] itemIds,
        CancellationToken cancellationToken)
    {
        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Exception? lastException = null;
            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                try
                {
                    return await SendBatchAsync(dataCenter, itemIds, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                    log.Warning(exception, "Universalis request attempt {Attempt} failed for {DataCenter}.", attempt, dataCenter);
                    if (attempt < MaximumAttempts)
                        await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt * attempt), cancellationToken).ConfigureAwait(false);
                }
            }

            log.Error(lastException, "Universalis requests failed for {DataCenter}.", dataCenter);
            return itemIds.ToDictionary(
                static itemId => itemId,
                itemId => new ItemMarketData(
                    itemId,
                    null,
                    [],
                    MarketDataStatus.RequestFailed,
                    lastException?.Message));
        }
        finally
        {
            requestGate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<uint, ItemMarketData>> SendBatchAsync(
        string dataCenter,
        uint[] itemIds,
        CancellationToken cancellationToken)
    {
        var ids = string.Join(',', itemIds.Select(static id => id.ToString(CultureInfo.InvariantCulture)));
        var relativeUrl = $"{Uri.EscapeDataString(dataCenter)}/{ids}";

        using var response = await httpClient.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var result = new Dictionary<uint, ItemMarketData>();
        ParseResponse(document.RootElement, result, itemIds);
        return result;
    }

    private static void ParseResponse(JsonElement root, IDictionary<uint, ItemMarketData> destination, IReadOnlyCollection<uint> requested)
    {
        var unresolved = new HashSet<uint>();
        if (root.TryGetProperty("unresolvedItems", out var unresolvedElement) && unresolvedElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in unresolvedElement.EnumerateArray())
            {
                if (item.TryGetUInt32(out var id))
                    unresolved.Add(id);
            }
        }

        if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in itemsElement.EnumerateObject())
                ParseItem(property.Value, destination);
        }
        else
        {
            ParseItem(root, destination);
        }

        foreach (var itemId in requested)
        {
            if (unresolved.Contains(itemId))
                destination[itemId] = new ItemMarketData(itemId, null, [], MarketDataStatus.UnresolvedItem);
            else if (!destination.ContainsKey(itemId))
                destination[itemId] = new ItemMarketData(itemId, null, [], MarketDataStatus.NoListings);
        }
    }

    private static void ParseItem(JsonElement itemElement, IDictionary<uint, ItemMarketData> destination)
    {
        if (!TryGetUInt32(itemElement, "itemID", out var itemId))
            return;

        var lastUploadTime = TryGetInt64(itemElement, "lastUploadTime", out var uploadMilliseconds) && uploadMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(uploadMilliseconds)
            : (DateTimeOffset?)null;

        var listings = new List<MarketListing>();
        if (itemElement.TryGetProperty("listings", out var listingsElement) && listingsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var listingElement in listingsElement.EnumerateArray())
            {
                if (!TryGetInt32(listingElement, "pricePerUnit", out var pricePerUnit) || pricePerUnit <= 0 ||
                    !TryGetInt32(listingElement, "quantity", out var quantity) || quantity <= 0)
                    continue;

                TryGetUInt32(listingElement, "worldID", out var worldId);
                var worldName = listingElement.TryGetProperty("worldName", out var worldNameElement)
                    ? worldNameElement.GetString() ?? worldId.ToString(CultureInfo.InvariantCulture)
                    : worldId.ToString(CultureInfo.InvariantCulture);

                var isHighQuality = listingElement.TryGetProperty("hq", out var hqElement) && hqElement.GetBoolean();
                var reviewTime = TryGetInt64(listingElement, "lastReviewTime", out var reviewSeconds) && reviewSeconds > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(reviewSeconds)
                    : (DateTimeOffset?)null;

                listings.Add(new MarketListing(itemId, worldId, worldName, pricePerUnit, quantity, isHighQuality, reviewTime));
            }
        }

        var status = listings.Count > 0 ? MarketDataStatus.Available : MarketDataStatus.NoListings;
        destination[itemId] = new ItemMarketData(itemId, lastUploadTime, listings, status);
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out value);
    }

    private static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out value);
    }

    private static bool TryGetUInt32(JsonElement element, string propertyName, out uint value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property) && property.TryGetUInt32(out value);
    }

    private sealed record CacheEntry(DateTimeOffset CachedAt, ItemMarketData Data);
}
