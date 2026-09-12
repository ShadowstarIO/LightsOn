# Privacy

LightsOn exists to answer “is anyone at this listed venue?” without building a player census.

## On the client only

- Names of nearby players
- Friend list
- Free Company tag comparison
- The numeric player count used for the 3+ check

Friends and FC members can be subtracted before the 3+ boolean is decided. Those lists never leave the machine.

## Opt-in reports (HTTPS)

Only if you turn **Send reports** on, and only when you click a report button after a successful on-plot scan:

- FFXIV Venues id
- `happening` or `wrapped_up`
- Resettable random reporter id (not a name, not a Content ID)
- World, district, ward, plot, subdivision, inside/outside
- `thresholdMet` (boolean)

## Never collected

- Other players’ names, Content IDs, or account IDs
- Your character name or Content ID
- Exact coordinates
- Unlisted houses
- Passive 5-minute zone scans

## Directory

The venue list is the public [FFXIV Venues API](https://api.ffxivvenues.com/docs/v1.0). LightsOn does not add occupancy to houses that are not on that list.

## Reporter id

Generated locally. Settings → Reset reporter id. After a reset, old reports cannot be tied to the new id from the plugin’s side.
