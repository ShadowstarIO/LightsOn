# Privacy

LightsOn answers “is anyone at this listed venue?” and “is there a scene in this outdoor pocket?” without building a census.

## On the client only

- Names of nearby people
- Friend list
- Free Company tag comparison
- The numeric score used for “enough company”
- Online-status labels used as extra score (in character, seeking company, at the bench)
- Who is targeted / targeting you (a glance)
- That a tell, party line, or say happened with someone already in the scan — never the text

Those never leave the machine.

## Opt-in (HTTPS)

Only with **Send reports** on:

**Venue occupancy** — listed venue id; lanterns lit or wrapped up early; random reporter id; world / district / ward / plot / subdivision / inside; `thresholdMet` (boolean). On-plot only.

**Log book** — same proof, plus a short text field (2–80 characters, no links). Only while lanterns are lit, after ~20 minutes on the plot.

**Outdoor scenes** — world, place name, pocket id, tier, in-character boolean, optional private-gathering vote. No names.

Listings are fetched from the public community venue directory (`https://api.ffxivvenues.com/venue`). Occupancy is LightsOn’s host. Polling is slow on purpose so that host stays cheap.

## Never collected

- Other people’s names, Content IDs, or account IDs
- Your character name or Content ID
- Exact coordinates (outdoor pockets are a coarse cell)
- Unlisted houses
- Chat logs (the words themselves are never uploaded; an optional boolean that someone in range spoke can be scored locally)

## Reporter id

Generated locally. Settings → Reset reporter id.
