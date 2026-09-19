<p align="center">
  <img src="images/icon.png" width="180" alt="LightsOn icon" />
</p>

<h1 align="center">LightsOn</h1>

<p align="center">
  <a href="https://github.com/ShadowstarIO/LightsOn/releases/latest"><img alt="Release" src="https://img.shields.io/github/v/release/ShadowstarIO/LightsOn?style=flat-square&color=blue"></a>
  <a href="https://github.com/ShadowstarIO/LightsOn/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/ShadowstarIO/LightsOn/total?style=flat-square&color=blue&cacheSeconds=300"></a>
  <a href="https://github.com/ShadowstarIO/LightsOn/actions/workflows/build.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/ShadowstarIO/LightsOn/build.yml?style=flat-square"></a>
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square"></a>
</p>

<p align="center">
  <em>Posted hours, empty room. See if the lanterns are lit. Built on Dalamud.</em>
</p>

---

## What it does

Shows occupancy for listed FFXIV venues: **Lanterns Lit** when there is enough company, **Quiet Halls** when the yard or room looks empty. A second tab covers short outdoor scenes. Reports never include names or headcounts.

Listings come from [FFXIV Venues](https://ffxivvenues.com/). Occupancy is LightsOn.

## Features

- **Venue directory** — filter by data center, world, and status. Select a listing for address, hours, occupancy, and the log book.
- **On-plot reports** — stand on the property (the yard counts) and **Audit**, mark **Active**, or mark **Quiet**. Apartments need you inside.
- **Current Plot** — a small window for the house or apartment you are standing on.
- **Outdoors** — street pockets, not houses and not a whole zone. Audit and stay near the start point, or skip it.
- **Travel and Copy** — uses [Lifestream](https://github.com/NightmareXIV/Lifestream) when it is installed. Copy still works without it.
- **Send Reports** — on by default. Names stay on your machine. **Auto Audit Routine** (Settings) can send lanterns after a stay; it stays off until you turn it on. Quiet is never automatic.
- **Log book** — a pair from two lists (*Kind Crowd*, *Warm Food*) while lanterns are lit. Flavor only, not an occupancy point.

## Install

1. In game, open `/xlsettings`.
2. Open the **Experimental** tab.
3. Under **Custom Plugin Repositories**, paste:

```
https://raw.githubusercontent.com/ShadowstarIO/XIV/main/repo.json
```

4. Tick **Enabled**, click **+**, then **Save and Close**.
5. Open `/xlplugins` → **All Plugins**, search **LightsOn**, and install.

The same catalog lists [StatusShift](https://github.com/ShadowstarIO/StatusShift). Do not also add this plugin’s own `repo.json`; the same internal name in two catalogs duplicates the installer row.

## Commands

| Command | Action |
| --- | --- |
| `/lightson` | Toggle the main window |
| `/lon` | Alias for `/lightson` |
| `/lon here` | Print current world and housing plot |
| `/lon plot` | Toggle Current Plot |
| `/lon config` | Open Settings |
| `/lon refresh` | Reload listings |

## Privacy

Names, friend lists, Free Company tags, the company score, and chat text stay on your machine. A sent report is a listed venue id, lanterns or quiet, a random reporter id, and a coarse plot. Details: [Privacy](docs/PRIVACY.md).

Other plugins can read the same occupancy: [API](docs/API.md).

## License

MIT. See [LICENSE](LICENSE).

Icon: [birthday and party icons by Magnific — Flaticon](https://www.flaticon.com/free-icons/birthday-and-party).
