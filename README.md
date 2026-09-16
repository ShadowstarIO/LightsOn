# LightsOn

**Testing pre-release.** Occupancy for listed Final Fantasy XIV venues: lanterns lit when there is enough company, quiet when posted hours meet an empty yard or hall. It does not rank venues by size.

Listings come from [FFXIV Venues](https://ffxivvenues.com/). Occupancy is LightsOn.

`/lightson` or `/lon`

## Install (testing)

Plugin installer → settings → experimental → custom repositories:

```
https://raw.githubusercontent.com/XozaShadow/LightsOn/main/repo.json
```

Enable **Get plugin testing versions**, then install LightsOn.

Every drop is testing-only until there is an explicit live release.

## Reports

Off until **Send Reports** is on. Never uploaded: names, IDs, friend or Free Company lists, or a headcount.

A check can be sent from the yard or the room. They list separately. The room weighs more. Ward + Plot is the property. Apartments use Ward + Apt (Subdivision when it is one).

Quiet is always a button. Lanterns may send themselves, then taper off as more people agree. After three different listed places in a night, sends wait 20 seconds instead of 45.

Public occupancy lasts while posted hours are on, **up to 4 hours**. Older reports fade (each hour divides their weight), so a fresh quiet can beat a stale busy. Log-book notes: **7 days**, cap 12, after about 8 minutes on the property.

## Log Book

Two lists, not free-form. Example: *Kind Host*, *Warm Music*. One note per person per venue per hour. Flavor only — not an occupancy point.

## Outdoors

A second tab for short-range street scenes. **Audit** and stay in the area (about a minute; busier scenes finish sooner), or wait about 10 minutes. Private gatherings can be kept off the list. The public list is about 20 minutes.

## Commands

| Command | Action |
| --- | --- |
| `/lightson` `/lon` | Open the window |
| `/lon here` | Current world and housing plot |
| `/lon plot` | Toggle Current Plot |
| `/lon config` | Settings |
| `/lon refresh` | Reload listings |

## Other plugins

Occupancy **GET** is public so other plugins and sites can read the same snapshots. Writes stay with the shipping plugin. See [API](docs/API.md).

## Docs

- [Privacy](docs/PRIVACY.md)
- [Activity wording](docs/ACTIVITY.md)
- [API](docs/API.md)
- [Outdoors](docs/OPENWORLD.md)

Source: https://github.com/XozaShadow/LightsOn

<a href="https://www.flaticon.com/free-icons/birthday-and-party" title="icons">Icons created by Magnific - Flaticon</a>
