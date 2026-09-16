# LightsOn

Tired of walking in on posted hours and an empty room?

LightsOn is a [Dalamud](https://github.com/goatcorp/Dalamud) plugin for Final Fantasy XIV. It shows occupancy for listed venues: **Lanterns Lit** when there is enough company, **Quiet Halls** when the yard or room looks empty. A second tab notes short outdoor scenes. Reports never include names or headcounts.

Listings come from [FFXIV Venues](https://ffxivvenues.com/). Occupancy is LightsOn.

Open with `/lightson` or `/lon`.

## Install

In the plugin installer: **Settings → Experimental → Custom Plugin Repositories**, add:

```
https://raw.githubusercontent.com/XozaShadow/LightsOn/main/repo.json
```

Turn on **Get plugin testing versions**, then install LightsOn.

## Using it

- **Venues** is the directory. Filter by data center, world, and status. Select a listing for address, hours, occupancy, and the log book.
- **Outdoors** is street pockets, not houses.
- **Current Plot** is a small window for the property you are standing on.
- Reports stay off until **Send Reports** is on in Settings.

Travel and Copy use [Lifestream](https://github.com/NightmareXIV/Lifestream) when it is installed. Copy still works without it.

## Occupancy

A check is local until you send it. Yard and inside are separate; the room weighs more. Houses are ward and plot. Apartments are ward and apartment (and subdivision when the listing is in one). You have to be on that property.

**Quiet** is always a button. **Active** (Lanterns Lit) may send itself after a short watch, then slows down as more people agree. After three different listed places in a night, waits drop so you can tour.

Occupancy lasts while the directory shows the listing as open, up to four hours. Older reports fade, so a fresh quiet can beat a stale busy.

Friends and Free Company can be left out of the company check. Chat text is never uploaded.

## Log book

Two lists, not free-form. Pairs look like *Kind Crowd* or *Warm Food*. One note per person per venue per hour, after about eight minutes on the property, and only while lanterns are lit. Flavor only — not an occupancy point. Notes last about seven days.

## Outdoors

Street pockets (~40 yalms), not a whole zone. **Audit** and stay near the start point for about a minute (busier scenes finish sooner), or wait about ten minutes with outdoor reports on. Walking away pauses; too far cancels. Same area waits five minutes before another audit. Optional scene tag (RP, Party, Hunt, …). Private gatherings can be kept off the list. Reports last about four hours.

## Commands

| Command | Action |
| --- | --- |
| `/lightson` `/lon` | Open the window |
| `/lon here` | Current world and housing plot |
| `/lon plot` | Toggle Current Plot |
| `/lon config` | Settings |
| `/lon refresh` | Reload listings |

## Privacy

Names, friend lists, Free Company tags, the numeric company score, and chat text stay on your machine. What leaves, if you opt in, is a listed venue id, lanterns or quiet, a random reporter id, and a coarse plot proof. Details: [Privacy](docs/PRIVACY.md).

## Docs

- [Privacy](docs/PRIVACY.md)
- [What the lantern reads](docs/ACTIVITY.md)
- [Outdoors](docs/OPENWORLD.md)
- [Occupancy API](docs/API.md) — public GET for other plugins and sites

## License

[MIT](LICENSE)

<a href="https://www.flaticon.com/free-icons/birthday-and-party" title="icons">Icons created by Magnific - Flaticon</a>
