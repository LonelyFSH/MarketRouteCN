# Architecture

MarketRoute CN separates local item lookup, shopping-list persistence, text transfer, market requests, route optimization, quote history and purchase-session tracking.

The V0.9 interface uses a compact persistent workspace. The primary navigation contains overview, shopping list, quotes, route and purchase progress. Import, history and settings are grouped under the secondary navigation.

Inside one data center, the optimizer enumerates server subsets. Each item is solved against complete listings with exact dynamic programming for bounded cases and a greedy fallback for large cases.

When cross-data-center analysis is enabled, the optimizer enumerates non-empty subsets of the four Chinese data centers. Each requested item is assigned to one complete data-center plan, then the route is scored with configurable data-center, server, overbuy and stale-data costs.

An active purchase session listens for confirmed market-board purchases. A listing is completed only when the item ID, stack quantity and current world match an unfinished route listing. Inventory changes are used to resolve HQ and NQ when the purchase event alone is ambiguous.
