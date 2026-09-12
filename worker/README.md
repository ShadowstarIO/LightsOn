# LightsOn occupancy worker

Cloudflare Worker + D1. Public GET is the 20-minute snapshot. Raw reports stay in D1 for owners later.

From this folder, logged into Wrangler as the account that owns D1 `lightson`:

```
npm install
npx wrangler d1 execute lightson --remote --file=schema.sql
npx wrangler deploy
```

That prints the `*.workers.dev` URL. Paste it into the plugin Settings as the occupancy API URL.

`GET /v1/occupancy` and `POST /v1/reports` match [docs/API.md](../docs/API.md).
