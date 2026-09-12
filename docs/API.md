# LightsOn occupancy API

v1 contract. The plugin is the reporter. The website is a reader. Same payloads.

Base URL is HTTPS with a real certificate and a DNS hostname (Dalamud requirement). No IPs.

## States

| `state` | Meaning |
| --- | --- |
| `happening` | At least one valid report that the on-plot scan hit 3+ (after the reporter’s friend/FC filters) |
| `wrapped_up` | At least one valid report that the door could not be entered and the yard was empty |
| `unknown` | No fresh reports |

Never a headcount. Never a ranking. Show report counts and age only.

Happening outranks wrapped-up when both are still fresh. A plugin that just scanned 3+ must refuse `wrapped_up`.

Decay: drop a report after 45 minutes. A venue returns to `unknown` when nothing is left.

## `GET /v1/occupancy`

Optional query: `dc`, `world`.

```json
[
  {
    "venueId": "00Htdj43hypR",
    "state": "happening",
    "happeningReports": 3,
    "wrappedUpReports": 0,
    "updatedAt": "2026-09-12T06:00:00Z",
    "expiresAt": "2026-09-12T06:45:00Z"
  }
]
```

Public. No auth. Cached is fine.

## `POST /v1/reports`

```json
{
  "venueId": "00Htdj43hypR",
  "kind": "happening",
  "reporterId": "a1b2c3d4e5f6…",
  "at": "2026-09-12T06:00:00Z",
  "proof": {
    "world": "Cuchulainn",
    "district": "Mist",
    "ward": 14,
    "plot": 33,
    "subdivision": true,
    "inside": true,
    "thresholdMet": true
  }
}
```

`kind` is `happening` or `wrapped_up`.

`reporterId` is a random GUID created on the client. Not derived from a character or account. Resettable in Settings.

`proof.thresholdMet` is the only occupancy boolean. **Do not accept a raw count.** Reject `wrapped_up` when `thresholdMet` is true. Reject when `world` / plot does not match the listed venue.

Rate limit: one report per `reporterId` per venue per 15 minutes.

Plugin reports with matching `proof` are trusted. Website confirmations (no proof) may increment a weaker counter later; they must not lead the public state in v1.

## `GET /v1/venues` (optional)

If the occupancy host also mirrors listings. Otherwise the plugin reads `https://api.ffxivvenues.com/venue` for names and plots and only uses this API for occupancy.

## Buffering

Do not write every POST straight to the database. Ingest, coalesce by `venueId`, store the snapshot `{ state, happeningReports, wrappedUpReports, updatedAt, expiresAt }`. Public GET reads the snapshot.
