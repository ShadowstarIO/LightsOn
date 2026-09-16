# LightsOn occupancy API

Public **GET**. Other plugins and sites can read the same occupancy the list uses. No names, no reporter ids.

Host: `https://lightson.xoza.net`  
Fallback: `https://REDACTED`

Writes (**POST**) are not a public API. They require a private ingest key compiled into the shipping plugin.

Windows: occupancy while posted hours are on, **up to 4 hours**, weight `1 / max(1, hours old)`. Cap 12 rows. Notes: **7 days**, cap 12. Outdoors list: **4 hours**.

## Read (public)

| Method | Path | What |
| --- | --- | --- |
| GET | `/v1/health` | Worker up, recent report count |
| GET | `/v1/occupancy` | Snapshot per listing: `venueId`, `state` (`happening` \| `wrapped_up` \| `mixed`), layer counts, `lean`, `doorLocked`, `updatedAt`, `expiresAt` |
| GET | `/v1/reports?venueId=` | Last 4 hours for one listing, while hours are on. Kind, yard vs inside, chips. Cap 12. |
| GET | `/v1/notes?venueId=` | Active log-book lines. Phrases only. |
| GET | `/v1/outdoors` | Outdoor reports, last 4 hours. Pocket, world, place, zone, tier, nearby/zone counts (cap 99), chips, optional scene. No names. |

Cached about 60s. CORS is open for GET.

Lean is interior×2 + exterior, happening minus quiet, each report weighted by age (`1 / max(1, hours)`). Unique reporters; latest per person per layer. Venue occupancy does not publish a headcount. Outdoors publishes nearby/zone counts (99+).

## Write (plugin only)

`POST /v1/reports`, `/v1/notes`, `/v1/outdoors` — not for third-party clients. If you want to contribute from another app, talk to the maintainer; do not scrape an ingest key.

Want to contribute later from another plugin? Same proof rules: on-plot, posted hours, no names, one packed write per send.
