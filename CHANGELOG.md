# Changelog

## 0.9.5.0

- Added 250, 500 and 1000 millisecond inventory polling during active purchasing sessions.
- Added a one-second stability window before inventory increases are recorded.
- Added partial quantity progress for every planned listing.
- Combined market purchase signals, current world checks, item identity and quality checks.
- Added automatic recording at the current route stop and confirmation prompts for lower-confidence changes.
- Added one-click undo for the latest automatic inventory record.
- Updated remaining-route refresh to subtract partially acquired quantities.
- Preserved acquired quantities when applying a refreshed route.

## 0.8.0.0

- Rebuilt the main interface as a persistent workspace with overview, sidebar navigation and direct workflow links.
- Added automatic navigation from refresh to quotes, from quote cards to routes, and from routes to purchase sessions.
- Added one-click use of the cheapest complete plan and one-click completion of the current world with advance to the next world.
- Added plain-text, MakePlace-style, CSV and JSON import plus CSV and JSON clipboard export.
- Added recent item shortcuts, list sorting and direct add-and-quote actions.
- Added quote trend comparison, fallback risk pricing, liquidity warnings, freshness confidence and total-price targets.
- Added compact mode, onboarding controls and command shortcuts for list, import, quote, route, session and settings.
- Migrated configuration schema to version 8 while preserving V0.5 lists, history and active sessions.

## 0.5.0.0

- Added persistent shopping lists, quote history, server-subset optimization and purchasing sessions.
