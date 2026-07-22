# Architecture

```text
Local CN game data
  └─ ItemCatalogService
       └─ item name search / tradeable filter / HQ capability

User shopping list
  └─ ShoppingListService
       └─ ItemId + quantity + quality

Universalis API
  └─ UniversalisClient
       └─ listings grouped by CN data center and world

PurchaseOptimizer
  ├─ full-listing quantity selection
  ├─ item purchase plan
  ├─ data-center complete plan
  └─ server-grouped route

PriceRefreshService
  ├─ manual refresh
  ├─ 5/10/15/30/60-minute schedule
  ├─ single-DC mode
  └─ four-DC comparison mode

MainWindow
  ├─ custom list
  ├─ data-center comparison
  ├─ purchase route
  └─ settings/data timestamps
```

## Design boundaries

- InternalName is permanently `MarketRouteCN`.
- Market data is read-only.
- V0.1 does not interact with the market-board UI or purchase items.
- A shopping entry is stored by ItemId, not by the user's raw search text.
- Query time and market-data time are separate fields throughout the model.
