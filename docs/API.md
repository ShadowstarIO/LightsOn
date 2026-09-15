# LightsOn occupancy API

v1. HTTPS hostname required. No IPs.

Public window: **60 minutes** for lanterns, **45 minutes** for quiet. Cap 12 rows per listing. Log-book notes last **7 days**, cap 12. Reporter ids never leave the worker. One player send is one `INSERT`. Occupancy is derived on GET. About 30 POSTs/minute per isolate; plugin timers (~45s per action) should rarely hit that.

## `GET /v1/occupancy`

Cached ~60s. Snapshot only: `venueId`, `state` (`happening` | `wrapped_up` | `mixed`), layer counts, `doorLocked`, `updatedAt`, `expiresAt`. Lean is interior×2 + exterior, happening minus quiet.

## `GET /v1/reports?venueId=`

Last 60 minutes for one listing: time, kind, yard vs inside, lock/voice/glance/music chips. No reporter ids. Cap 12.

## `GET /v1/notes?venueId=`

Active log-book lines for one listing. Phrases only. No reporter ids.

## Writes

`POST` routes are not a public API. They require an ingest key compiled into the shipping plugin (not stored in git). Reading occupancy (`GET`) stays public.

## Cost limits

- Occupancy/outdoors GET cached 60s
- Cron every 5 minutes (not every minute)
- Directory refresh at most every 30 minutes
- POST body cap 8 KB, ~30 POSTs/minute/isolate
- History pruned at 14 days
