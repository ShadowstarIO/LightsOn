# LightsOn occupancy API

v1. HTTPS hostname required. No IPs.

Public window: **20 minutes**. Raw reports stay in D1 for hosts; GET never reads them.

## `GET /v1/occupancy`

Optional `dc`, `world`. Cached ~60s. Snapshot only: `venueId`, `state` (`happening` | `wrapped_up`), report counts, `updatedAt`, `expiresAt`.

## `POST /v1/reports`

`venueId`, `kind` (`happening` | `wrapped_up`), `reporterId`, `proof` (world/plot/inside/`thresholdMet`). No counts, no names. One report per reporter per venue per 20 minutes. Happening requires `thresholdMet`. Wrapped-up is rejected when `thresholdMet`.

## `GET /v1/notes?venueId=` / `POST /v1/notes`

Short log-book lines. POST only while that venue is `happening`. 80 characters, no links, one per reporter per venue per day, 14-day life, 12 notes kept.

## `GET /v1/outdoors` / `POST /v1/outdoors`

Pocket scenes. POST `pocket`, `world`, `place`, `tier`, `inCharacter`, optional `privateGathering`. GET hides pockets that pass the private-vote rule.

## Cost limits

- Occupancy/outdoors GET cached 60s
- Cron every 5 minutes (not every minute)
- Directory refresh at most every 30 minutes
- POST body cap 8 KB, ~80 POSTs/minute/isolate
- History pruned at 14 days
