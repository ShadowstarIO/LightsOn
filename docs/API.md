# Occupancy API

Other plugins and sites can **GET** the same occupancy LightsOn shows. No names, no reporter ids.

Host: `https://on.xiv.run`

Writes are not a public API.

## Read

| Method | Path | What |
| --- | --- | --- |
| GET | `/v1/health` | Worker up |
| GET | `/v1/occupancy` | Full state per listing. Quiet is included only after two different networks report it and the score still comes out quiet. |
| GET | `/v1/snapshot` | The same list, stored and refreshed about every five minutes. |
| GET | `/v1/lit` | Venue ids that are lit. This list does not include quiet. |
| GET | `/v1/reports?venueId=` | Recent reports for one listing |
| GET | `/v1/outdoors` | Outdoor pockets |
| GET | `/v1/notes?venueId=` | Log-book lines (phrases only) |

Cached about a minute. CORS is open for GET. If you go too fast you will get **429**. Poll occupancy no faster than every 30 seconds.

Venue occupancy does not publish a headcount. Outdoors may show nearby/zone counts (99+). Reports last while posted hours are on, up to four hours.

Want a named client for another plugin? Talk to the maintainer. Do not POST.

## Open a plot window

If LightsOn is installed, call `LightsOn.OpenVenue` with a venue id. It opens the plot window and does not send a report. The call returns true when the player is standing on that listing. Reporting still happens only from the plot, with the same buttons.
