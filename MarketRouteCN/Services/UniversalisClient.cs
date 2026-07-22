using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Dalamud.Plugin.Services;
using MarketRouteCN.Models;

namespace MarketRouteCN.Services;

public sealed class UniversalisClient : IDisposable
{
    private const string BaseAddress = "https://universalis.app/api/v2/";

    private readonly HttpClient httpClient;
    private readonly IPluginLog log;

    public UniversalisClient(IPluginLog log)
    {
        this.log = log;
        httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseAddress),
            Timeout = TimeSpan.FromSeconds(45),
        };

        httpClient.DefaultRequestHeaders.UserAgent.Clear();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MarketRouteCN", "0.1.0"));
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/LonelyFSH/MarketRouteCN)"));
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }

    // 分批查询市场挂单
    public async Task<IReadOnlyDictionary<uint, ItemMarketData>> GetListingsAsync(
        string dataCenter,
        IReadOnlyCollection<uint> itemIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<uint, ItemMarketData>();

        foreach (var batch in itemIds.Distinct().Chunk(100))
        {
            var ids = string.Join(',', batch.Select(static id => id.ToString(CultureInfo.InvariantCulture)));
            var relativeUrl = $"{Uri.EscapeDataString(dataCenter)}/{ids}?entries=0";

            using var response = await httpClient.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

            ParseResponse(document.RootElement, result);
        }

        return result;
    }

    // 解析市场数据
    private void ParseResponse(JsonElement root, IDictionary<uint, ItemMarketData> destination)
    {
        if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in itemsElement.EnumerateObject())
                ParseItem(property.Value, destination);

            return;
        }

        ParseItem(root, destination);
    }

    private void ParseItem(JsonElement itemElement, IDictionary<uint, ItemMarketData> destination)
    {
        if (!TryGetUInt32(itemElement, "itemID", out var itemId))
            return;

        var lastUploadTime = TryGetInt64(itemElement, "lastUploadTime", out var uploadMilliseconds) && uploadMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(uploadMilliseconds)
            : (DateTimeOffset?)null;

        var listings = new List<MarketListing>();
        if (itemElement.TryGetProperty("listings", out var listingsElement) &&
            listingsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var listingElement in listingsElement.EnumerateArray())
            {
                if (!TryGetInt32(listingElement, "pricePerUnit", out var pricePerUnit) || pricePerUnit <= 0 ||
                    !TryGetInt32(listingElement, "quantity", out var quantity) || quantity <= 0)
                {
                    continue;
                }

                TryGetUInt32(listingElement, "worldID", out var worldId);
                var worldName = listingElement.TryGetProperty("worldName", out var worldNameElement)
                    ? worldNameElement.GetString() ?? worldId.ToString(CultureInfo.InvariantCulture)
                    : worldId.ToString(CultureInfo.InvariantCulture);

                var isHighQuality = listingElement.TryGetProperty("hq", out var hqElement) && hqElement.GetBoolean();
                var reviewTime = TryGetInt64(listingElement, "lastReviewTime", out var reviewSeconds) && reviewSeconds > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(reviewSeconds)
                    : (DateTimeOffset?)null;

                listings.Add(new MarketListing(
                    itemId,
                    worldId,
                    worldName,
                    pricePerUnit,
                    quantity,
                    isHighQuality,
                    reviewTime));
            }
        }

        destination[itemId] = new ItemMarketData(itemId, lastUploadTime, listings);
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
}
