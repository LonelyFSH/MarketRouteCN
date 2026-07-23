# Architecture

MarketRoute CN separates local item lookup, shopping-list persistence, text transfer, market requests, route optimization, quote history and purchase-session tracking.

The V0.8 interface uses one persistent workspace page state. User actions can move directly from list creation to quote results, from a data-center quote to its route, and from a route to a purchase session.

The optimizer enumerates server subsets inside each data center. Each item is solved against complete listings with exact dynamic programming for bounded cases and a greedy fallback for large cases. The quote layer also calculates a fallback cost after removing one selected listing to expose price fragility.
