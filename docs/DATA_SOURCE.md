# Market Data Source

MarketRoute CN uses Universalis REST API v2.

Relevant endpoints:

```text
GET /api/v2/{worldDcRegion}/{itemIds}
GET /api/v2/data-centers
GET /api/v2/marketable
```

The current-price endpoint accepts up to 100 comma-separated item IDs per request. The client batches longer lists and requests `entries=0` because purchase planning needs current listings rather than sale history.

## Freshness

The plugin displays:

1. **Query time** — when MarketRoute CN requested the API;
2. **Item upload time** — `lastUploadTime` returned for the item;
3. **Listing review time** — `lastReviewTime` returned for a selected listing.

These times must not be presented as equivalent. Universalis is crowdsourced, so a fresh query may still return old market data.
