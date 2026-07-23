# MarketRoute CN

MarketRoute CN is a Dalamud plugin for planning batch market-board purchases on the Chinese FFXIV service.

## V0.9 highlights

- Streamlined workspace without the numbered workflow strip
- Direct links from quotes to routes and purchase sessions
- Automatic completion of matching planned listings after a confirmed market-board purchase
- Automatic synchronization to a planned stop when the character changes world
- Optional automatic advance after every listing at the current stop is complete
- Advanced cross-data-center mixed-route analysis
- Compact quote cards, route summaries and purchase tables

## Main features

- Multiple persistent shopping lists
- Local marketable-item search using game data
- Quantity and Any, HQ or NQ requirements
- Plain text, MakePlace-style, CSV and JSON import and export
- Single-data-center procurement and four-data-center complete-price comparison
- Optional cross-data-center mixed procurement analysis
- Complete-listing bundle optimization
- Exact server-subset route optimization inside each data center
- Lowest price, balanced and fewest servers strategies
- Quote request time and market-data age
- Persistent purchase sessions and remaining-route refresh
- Automatic refresh with local request caching

## Automatic purchase recording

During an active purchase session, the plugin listens for a confirmed market-board purchase. It marks a planned listing complete only when the item ID, purchased stack quantity and current world match an unfinished route listing. HQ ambiguity is resolved with the subsequent inventory change when necessary. Manual checkboxes remain available as a fallback.

## Cross-data-center analysis

Enable Advanced options and Cross-data-center mixed route analysis in Settings. Then select Cross-data-center mixed route on the shopping-list page before refreshing prices.

The optimizer compares non-empty subsets of the four Chinese data centers. Each requested item is assigned to the lowest-scoring complete plan in one selected data center, and the final score can include additional data-center and server costs.

## Commands

```text
/marketroute
/mrcn
/mrcn list
/mrcn import
/mrcn quote
/mrcn route
/mrcn session
/mrcn settings
```

## Data notice

Market listings are requested from the Universalis community data service and are not real-time market data supplied by the game operator.

## Build

The project targets Dalamud API 15 through `Dalamud.NET.Sdk/15.0.0`, .NET 10 and x64.

```text
dotnet restore MarketRouteCN.sln
dotnet build MarketRouteCN.sln -c Release
```

## Custom repository

```text
https://raw.githubusercontent.com/LonelyFSH/MarketRouteCN/main/repo.json
```
