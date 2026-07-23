# MarketRoute CN

MarketRoute CN is a Dalamud plugin for planning batch market-board purchases on the Chinese FFXIV service.

## V0.8 workflow

The main window is a continuous workspace rather than a set of isolated tabs.

- Overview shows the current list, latest complete quote, data confidence and purchase progress.
- The workflow strip jumps directly between list, quote, route and purchase stages.
- A price refresh can automatically open the quote result.
- Quote cards open a selected data-center route or start a purchase session immediately.
- The purchase page can complete the current world and advance to the next world in one action.

## Main features

- Multiple persistent shopping lists
- Local marketable-item search using game data
- Quantity and Any, HQ or NQ requirements
- Plain text and MakePlace-style import
- CSV and JSON clipboard import and export
- Single-data-center procurement or four-data-center complete-price comparison
- Complete-listing bundle optimization
- Exact server-subset route optimization
- Lowest price, balanced and fewest servers strategies
- Quote request time and market-data age
- Quote trend comparison against previous snapshots
- Fallback risk price and low-liquidity warnings
- Optional total-price target
- Persistent purchase sessions and inventory increase suggestions
- Automatic refresh with local request caching

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

Item metadata is read from the local game client. Market listings are requested from the Universalis community data service. The displayed listings may be stale or may no longer exist in the in-game market board.

The plugin does not automate world travel, market-board interaction or purchasing.

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
