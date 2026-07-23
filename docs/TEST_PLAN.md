# V0.9 test plan

## Upgrade

- Upgrade from V0.8 and verify shopping lists, quote history and active purchase session remain available.
- Confirm the configuration schema becomes version 9.
- Confirm the numbered 1 2 3 4 workflow strip is absent.

## Automatic purchase recording

- Start a route on the current world and buy the exact planned listing quantity.
- Verify the corresponding row is checked automatically.
- Verify the row shows the automatic marker.
- Buy the same item with a different stack quantity and verify no unrelated row is completed.
- Buy the planned item on a different world and verify no unrelated row is completed.
- Test both HQ and NQ listings.
- Complete the last pending listing at a stop and verify automatic advance when enabled.
- Disable automatic recording and verify manual checkboxes still work.

## Cross-data-center analysis

- Enable Advanced options and Cross-data-center mixed route analysis.
- Select Cross-data-center mixed route and refresh prices.
- Verify the four normal data-center plans and the mixed plan are all displayed.
- Verify mixed route stops show both data-center and world names.
- Verify a mixed purchase session can be saved, reloaded and refreshed for remaining items.
- Disable Advanced options and verify the scope returns to four-data-center comparison.

## Interface

- Confirm the primary sidebar contains Overview, List, Quotes, Route and Purchase.
- Confirm Import, History and Settings are under More.
- Confirm direct Start buttons open a purchase session without an intermediate route click.
- Confirm Simple interface hides secondary risk and data columns.
- Confirm the data notice contains only the Universalis source statement.

## Stability

- Reload and unload the plugin while a price refresh is active.
- Reload and unload the plugin while a purchase session is active.
- Test network failure, empty listings and stale data.
