# LightsOn

See if an FFXIV venue has **company** — **lanterns lit**, or **wrapped up early**.

Posted hours lie. LightsOn is the occupancy layer on top of the public [FFXIV Venues](https://ffxivvenues.com/) list. A place with enough company is lanterns lit. A locked door and an empty yard is wrapped up early.

`/lightson` or `/lo`

## What it sends

Reports are **opt-in**. A report is only allowed after a scan **on that plot**.

The plugin never uploads:

- character names
- Content IDs or account IDs
- friend or FC lists
- a player count

The server only sees: venue id, lanterns lit or wrapped up early, a resettable random reporter id, and that enough company passed (a boolean). Friends and FC members are subtracted on your client before that boolean is decided.

A scan with enough company cannot file wrapped-up. Several reports raise confidence. Reports age out in about 20 minutes.

See [docs/PRIVACY.md](docs/PRIVACY.md), [docs/API.md](docs/API.md), and [docs/ACTIVITY.md](docs/ACTIVITY.md) (OOC/IC wording).

See [docs/PRIVACY.md](docs/PRIVACY.md) and [docs/API.md](docs/API.md).

## Test in Dalamud

Custom plugin repo:

```
https://raw.githubusercontent.com/XozaShadow/LightsOn/main/repo.json
```

Dalamud → Settings → Experimental → Custom Plugin Repositories → add that URL → Save → Plugin Installer → LightsOn.

Occupancy API is already defaulted to `https://lightson.wbro12-cloudflare.workers.dev`.

## Status

Early source. Not in the Dalamud plugin installer yet. Occupancy API is a Cloudflare Worker in [`worker/`](worker/), deployed by GitHub Actions. After the first deploy, the URL is `https://lightson.wbro12-cloudflare.workers.dev` — paste that origin into plugin Settings.

The venue list loads from the public FFXIV Venues API. Occupancy stays empty until that URL is set.

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
