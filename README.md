# LightsOn

See if an FFXIV venue has people there — **happening** or **wrapped up early**.

Posted hours lie. LightsOn is the occupancy layer on top of the public [FFXIV Venues](https://ffxivvenues.com/) list. It does not rank venues by size or “how busy.” A place with three people is happening. A locked door and an empty yard is wrapped up early.

`/lightson` or `/lo`

## What it sends

Reports are **opt-in**. A report is only allowed after a scan **on that plot**.

The plugin never uploads:

- character names
- Content IDs or account IDs
- friend or FC lists
- a player count

The server only sees: venue id, `happening` or `wrapped_up`, a resettable random reporter id, and that the 3+ check passed (a boolean). Friends and FC members are subtracted on your client before that boolean is decided.

A passing 3+ scan cannot file wrapped-up. Several reports raise confidence. Reports age out.

See [docs/PRIVACY.md](docs/PRIVACY.md) and [docs/API.md](docs/API.md).

## Status

Early source. Not in the Dalamud plugin installer yet. No public occupancy API is wired — set one in Settings when that endpoint exists. The venue list already loads from the public FFXIV Venues API.

## Build

Windows, .NET 10, Dalamud API 15.

```
dotnet build LightsOn.slnx -c Release
```

D17 needs `images/icon.png` at 512×512. SVG source is `images/icon.svg`.

## Commands

| Command | Action |
| --- | --- |
| `/lightson` `/lo` | Open the window |
| `/lo here` | Print current world and housing plot |
| `/lo config` | Settings |

## Settings

- Exclude friends from the 3+ check
- Exclude Free Company members (same company tag, client-side)
- Opt in to sending reports
- Reset reporter id
- Occupancy API URL (HTTPS hostname, not an IP)

Source: https://github.com/XozaShadow/LightsOn
