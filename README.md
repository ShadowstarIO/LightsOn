# LightsOn

Posted hours, empty room. LightsOn is a Dalamud plugin that tells you if a listed venue actually has people — **Lanterns Lit** or **Quiet Halls**. A second tab covers short outdoor scenes. No names, no headcounts.

Listings come from [FFXIV Venues](https://ffxivvenues.com/). Occupancy is ours.

`/lightson` or `/lon`.

## Install

Dalamud plugin installer → **Settings → Experimental → Custom Plugin Repositories**, add:

```
https://raw.githubusercontent.com/ShadowstarIO/XIV/main/repo.json
```

Save, `/xlplugins`, install LightsOn.

## Use

**Venues** is the directory. Filter, pick a listing, see hours and whether anyone has said the lanterns are lit.

Stand on the property (the yard counts) and you can **Audit**, mark **Active**, or mark **Quiet**. Apartments need you inside. Travel and Copy use [Lifestream](https://github.com/NightmareXIV/Lifestream) if you have it; Copy still works without it.

**Outdoors** is a pocket of street, not a house and not a whole zone. Audit, stay put for about a minute, or skip it.

**Current Plot** is the small window for the house or apartment you are standing on (`/lon plot`).

Send Reports is on. Names never leave your machine. **Auto Audit Routine** in Settings will send lanterns after you hang around — that one stays off unless you turn it on. Quiet is never automatic.

## Commands

| | |
| --- | --- |
| `/lightson` `/lon` | Window |
| `/lon here` | Where you are |
| `/lon plot` | Current Plot |
| `/lon config` | Settings |
| `/lon refresh` | Reload listings |

## Privacy

What other people are named, who is in your FC, and anything said in chat stay on your PC. If a report goes out, it is a venue id, lanterns or quiet, and a random reporter id. [More](docs/PRIVACY.md).

Other plugins can read the same occupancy: [API](docs/API.md).

[MIT](LICENSE) · [Icons by Magnific — Flaticon](https://www.flaticon.com/free-icons/birthday-and-party)
