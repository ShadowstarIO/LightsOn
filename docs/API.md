# Occupancy API

Other plugins and sites can **GET** the same occupancy LightsOn shows. No names, no reporter ids.

Host: `https://lightson.shadowstar.io`

Writes are not a public API.

## Read

| Method | Path | What |
| --- | --- | --- |
| GET | `/v1/health` | Worker up |
| GET | `/v1/occupancy` | Snapshot per listing |
| GET | `/v1/reports?venueId=` | Recent reports for one listing |
| GET | `/v1/outdoors` | Outdoor pockets |
| GET | `/v1/notes?venueId=` | Log-book lines (phrases only) |

Cached about a minute. CORS is open for GET. If you go too fast you will get **429**. Poll occupancy no faster than every 30 seconds.

Venue occupancy does not publish a headcount. Outdoors may show nearby/zone counts (99+). Reports last while posted hours are on, up to four hours.

Want a named client for another plugin? Talk to the maintainer. Do not POST.
