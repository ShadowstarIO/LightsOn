# Privacy

LightsOn answers “is anyone at this listed venue?” and “is there a scene in this outdoor pocket?” without building a census.

## On the client only

- Names of nearby people
- Friend list
- Free Company tag comparison
- The numeric score used for “enough company”
- Online-status labels (IC, Party Finder, melding)
- Who is targeted / targeting you (glances)
- That a tell, party line, or say happened with someone already in the scan — never the text

Those never leave the machine.

## Opt-in (HTTPS)

Only with **Send Reports** on:

**Venue occupancy** — listed venue id; lanterns lit or wrapped up early; random reporter id; world / district / ward / plot / inside; `thresholdMet` (boolean). On-plot only.

**Log Book** — same proof, plus a pair from two lists. Only while lanterns are lit, after ~8 minutes on the plot.

**Outdoor scenes** — world, place name, pocket id, tier, in-character boolean, optional private-gathering vote. No names.

Listings are fetched from the public community venue directory ([FFXIV Venues](https://ffxivvenues.com/)). Occupancy is LightsOn’s host. Occupancy GET is public; writes are not.

## Never collected

- Other people’s names, Content IDs, or account IDs
- Your character name or Content ID
- Exact coordinates (outdoor pockets are a coarse cell)
- Unlisted houses
- Chat logs

## Reporter ID

Generated locally. Settings → Reset ID. Reports wait 20 minutes after a reset.
