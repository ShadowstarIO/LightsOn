# LightsOn occupancy worker

GitHub Actions deploys this. You do not run Node or Wrangler on your PC.

One-time: create a Cloudflare API token (Workers Scripts Edit + D1 Edit) and save it as the GitHub Actions secret `CLOUDFLARE_API_TOKEN` on this repo. After that, a push to `worker/` (or Run workflow) publishes `https://lightson.wbro12-cloudflare.workers.dev`.
