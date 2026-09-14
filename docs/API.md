# LightsOn occupancy API

v1. HTTPS hostname required. No IPs.

Public window: **20 minutes**. Reporter ids never leave the worker.

## `GET /v1/occupancy`

Optional `dc`, `world`. Cached ~60s. Snapshot only: `venueId`, `state` (`happening` | `wrapped_up`), report counts, `updatedAt`, `expiresAt`.

## `GET /v1/reports?venueId=`

Last 20 minutes for one listing: time, kind, yard vs inside. No reporter ids.

## Writes

`POST` routes are not a public API. They require an ingest key compiled into the shipping plugin (not stored in git). Reading occupancy (`GET`) stays public.

## Cost limits

- Occupancy/outdoors GET cached 60s
- Cron every 5 minutes (not every minute)
- Directory refresh at most every 30 minutes
- POST body cap 8 KB, ~80 POSTs/minute/isolate
- History pruned at 14 days
