# LightsOn

Tired of looking for a place that’s open, walking the ward, and finding the lights out? LightsOn is there so you spend less time on empty plots and more time where something is actually happening — and so you can tell others, quietly, what you found.

**Lanterns lit** means enough company on a listed plot. **Yard quiet / halls quiet** means the hours said open, but that layer looks empty. It does not rank venues by size. A small lounge with three patrons counts.

A check can be sent from the **yard or the room** — they list separately. The room weighs more. **Ward + Plot** is the property; rooms add to it. `sub` is only for apartments.

`/lightson` or `/lon`

## Testing

Every drop is a **pre-release / testing** build until there is an explicit live release.

Custom repository:

```
https://raw.githubusercontent.com/XozaShadow/LightsOn/main/repo.json
```

Plugin installer → settings → experimental → custom repositories → add that URL. Enable **Get plugin testing versions**, then install LightsOn.

Occupancy host: `https://REDACTED`

## What it sends

Reports are **opt-in**. Nothing goes out until you turn **Send reports** on.

The occupancy host stores **reports only**, and only while that listing is in posted hours. The venue directory is read from the public listing API; it is not copied into LightsOn's database. Writes are accepted only from the shipping plugin. Reading occupancy is public.

Never uploaded:

- character names
- Content IDs or account IDs
- friend or Free Company lists
- a headcount

The server sees a venue or outdoor pocket id, lanterns lit / wrapped up early / a scene tier, a resettable random reporter id, and booleans (enough company, in character, private gathering). Friends and Free Company are subtracted on your machine first.

A scan with enough company cannot file wrapped up early. Several unique reporters raise confidence. Public occupancy lasts about **60 minutes** for lanterns, **45 minutes** for quiet. History is kept for hosts, not shown as a graveyard of old closures.

## Log book

While lanterns are lit, someone who has been on that plot about **8 minutes** (yard and halls count as one stay) can leave a short pair from two lists: *Kind host*, *Warm music*. Not a rating. No free-form text. Notes fade after **7 days**. One note per person per venue per day. Cap 12.

## Outdoors

A second tab for moving or street scenes. Short range (a pocket, not a whole city). After about **10 minutes** in the same pocket it can note **extremely busy**, **some activity**, or **some wandering**, plus an **in character** chip. If most of the company looks like friends or Free Company, it asks whether the gathering is private. Enough “yes, private” votes hide it; enough “no, public” votes list it.

## Settings

- Listings only (hours, no occupancy fetched or sent)
- Send reports (master opt-in)
- Auto lanterns when the scan is enough (quiet is always a button)
- Open Current plot when you walk onto an open listed venue
- Leave friends / Free Company out of company
- Count in-character / seeking company / at the bench as extra score
- Count a glance (looking at / looked at)
- Count tells and party chat with patrons here (say is off unless you turn it on)
- Log book
- Note outdoor scenes
- Reset reporter id (reports wait 20 minutes after a reset)

## Commands

| Command | Action |
| --- | --- |
| `/lightson` `/lon` | Open the window |
| `/lon here` | Current world and housing plot |
| `/lon plot` | Current plot window |
| `/lon config` | Settings |
| `/lon refresh` | Reload listings |

## Docs

- [Privacy](docs/PRIVACY.md)
- [Activity wording](docs/ACTIVITY.md)
- [API](docs/API.md)
- [Outdoors](docs/OPENWORLD.md)

Listings are loaded from the public community venue directory. Occupancy is LightsOn.

Source: https://github.com/XozaShadow/LightsOn

<a href="https://www.flaticon.com/free-icons/birthday-and-party" title="icons">Icons created by Magnific - Flaticon</a>
