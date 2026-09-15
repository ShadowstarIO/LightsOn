# LightsOn occupancy API

Public **GET**. Other plugins and sites can read the same occupancy the list uses. No names, no reporter ids, no counts of people.

Writes (**POST**) are not a public API. They require a private ingest key compiled into the shipping plugin.

Testing host (public GET): `https://lightson.wbro12-cloudflare.workers.dev`

Writes (**POST**) are not a public API. They require a private ingest key compiled into the shipping plugin.

Windows: **60 minutes** lanterns, **45 minutes** quiet, cap 12 rows. Notes: **7 days**, cap 12.

## Read (public)

| Method | Path | What |
| --- | --- | --- |
| GET | `/v1/health` | Worker up, recent report count |
| GET | `/v1/occupancy` | Snapshot per listing: `venueId`, `state` (`happening` \| `wrapped_up` \| `mixed`), layer counts, `doorLocked`, `updatedAt`, `expiresAt` |
| GET | `/v1/reports?venueId=` | Last 60 minutes for one listing. Kind, yard vs inside, chips. Cap 12. |
| GET | `/v1/notes?venueId=` | Active log-book lines. Phrases only. |
| GET | `/v1/outdoors` | Short-range outdoor pockets |

Cached about 60s. CORS is open for GET.

Lean is interior×2 + exterior, happening minus quiet.

## Write (plugin only)

`POST /v1/reports`, `/v1/notes`, `/v1/outdoors` — not for third-party clients. If you want to contribute from another app, talk to the maintainer; do not scrape an ingest key.

Want to contribute later from another plugin? Same proof rules: on-plot, posted hours, no names or counts, one packed write per send.
